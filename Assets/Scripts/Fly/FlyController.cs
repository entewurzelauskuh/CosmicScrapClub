using System.Collections.Generic;
using CubeFly.Core;
using CubeFly.Input;
using UnityEngine;

namespace CubeFly.Fly
{
    // Rigidbody-driven construct flight. The construct GameObject
    // carries a non-kinematic Rigidbody (useGravity=false, continuous
    // collision detection); each placed cube prefab brings its own
    // BoxCollider. Together they form a compound rigid body — Unity
    // moves the parent, the cube colliders all move with it, and
    // collisions are handled by physics (bouncing off the ground,
    // recoil from world cubes) plus our FlyCrashHandler for the
    // damage side.
    //
    // Linear motion: AddForce(thrustForce * worldThrustDir, Force)
    // each FixedUpdate. Mass affects acceleration naturally via F=ma
    // — heavier ships are sluggish to spin up. linearVelocity is hard-
    // clamped to maxSpeed so a long burn doesn't overshoot.
    //
    // Rotation: AddRelativeTorque for pitch/roll (local axes),
    // AddTorque(Vector3.up) for yaw (world axis, keeps "left/right"
    // intuitive when pitched). Mass affects rotation via the inertia
    // tensor Unity computes from the compound collider — heavier ship
    // turns slower for the same torque, opening the future "build more
    // gyros to turn faster" gameplay. Two knobs balance this:
    //
    //   • rotationMassCompensation — applied torque is multiplied by
    //     mass^p so heavy ships don't crawl. p ≈ 0.7 gives a ~2.5×
    //     spread across a 20× mass range, instead of the ~20× that
    //     raw F=Iα would produce.
    //   • maxAngularSpeed — hard cap on angular velocity magnitude
    //     (Rigidbody.maxAngularVelocity). Light ships have tiny
    //     inertia tensors and would otherwise spin uncontrollably;
    //     the cap stops that. Heavy ships never reach the cap.
    //
    // Rigidbody.angularDamping is set to a substantial value so
    // rotation decays to rest when input is released — we explicitly
    // do NOT want Kerbal-style endless drift.
    public class FlyController : MonoBehaviour
    {
        [Header("Registries (decoupled shape × material)")]
        [SerializeField] ShapeRegistry shapeRegistry;
        [SerializeField] MaterialRegistry materialRegistry;

        [SerializeField] GameObject alphaCubePrefab;
        [SerializeField] Transform construct;

        [Header("Shooting wiring")]
        [Tooltip("Auto-wired in Start if left null. Receives the list of WeaponBehavior instances we spawn during BuildConstruct.")]
        [SerializeField] FlyShootingController shootingController;

        // Public so FlyCrosshair and FlyShootingController can read the
        // construct's transform without a private-field hack.
        public Transform Construct => construct;

        // Collected during BuildConstruct, handed off to FlyShootingController
        // so it can group weapons by ShapeDefinition for selection + dispatch.
        readonly List<WeaponBehavior> _spawnedWeapons = new();

        [Header("Linear thrust (Rigidbody.AddForce)")]
        [Tooltip("Force in Newtons applied per FixedUpdate while thrust input is held. Mass affects acceleration: accel = thrustForce / Rigidbody.mass. Starting value tuned for a ~25-mass construct; expect to retune.")]
        [SerializeField] float thrustForce = 100f;
        [Tooltip("Hard cap on Rigidbody.linearVelocity magnitude. Independent of mass — heavy ships just take longer to reach it.")]
        [SerializeField] float maxSpeed = 37.5f;

        [Header("Rotation (Rigidbody.AddTorque)")]
        [Tooltip("Pitch torque in Newton-metres applied per FixedUpdate while pitch input is held. Multiplied by the mass-compensation factor below before being applied.")]
        [SerializeField] float pitchTorque = 3f;
        [Tooltip("Yaw torque (world Y axis — avoids roll coupling when the ship is pitched).")]
        [SerializeField] float yawTorque = 3f;
        [Tooltip("Roll torque (local Z axis).")]
        [SerializeField] float rollTorque = 3f;
        [Tooltip("Exponent on Rigidbody.mass that scales applied torque, compensating for the inertia tensor's mass dependence. 0 = no compensation (heavy ships crawl, light ships spin), 1 = full compensation (mass-independent rotation), 0.7 = arcade middle ground where heavy ships still feel heavier but the spread is ~2.5× across a 20× mass range instead of ~20×. Future 'build more gyros' cubes add raw torque on top.")]
        [SerializeField] float rotationMassCompensation = 0.7f;
        [Tooltip("Hard cap on Rigidbody.angularVelocity magnitude in rad/s, set via Rigidbody.maxAngularVelocity. Unity's default of 7 rad/s (~400°/s) is too high for arcade flight — light ships would spin uncontrollably because their inertia tensor is tiny. 3 rad/s ≈ 172°/s is responsive but controllable; heavier ships' natural terminal angular velocity falls well below this cap so the limit only ever affects light ships.")]
        [SerializeField] float maxAngularSpeed = 3f;

        [Header("Rigidbody tuning (also editable on the Rigidbody component directly)")]
        [Tooltip("Applied to the Rigidbody at Start. Linear drag — small value, since maxSpeed clamp does the hard cap.")]
        [SerializeField] float linearDrag = 0.5f;
        [Tooltip("Applied to the Rigidbody at Start. Angular drag — substantial, so rotation decays back to rest when input is released. We explicitly DO NOT want endless space-physics spin.")]
        [SerializeField] float angularDrag = 3f;

        [Header("Minimum responsiveness floor")]
        [Tooltip("Above this mass, applied thrust force and torque are scaled UP by mass/maxResponsivenessMass so a very heavy build (e.g. a maxed Tank) doesn't become an unflyable brick. Below it, mass fights flight normally. Effectively: anything heavier than this flies/turns as if it were this mass.")]
        [SerializeField] float maxResponsivenessMass = 100f;

        CubeFlyInputActions _input;

        // Per-frame input snapshots, sampled in Update, applied in FixedUpdate.
        Vector3 _thrustInput;   // x = strafe (+R/-L), y = vertical (+U/-D), z = forward (+F/-B)
        float _pitchInput;      // -1 / 0 / +1 (Down / none / Up arrow)
        float _yawInput;        // -1 / 0 / +1 (Left / none / Right arrow)
        float _rollInput;       // -1 / 0 / +1 (Q / none / E)

        // Cached at Start. The construct's Rigidbody is the source of
        // truth for position / rotation / velocity from here on; we
        // never write to construct.transform directly in Update or
        // FixedUpdate after this refactor.
        Rigidbody _rb;

        // Per-session flight factors, all cached in ResolveRigidbody —
        // mass and ship class are fixed for the lifetime of a Fly
        // session, so recomputing them each FixedUpdate would just burn
        // Pow calls.
        //
        // _linearForceFactor — multiplies thrustForce. Folds in the
        //   ship-class movement multiplier and the responsiveness-floor
        //   over-cap ratio.
        // _torqueFactor — multiplies the pitch/yaw/roll torques. Folds
        //   in three things: the class multiplier; the inertia
        //   compensation `effectiveMass^rotationMassCompensation` where
        //   effectiveMass = min(rb.mass, maxResponsivenessMass) — note
        //   it is the CAPPED mass, not rb.mass; and the over-cap ratio
        //   applied separately on top. See ResolveRigidbody for the
        //   exact composition.
        float _linearForceFactor = 1f;
        float _torqueFactor = 1f;

        const string TAG = "FlyController";

        void Awake()
        {
            _input = new CubeFlyInputActions();
            Debug.unityLogger.Log(TAG, "FlyController initialised.");
        }

        void OnEnable() => _input.Fly.Enable();
        void OnDisable() => _input.Fly.Disable();
        void OnDestroy() => _input?.Dispose();

        void Start()
        {
            BuildConstruct();
            int total = GameData.PlacedCubes.Count + 1; // +1 for the alpha cube
            Debug.unityLogger.Log(TAG, $"FlyScene ready. Construct rebuilt: {total} cube(s) (including alpha). Weapons: {_spawnedWeapons.Count}.");
            Debug.unityLogger.Log(TAG, $"Construct initial position: {construct.position}");

            ResolveRigidbody();

            // Hand the weapon list to the shooting controller so it can
            // group by ShapeDefinition for selection + dispatch.
            if (shootingController == null) shootingController = FindAnyObjectByType<FlyShootingController>();
            if (shootingController != null) shootingController.RegisterWeapons(_spawnedWeapons);
            else Debug.unityLogger.LogWarning(TAG, "No FlyShootingController in scene; weapons won't fire.");
        }

        // Resolve the Rigidbody on the construct, configure it, and
        // set mass from the actual placed cubes. Setting rb.mass after
        // child colliders are attached makes Unity recompute the
        // inertia tensor from the current compound collider — heavier,
        // more spread-out constructs naturally feel sluggier in
        // rotation.
        //
        // Caveat: rb.mass is set ONCE here. Cube destruction mid-flight
        // doesn't update it. Acceptable for v1; future improvement
        // would re-set mass on CubeDeath / spawn events.
        void ResolveRigidbody()
        {
            if (construct == null)
            {
                Debug.unityLogger.LogError(TAG, "No construct Transform assigned — flight disabled.");
                return;
            }
            _rb = construct.GetComponent<Rigidbody>();
            if (_rb == null)
            {
                Debug.unityLogger.LogError(TAG,
                    "CubeConstruct has no Rigidbody. Add one in the scene (the Rigidbody-driven flight refactor expects it).");
                return;
            }

            _rb.useGravity = false;
            _rb.linearDamping = linearDrag;
            _rb.angularDamping = angularDrag;
            _rb.maxAngularVelocity = maxAngularSpeed;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            float totalMass = ComputeTotalMass();
            // Mass below 1 makes the inertia tensor effectively zero
            // and produces wildly unstable rotation. Floor at 1.
            _rb.mass = Mathf.Max(1f, totalMass);

            // --- Flight factors ---
            // Ship class movement multiplier (Tank slower, Scout faster).
            float movementMultiplier = ShipClasses.StatsFor(GameData.ActiveShipClass).MovementMultiplier;

            // Responsiveness floor: above maxResponsivenessMass, scale
            // applied force/torque UP by mass/cap so accel and turn
            // rate flatten instead of dropping toward zero. Below the
            // cap, overCap == 1 and mass fights flight normally.
            float overCap = Mathf.Max(1f, _rb.mass / Mathf.Max(1f, maxResponsivenessMass));
            float effectiveMass = Mathf.Min(_rb.mass, maxResponsivenessMass);

            _linearForceFactor = movementMultiplier * overCap;
            // effectiveMass^comp is the inertia compensation (heavy ships
            // need more torque); overCap is the floor; movementMultiplier
            // is the class lever. See the field comments + the rotation
            // block in FixedUpdate.
            _torqueFactor = movementMultiplier
                          * Mathf.Pow(effectiveMass, rotationMassCompensation)
                          * overCap;

            Debug.unityLogger.Log(TAG,
                $"Rigidbody armed. Total mass: {totalMass:F1} (rb.mass: {_rb.mass:F1}). " +
                $"Ship class {GameData.ActiveShipClass} → movement ×{movementMultiplier:F2}. " +
                $"linearForceFactor={_linearForceFactor:F2}, torqueFactor={_torqueFactor:F2} " +
                $"(overCap ×{overCap:F2}, compensation exp={rotationMassCompensation:F2}). " +
                $"linearDrag={linearDrag:F1}, angularDrag={angularDrag:F1}.");
        }

        float ComputeTotalMass()
        {
            // Sum the alpha cube's mass + every placed cube's resolved
            // material mass. Mirrors the pre-refactor formula.
            float alphaMass = 1f;
            if (alphaCubePrefab != null)
            {
                CubeStats stats = alphaCubePrefab.GetComponent<CubeStats>();
                if (stats != null && !Mathf.Approximately(stats.mass, 0f)) alphaMass = stats.mass;
            }
            return alphaMass + GameData.SumPlacedMasses(shapeRegistry, materialRegistry);
        }

        void BuildConstruct()
        {
            if (construct == null) construct = transform;

            if (alphaCubePrefab != null)
            {
                GameObject alpha = Instantiate(alphaCubePrefab, construct);
                alpha.transform.localPosition = Vector3.zero;
                alpha.transform.localRotation = Quaternion.identity;
                // Apply the ship class's alpha HP — Tank tougher, Scout
                // more fragile. Overrides the prefab's CubeStats default.
                CubeStats alphaStats = alpha.GetComponent<CubeStats>();
                if (alphaStats != null)
                    alphaStats.healthPoints = ShipClasses.StatsFor(GameData.ActiveShipClass).AlphaHealthPoints;
            }

            if (shapeRegistry == null)
            {
                Debug.unityLogger.LogError(TAG, "No ShapeRegistry assigned on FlyController; placed cubes won't be rebuilt.");
                return;
            }

            for (int i = 0; i < GameData.PlacedCubes.Count; i++)
            {
                Placement p = GameData.PlacedCubes[i];
                ShapeDefinition shape = shapeRegistry.Get(p.ShapeIndex);
                if (shape == null || shape.prefab == null)
                {
                    Debug.unityLogger.LogWarning(TAG, $"Skipping {p.Cell}: no prefab for shape index {p.ShapeIndex}.");
                    continue;
                }
                GameObject go = Instantiate(shape.prefab, construct);
                go.transform.localPosition = new Vector3(p.Cell.x, p.Cell.y, p.Cell.z);
                // Apply the orientation chosen at build-time so each cube
                // keeps its placed pose relative to the construct.
                go.transform.localRotation = p.Rotation;
                // ResolveMaterial picks the right MaterialDefinition for
                // the placement's shape category — armour pulls from
                // the registry by index, weapons return their coupled
                // weaponMaterial.
                MaterialDefinition mdef = shape.ResolveMaterial(p.MaterialIndex, materialRegistry);
                mdef?.ApplyTo(go);

                // Collect any WeaponBehavior on this placement — wire
                // it to the construct (so weapons can compare their
                // local rotation against construct.forward) and to
                // the source ShapeDefinition (so the toolbar can group
                // and label by shape).
                WeaponBehavior weapon = go.GetComponent<WeaponBehavior>();
                if (weapon != null)
                {
                    weapon.Construct = construct;
                    weapon.Shape = shape;
                    _spawnedWeapons.Add(weapon);
                }
            }
        }

        void Update()
        {
            // Pause overlay catches gameplay input. Time.timeScale = 0
            // already freezes FixedUpdate (so AddForce/AddTorque don't
            // run); this guard zeroes the per-frame input sample so the
            // controller doesn't pile up input across pause boundaries.
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen)
            {
                _thrustInput = Vector3.zero;
                _pitchInput  = 0f;
                _yawInput    = 0f;
                _rollInput   = 0f;
                return;
            }

            // Sample the input every frame; physics-paced application happens
            // in FixedUpdate.
            _thrustInput = _input.Fly.Thrust.ReadValue<Vector3>();
            _pitchInput  = _input.Fly.Pitch.ReadValue<float>();
            _yawInput    = _input.Fly.Yaw.ReadValue<float>();
            _rollInput   = _input.Fly.Roll.ReadValue<float>();
        }

        void FixedUpdate()
        {
            if (_rb == null) return;

            // Linear thrust — local-frame input rotated into world frame,
            // then applied as continuous force. ForceMode.Force integrates
            // over dt internally; multiplying by Time.fixedDeltaTime here
            // would double-integrate and is a classic Rigidbody bug.
            if (_thrustInput.sqrMagnitude > 0f)
            {
                Vector3 worldThrust = construct.right   * _thrustInput.x +
                                      construct.up      * _thrustInput.y +
                                      construct.forward * _thrustInput.z;
                _rb.AddForce(worldThrust * (thrustForce * _linearForceFactor), ForceMode.Force);
            }

            // Hard speed cap. linearVelocity is the Unity 6 name; pre-6
            // would be `velocity`. We accept whatever drag has already done
            // and clamp on top so a long burn doesn't blow past maxSpeed.
            Vector3 v = _rb.linearVelocity;
            if (v.sqrMagnitude > maxSpeed * maxSpeed)
                _rb.linearVelocity = v.normalized * maxSpeed;

            // Rotation: torque is scaled by `_torqueFactor`, which folds
            // in the ship-class movement multiplier, the inertia
            // compensation `effectiveMass^rotationMassCompensation`
            // (effectiveMass is rb.mass capped at maxResponsivenessMass),
            // and the responsiveness-floor over-cap ratio applied
            // separately. See ResolveRigidbody for the composition.

            // Pitch: local X. Up arrow → nose up → torque around local -X.
            if (_pitchInput != 0f)
                _rb.AddRelativeTorque(Vector3.right * (-_pitchInput * pitchTorque * _torqueFactor), ForceMode.Force);

            // Yaw: world Y. Right arrow → yaw right → positive world-Y torque.
            // World-space yaw avoids roll coupling when the ship is pitched.
            if (_yawInput != 0f)
                _rb.AddTorque(Vector3.up * (_yawInput * yawTorque * _torqueFactor), ForceMode.Force);

            // Roll: local Z. Q (input = -1, anti-clockwise from pilot POV) →
            // positive local-Z torque; E reverses.
            if (_rollInput != 0f)
                _rb.AddRelativeTorque(Vector3.forward * (-_rollInput * rollTorque * _torqueFactor), ForceMode.Force);
        }
    }
}
