using System;
using System.Collections.Generic;
using CubeFly.Core;
using CubeFly.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CubeFly.Fly
{
    // One per FlyScene — sibling component of FlyController. Owns:
    //   • The list of weapons grouped by ShapeDefinition.
    //   • The currently-selected weapon-type index.
    //   • All shoot-related input polling: Fire (LMB) and selection
    //     (mouse scroll wheel, digit keys 1–9).
    //   • Dispatch: each frame Fire is held, call TryFire on every
    //     weapon of the selected type. The per-weapon cooldown
    //     throttles the actual firing rate.
    //
    // Subscribers (FlyWeaponToolbarController) react to TypesChanged
    // and SelectedChanged events.
    public class FlyShootingController : MonoBehaviour
    {
        [SerializeField] FlyController flyController;
        [Tooltip("Same value the FlyCrosshair UI uses. The on-screen reticle and the fire dispatch must agree, so keep these in sync.")]
        [SerializeField] float aimRange = 100f;
        [Tooltip("Scroll-wheel accumulator drained per selection change. Higher = less sensitive. 1.0 works for wheel notches; trackpads send smaller deltas that build up over time.")]
        [SerializeField] float scrollPerSelectionTick = 1f;

        public event Action TypesChanged;
        public event Action<int> SelectedChanged;

        readonly List<WeaponTypeGroup> _types = new();
        public IReadOnlyList<WeaponTypeGroup> Types => _types;

        int _selectedTypeIndex = -1;
        public int SelectedTypeIndex => _selectedTypeIndex;
        public bool HasWeapons => _types.Count > 0;
        public WeaponTypeGroup SelectedType =>
            (_selectedTypeIndex >= 0 && _selectedTypeIndex < _types.Count) ? _types[_selectedTypeIndex] : null;

        CubeFlyInputActions _input;
        float _scrollAccumulator;

        const string TAG = "FlyShooting";

        void Awake()
        {
            _input = new CubeFlyInputActions();
        }

        void OnEnable() => _input.Fly.Enable();
        void OnDisable() => _input.Fly.Disable();
        void OnDestroy() => _input?.Dispose();

        // Called by FlyController.Start once BuildConstruct has finished
        // instantiating placed shapes. Groups the supplied weapon
        // instances by ShapeDefinition so the toolbar can render one
        // entry per distinct type. Defaults the active selection to
        // the first available type. Fires TypesChanged + SelectedChanged
        // so the toolbar UI rebuilds and highlights correctly.
        public void RegisterWeapons(IEnumerable<WeaponBehavior> weapons)
        {
            _types.Clear();
            Dictionary<ShapeDefinition, WeaponTypeGroup> byShape = new();
            foreach (WeaponBehavior w in weapons)
            {
                if (w == null || w.Shape == null) continue;
                if (!byShape.TryGetValue(w.Shape, out WeaponTypeGroup g))
                {
                    g = new WeaponTypeGroup(w.Shape);
                    byShape[w.Shape] = g;
                    _types.Add(g);
                }
                g.Instances.Add(w);
            }
            _selectedTypeIndex = _types.Count > 0 ? 0 : -1;
            Debug.unityLogger.Log(TAG,
                $"Registered {_types.Count} weapon type(s) across {CountInstances()} instance(s).");
            TypesChanged?.Invoke();
            SelectedChanged?.Invoke(_selectedTypeIndex);
        }

        int CountInstances()
        {
            int n = 0;
            for (int i = 0; i < _types.Count; i++) n += _types[i].Instances.Count;
            return n;
        }

        void Update()
        {
            // Pause + UI gating, same pattern as BuildManager.
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (!HasWeapons) return;

            HandleSelectionInputs();
            HandleFireInput();
        }

        void HandleSelectionInputs()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
                                 Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
                int max = Mathf.Min(digits.Length, _types.Count);
                for (int i = 0; i < max; i++)
                {
                    if (kb[digits[i]].wasPressedThisFrame)
                    {
                        SetSelected(i);
                        break;
                    }
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                _scrollAccumulator += mouse.scroll.ReadValue().y;
                while (_scrollAccumulator >= scrollPerSelectionTick)
                {
                    CycleSelected(+1);
                    _scrollAccumulator -= scrollPerSelectionTick;
                }
                while (_scrollAccumulator <= -scrollPerSelectionTick)
                {
                    CycleSelected(-1);
                    _scrollAccumulator += scrollPerSelectionTick;
                }
            }
        }

        void HandleFireInput()
        {
            if (!_input.Fly.Fire.IsPressed()) return;
            if (flyController == null) return;
            Transform construct = flyController.Construct;
            if (construct == null) return;

            Vector3 target = construct.position + construct.forward * aimRange;
            WeaponTypeGroup active = _types[_selectedTypeIndex];
            for (int i = 0; i < active.Instances.Count; i++)
            {
                WeaponBehavior w = active.Instances[i];
                if (w != null) w.TryFire(target);
            }
        }

        public void SetSelected(int i)
        {
            if (i < 0 || i >= _types.Count) return;
            if (i == _selectedTypeIndex) return;
            _selectedTypeIndex = i;
            Debug.unityLogger.Log(TAG, $"Selected weapon type index {i} ({_types[i].Shape.displayName}).");
            SelectedChanged?.Invoke(_selectedTypeIndex);
        }

        public void CycleSelected(int delta)
        {
            if (_types.Count == 0) return;
            int next = (_selectedTypeIndex + delta + _types.Count) % _types.Count;
            SetSelected(next);
        }
    }

    // One per distinct weapon ShapeDefinition. Tracks every instance
    // of that type on the construct. The first instance's reload state
    // is used to drive the shared progress bar — since all instances of
    // a type share the same reloadSeconds and all fire on the same
    // frame, they stay synchronised.
    public class WeaponTypeGroup
    {
        public ShapeDefinition Shape { get; }
        public List<WeaponBehavior> Instances { get; } = new();

        public WeaponTypeGroup(ShapeDefinition shape) { Shape = shape; }

        public float MaxReloadSeconds => Instances.Count > 0 ? Instances[0].ReloadSeconds : 0f;
        public float CooldownRemaining => Instances.Count > 0 ? Instances[0].CooldownRemaining : 0f;

        // 0 = just fired, 1 = ready to fire. Drives the reload progress bar.
        public float ReadyFraction
        {
            get
            {
                float r = MaxReloadSeconds;
                if (r <= 0f) return 1f;
                return 1f - Mathf.Clamp01(CooldownRemaining / r);
            }
        }
    }
}
