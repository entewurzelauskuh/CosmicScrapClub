using CubeFly.Core;
using CubeFly.Input;
using UnityEngine;

namespace CubeFly.Fly
{
    public class FlyController : MonoBehaviour
    {
        [SerializeField] CubeTypeRegistry cubeTypeRegistry;
        [SerializeField] GameObject alphaCubePrefab;
        [SerializeField] Transform construct;

        // Bumped 50% over the originals (6 / 25 / 45 / 45 / 60). The actual
        // values applied per FixedUpdate are scaled by `_massMultiplier`,
        // which trends from 1.0 (alpha alone) to 0.1 at the build mass cap.
        [SerializeField] float accelerationRate = 9f;
        [SerializeField] float maxSpeed = 37.5f;
        [SerializeField] float drag = 2f;
        [SerializeField] float pitchSensitivity = 67.5f;
        [SerializeField] float yawSensitivity = 67.5f;
        [SerializeField] float rollSpeed = 90f;

        [Header("Mass-driven slowdown")]
        [Tooltip("Mass at or below which there's no slowdown (multiplier = 1.0).")]
        [SerializeField] float baseMassThreshold = 10f;
        [Tooltip("Mass at which slowdown caps (multiplier = 1 - maxSlowdown).")]
        [SerializeField] float massCap = 100f;
        [Tooltip("Maximum slowdown applied at massCap. 0.9 = 90% slow.")]
        [SerializeField] float maxSlowdown = 0.9f;

        CubeFlyInputActions _input;

        // Per-frame input snapshots, sampled in Update, applied in FixedUpdate.
        Vector3 _thrustInput;   // x = strafe (+R/-L), y = vertical (+U/-D), z = forward (+F/-B)
        float _pitchInput;      // -1 / 0 / +1 (Down / none / Up arrow)
        float _yawInput;        // -1 / 0 / +1 (Left / none / Right arrow)
        float _rollInput;       // -1 / 0 / +1 (Q / none / E)

        // Accumulated linear velocity in the construct's local frame.
        Vector3 _velocity;

        // Computed once in Start from the rebuilt construct's total mass.
        // Scales linear acceleration AND rotation rates each FixedUpdate,
        // making heavier ships feel sluggish in both translation and turn.
        float _massMultiplier = 1f;

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
            Debug.unityLogger.Log(TAG, $"FlyScene ready. Construct rebuilt: {total} cube(s) (including alpha).");
            Debug.unityLogger.Log(TAG, $"Construct initial position: {construct.position}");

            float totalMass = ComputeTotalMass();
            _massMultiplier = ComputeMassMultiplier(totalMass);
            Debug.unityLogger.Log(TAG,
                $"Total mass: {totalMass:F1}. Acceleration multiplier: {_massMultiplier:F3} " +
                $"({(1f - _massMultiplier) * 100f:F0}% slow).");
        }

        float ComputeTotalMass()
        {
            float alphaMass = baseMassThreshold;
            if (alphaCubePrefab != null)
            {
                CubeStats stats = alphaCubePrefab.GetComponent<CubeStats>();
                if (stats != null && !Mathf.Approximately(stats.mass, 0f)) alphaMass = stats.mass;
            }
            return alphaMass + GameData.SumPlacedMasses(cubeTypeRegistry);
        }

        // Linear lerp from (mass=baseMassThreshold → multiplier=1.0) to
        // (mass=massCap → multiplier=1-maxSlowdown), clamped outside.
        float ComputeMassMultiplier(float totalMass)
        {
            float span = massCap - baseMassThreshold;
            if (span <= Mathf.Epsilon) return 1f;
            float t = Mathf.Clamp01((totalMass - baseMassThreshold) / span);
            return Mathf.Lerp(1f, 1f - maxSlowdown, t);
        }

        void BuildConstruct()
        {
            if (construct == null) construct = transform;

            if (alphaCubePrefab != null)
            {
                GameObject alpha = Instantiate(alphaCubePrefab, construct);
                alpha.transform.localPosition = Vector3.zero;
                alpha.transform.localRotation = Quaternion.identity;
            }

            if (cubeTypeRegistry == null)
            {
                Debug.unityLogger.LogError(TAG, "No CubeTypeRegistry assigned on FlyController; placed cubes won't be rebuilt.");
                return;
            }

            for (int i = 0; i < GameData.PlacedCubes.Count; i++)
            {
                Placement p = GameData.PlacedCubes[i];
                CubeTypeDefinition def = cubeTypeRegistry.Get(p.TypeIndex);
                if (def == null || def.prefab == null)
                {
                    Debug.unityLogger.LogWarning(TAG, $"Skipping {p.Cell}: no prefab for type index {p.TypeIndex}.");
                    continue;
                }
                GameObject go = Instantiate(def.prefab, construct);
                go.transform.localPosition = new Vector3(p.Cell.x, p.Cell.y, p.Cell.z);
                // Apply the orientation chosen at build-time so each cube
                // keeps its placed pose relative to the construct.
                go.transform.localRotation = p.Rotation;
                // Seed any zero-valued CubeStats fields from the type
                // defaults so the construct in fly mode carries the same
                // baselines that the build scene would have applied.
                def.ApplyDefaultsTo(go.GetComponent<CubeStats>());
            }
        }

        void Update()
        {
            // Sample the input every frame; physics-paced application happens
            // in FixedUpdate to keep the throttle integration stable.
            _thrustInput = _input.Fly.Thrust.ReadValue<Vector3>();
            _pitchInput  = _input.Fly.Pitch.ReadValue<float>();
            _yawInput    = _input.Fly.Yaw.ReadValue<float>();
            _rollInput   = _input.Fly.Roll.ReadValue<float>();
        }

        void FixedUpdate()
        {
            if (construct == null) return;
            float dt = Time.fixedDeltaTime;
            float mult = _massMultiplier;

            // 3-axis throttle: per-axis accumulation, magnitude clamp,
            // frame-rate-independent exponential decay. Mass scales the
            // applied acceleration; maxSpeed and drag are unchanged.
            _velocity += _thrustInput * (accelerationRate * mult) * dt;
            _velocity = Vector3.ClampMagnitude(_velocity, maxSpeed);
            _velocity *= Mathf.Exp(-drag * dt);

            // Translate in the construct's local frame so thrust always
            // aligns with the ship's current orientation.
            Vector3 localTranslation =
                construct.right   * _velocity.x +
                construct.up      * _velocity.y +
                construct.forward * _velocity.z;
            construct.position += localTranslation * dt;

            // Pitch: Up arrow → nose up → negative local-X rotation.
            construct.Rotate(Vector3.right * (-_pitchInput * pitchSensitivity * mult * dt), Space.Self);
            // Yaw: Right arrow → yaw right → positive world-Y rotation
            // (world-space yaw avoids roll coupling when the ship is pitched).
            construct.Rotate(Vector3.up * (_yawInput * yawSensitivity * mult * dt), Space.World);
            // Roll: Q (input = -1, anti-clockwise from pilot POV) → positive
            // local-Z rotation; E reverses.
            construct.Rotate(Vector3.forward * (-_rollInput * rollSpeed * mult * dt), Space.Self);
        }
    }
}
