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
        [Tooltip("Magnitude threshold for treating a scroll-wheel delta as an active 'scroll event'. Each non-zero event (in either direction) cycles selection by one step regardless of magnitude, so a Windows wheel notch arriving as ±120 raw units and a trackpad swipe arriving as small fractional values both behave the same — one notch / one swipe = one cycle.")]
        [SerializeField] float scrollDeadzone = 0.05f;

        // Cached so HandleSelectionInputs() doesn't allocate a fresh
        // array every Update — keeps the hot path GC-free.
        static readonly Key[] DigitKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
            Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
        };

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
        // Tracks the sign of the previous frame's scroll-event state.
        // Used by edge detection: we only fire a cycle when this
        // changes from 0 → non-zero, so one wheel notch / one trackpad
        // swipe yields one cycle even though it may span many frames.
        int _lastScrollSign;

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
            // Pause + weapon-presence gating.
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) return;
            if (!HasWeapons) return;

            // Auto-switch off a fully-dead selected type. Runs before the
            // pointer-over-UI gate — a weapon dying must move selection
            // regardless of where the cursor is.
            AutoSwitchOffDeadType();

            // UI gating — selection/fire input only when not over the HUD.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            HandleSelectionInputs();
            HandleFireInput();
        }

        // If the selected type is fully dead, move selection to the
        // nearest live type. No-op when the selection is live or when no
        // live type remains (the player simply cannot fire).
        void AutoSwitchOffDeadType()
        {
            WeaponTypeGroup selected = SelectedType;
            if (selected == null || !selected.IsFullyDead) return;
            CycleSelected(1);
        }

        void HandleSelectionInputs()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                int max = Mathf.Min(DigitKeys.Length, _types.Count);
                for (int i = 0; i < max; i++)
                {
                    if (kb[DigitKeys[i]].wasPressedThisFrame)
                    {
                        SetSelected(i);
                        break;
                    }
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                // Edge detection rather than an accumulator: classify
                // the current scroll delta into {-1, 0, +1} and only
                // cycle on transitions from 0 → ±1. A single wheel
                // notch (which arrives as a brief ±120 spike on
                // Windows or ±1 on macOS depending on Input System
                // version) collapses to one cycle. A continuous
                // trackpad swipe also produces one cycle until the
                // user pauses — fine for a 2-3-weapon roster.
                float scrollY = mouse.scroll.ReadValue().y;
                int sign = Mathf.Abs(scrollY) > scrollDeadzone
                    ? (scrollY > 0f ? 1 : -1)
                    : 0;
                if (sign != 0 && sign != _lastScrollSign) CycleSelected(sign);
                _lastScrollSign = sign;
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
                if (w != null && w.IsAlive) w.TryFire(target);
            }
        }

        public void SetSelected(int i)
        {
            if (i < 0 || i >= _types.Count) return;
            // Cannot select a fully-dead type. Centralises the rule for
            // digit keys and button clicks; CycleSelected and auto-switch
            // always pass a live index, so the guard never blocks them.
            if (_types[i].IsFullyDead) return;
            if (i == _selectedTypeIndex) return;
            _selectedTypeIndex = i;
            Debug.unityLogger.Log(TAG, $"Selected weapon type index {i} ({_types[i].Shape.displayName}).");
            SelectedChanged?.Invoke(_selectedTypeIndex);
        }

        // Step selection by `delta`, skipping past fully-dead types to the
        // next live one. Scans up to Types.Count steps; if no live type
        // exists, selection is left unchanged.
        public void CycleSelected(int delta)
        {
            if (_types.Count == 0) return;
            int step = delta >= 0 ? 1 : -1;
            int next = _selectedTypeIndex;
            for (int scanned = 0; scanned < _types.Count; scanned++)
            {
                next = (next + step + _types.Count) % _types.Count;
                if (!_types[next].IsFullyDead)
                {
                    SetSelected(next);
                    return;
                }
            }
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

        // Instances still alive — non-null (excludes Unity-destroyed
        // cubes) and IsAlive (excludes cubes mid death-drift at 0 HP).
        public int AliveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Instances.Count; i++)
                {
                    WeaponBehavior w = Instances[i];
                    if (w != null && w.IsAlive) n++;
                }
                return n;
            }
        }

        // Every instance of this type is dead. A group always has >=1
        // instance (RegisterWeapons only creates a group for a member).
        public bool IsFullyDead => AliveCount == 0;

        // Some but not all instances are dead — only meaningful for a
        // multi-instance type.
        public bool IsPartiallyDead =>
            Instances.Count > 1 && AliveCount > 0 && AliveCount < Instances.Count;
    }
}
