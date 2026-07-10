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

        [Header("Laser heat (shared per laser weapon-type)")]
        [Tooltip("Heat units added per second while a laser of the selected type is firing. 100 = overheated. At 50/s a cold laser overheats after ~2 s of sustained fire.")]
        [SerializeField] float heatRisePerSecond = 50f;
        [Tooltip("Heat units shed per second when not firing (and not overheated).")]
        [SerializeField] float heatFallPerSecond = 30f;
        [Tooltip("Heat units shed per second while overheated — the slow lockout recovery. The laser stays locked until heat returns to 0.")]
        [SerializeField] float heatFallOverheatedPerSecond = 15f;

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

        // Resolved in RegisterWeapons. The laser is the weapon-tier power
        // consumer; FlyShootingController allocates AvailableForWeapons
        // across firing laser cubes.
        ConstructEnergySystem _energy;
        // Set true by HandleFireInput on a frame the SELECTED laser type
        // actually beamed (>=1 cube powered + fired); consumed by
        // TickLaserHeat to decide rise vs cool. Reset each Update.
        bool _selectedLaserFiredThisFrame;

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
        public void RegisterWeapons(IEnumerable<WeaponBehavior> weapons, ConstructEnergySystem energy)
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
            _energy = energy; // the construct's own system, passed by FlyController — not a scene-wide find (AP-11)
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

            _selectedLaserFiredThisFrame = false; // HandleFireInput may set it

            // Auto-switch off a fully-dead selected type. Runs before the
            // pointer-over-UI gate — a weapon dying must move selection
            // regardless of where the cursor is.
            AutoSwitchOffDeadType();

            // Selection input (digits, mouse wheel) is allowed even when
            // the cursor is over the weapon toolbar — scrolling on the
            // toolbar is the natural place to cycle weapons. Fire (LMB)
            // is the only input that conflicts with UI clicks and stays
            // gated by the pointer-over-UI check below. Heat must still
            // tick (cool) when over UI, so the fire dispatch is conditional
            // but TickLaserHeat below always runs.
            HandleSelectionInputs();

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!overUI) HandleFireInput();

            TickLaserHeat();
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

            WeaponTypeGroup active = _types[_selectedTypeIndex];
            Vector3 target = construct.position + construct.forward * aimRange;

            if (active.IsLaser)
            {
                // Overheated lasers are locked out entirely (heat still
                // cools in TickLaserHeat).
                if (active.Overheated) return;

                // Power-gate: the laser is the weapon-tier consumer. Power
                // floor(available / per-cube draw) of the alive lasers; the
                // rest don't fire this frame and turn their beam off in
                // LaserWeapon.LateUpdate.
                float drawPer = active.LaserPowerDraw;
                float available = _energy != null ? _energy.AvailableForWeapons : 0f;
                int budget = drawPer > 0f ? Mathf.FloorToInt(available / drawPer) : int.MaxValue;

                int fired = 0;
                for (int i = 0; i < active.Instances.Count; i++)
                {
                    WeaponBehavior w = active.Instances[i];
                    if (w == null || !w.IsAlive) continue;
                    if (fired >= budget) continue;
                    w.TryFire(target); // laser ignores target, beams along its barrel
                    fired++;
                }
                _selectedLaserFiredThisFrame = fired > 0;
            }
            else
            {
                for (int i = 0; i < active.Instances.Count; i++)
                {
                    WeaponBehavior w = active.Instances[i];
                    if (w != null && w.IsAlive) w.TryFire(target);
                }
            }
        }

        // Tick shared heat for every laser type each frame: the selected
        // type rises while it's firing, everything else (and the selected
        // type when idle) cools. Overheat latches at 100 and clears at 0.
        void TickLaserHeat()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _types.Count; i++)
            {
                WeaponTypeGroup t = _types[i];
                if (!t.IsLaser) continue;

                bool rising = (i == _selectedTypeIndex) && _selectedLaserFiredThisFrame;
                if (rising)
                    t.Heat = Mathf.Min(100f, t.Heat + heatRisePerSecond * dt);
                else
                    t.Heat = Mathf.Max(0f, t.Heat -
                        (t.Overheated ? heatFallOverheatedPerSecond : heatFallPerSecond) * dt);

                if (!t.Overheated && t.Heat >= 100f) t.Overheated = true;
                else if (t.Overheated && t.Heat <= 0f) t.Overheated = false;
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

        // True when this weapon type is a laser with living instances that
        // currently can't afford even one shot (spare weapon power < per-cube
        // draw). Non-laser types never energy-starve; a fully-dead type reports
        // false (that's the dead state, shown separately by the toolbar).
        // Mirrors the exact gate HandleFireInput uses to power lasers.
        public bool GroupEnergyStarved(int index)
        {
            if (index < 0 || index >= _types.Count) return false;
            WeaponTypeGroup t = _types[index];
            if (!t.IsLaser || t.IsFullyDead) return false;
            float draw = t.LaserPowerDraw;
            if (draw <= 0f) return false;
            float available = _energy != null ? _energy.AvailableForWeapons : 0f;
            return available < draw;
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

        // Shared heat for a laser type (0..100). Ticked by
        // FlyShootingController; meaningless for non-laser types.
        public float Heat;
        // Latched at heat 100, cleared at 0 — while true the type is fire-
        // locked.
        public bool Overheated;

        bool _isLaserResolved;
        bool _isLaser;
        // True when this type's instances are LaserWeapons. Cached — a
        // type's weapon class never changes for a Fly session.
        public bool IsLaser
        {
            get
            {
                if (!_isLaserResolved)
                {
                    for (int i = 0; i < Instances.Count; i++)
                        if (Instances[i] is LaserWeapon) { _isLaser = true; break; }
                    _isLaserResolved = true;
                }
                return _isLaser;
            }
        }

        // Per-cube power draw of this laser type (0 for non-lasers). Read
        // from a representative LaserWeapon instance.
        public float LaserPowerDraw
        {
            get
            {
                for (int i = 0; i < Instances.Count; i++)
                    if (Instances[i] is LaserWeapon lw) return lw.PowerDraw;
                return 0f;
            }
        }

        // Route reload-bar inputs through the first ALIVE instance.
        // Reading Instances[0] unconditionally was wrong when the first
        // weapon cube of the group died: its _cooldown decays to 0 during
        // the death drift while the surviving instances fire and hold a
        // real cooldown, so the bar appeared to "always be full."
        public float MaxReloadSeconds
        {
            get
            {
                WeaponBehavior alive = FirstAliveInstance();
                return alive != null ? alive.ReloadSeconds : 0f;
            }
        }
        public float CooldownRemaining
        {
            get
            {
                WeaponBehavior alive = FirstAliveInstance();
                return alive != null ? alive.CooldownRemaining : 0f;
            }
        }

        WeaponBehavior FirstAliveInstance()
        {
            for (int i = 0; i < Instances.Count; i++)
            {
                WeaponBehavior w = Instances[i];
                if (w != null && w.IsAlive) return w;
            }
            return null;
        }

        // 0 = just fired / fully heated, 1 = ready / cold. Drives the
        // toolbar bar. For a laser the bar shows remaining heat capacity
        // (1 - heat); for a projectile weapon it shows reload progress.
        public float ReadyFraction
        {
            get
            {
                if (IsLaser) return 1f - Mathf.Clamp01(Heat / 100f);
                float r = MaxReloadSeconds;
                if (r <= 0f) return 1f;
                return 1f - Mathf.Clamp01(CooldownRemaining / r);
            }
        }

        // Instances still alive — non-null (excludes Unity-destroyed
        // cubes) and IsAlive (excludes cubes mid death-drift at 0 HP).
        // O(n); prefer GetDeadState when both dead-state flags are needed.
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

        // Both dead-state flags from a SINGLE AliveCount scan. The weapon
        // toolbar polls liveness every frame for every group, so deriving
        // both flags together keeps that to one Instances walk per group
        // per frame instead of one walk per flag read.
        public void GetDeadState(out bool fullyDead, out bool partiallyDead)
        {
            int alive = AliveCount;
            fullyDead = alive == 0;
            partiallyDead = Instances.Count > 1 && alive > 0 && alive < Instances.Count;
        }

        // Every instance of this type is dead. A group always has >=1
        // instance (RegisterWeapons only creates a group for a member).
        public bool IsFullyDead
        {
            get { GetDeadState(out bool fullyDead, out _); return fullyDead; }
        }

        // Some but not all instances are dead — only meaningful for a
        // multi-instance type.
        public bool IsPartiallyDead
        {
            get { GetDeadState(out _, out bool partiallyDead); return partiallyDead; }
        }
    }
}
