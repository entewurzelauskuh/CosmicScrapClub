using System.Collections.Generic;
using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // Crash damage for the player's construct. Sweeps each construct
    // cube each FixedUpdate from its previous-frame world position to
    // its current one; any non-self collider hit during the sweep
    // produces damage scaled to impact speed.
    //
    // We don't use Unity's collision events because the construct is
    // transform-driven (no Rigidbody) — collision events wouldn't fire
    // reliably. A per-frame swept BoxCast is deterministic at any
    // speed, captures rotation-induced motion at the construct's
    // extremities, and naturally integrates with the existing self-hit
    // filter pattern from ProjectileHit.
    //
    // Damage shape: `clamp(impactSpeed * scale, min, max)`. Below
    // MinSpeedForDamage we no-op entirely so a gentle landing isn't
    // punishing. With scale 0.3 and max 10, a max-speed (37.5 u/s)
    // crash caps at 10 damage; slow taps yield 1.
    //
    // Entry-only model: per-cube `_inContact` boolean. Damage fires
    // ONLY on the first frame of contact; subsequent frames where the
    // sweep keeps hitting (e.g. scraping a wall) don't pile damage
    // every frame. To re-arm, the sweep must miss for one frame.
    // Mirrors `OnCollisionEnter` semantics without using Unity's
    // physics events.
    public class FlyCrashDetector : MonoBehaviour
    {
        [SerializeField] FlyController flyController;

        [Header("Damage tuning")]
        [Tooltip("Damage = clamp(impactSpeed * DamageScale, MinDamage, MaxDamage). 0.3 maps the FlyController default maxSpeed of 37.5 u/s to ~11 (clamped to 10).")]
        [SerializeField] float damageScale = 0.3f;
        [SerializeField] float minDamage = 1f;
        [SerializeField] float maxDamage = 10f;
        [Tooltip("Speeds below this don't produce damage at all — landing gently shouldn't hurt.")]
        [SerializeField] float minSpeedForDamage = 3f;

        // Per-cube state. Allocations stay one-time at session start
        // (apart from when new cubes attach, which happens at most a
        // few times per session as the construct rebuilds in Start).
        readonly Dictionary<Transform, Vector3> _prevPos = new();
        readonly Dictionary<Transform, bool> _inContact = new();

        // Allocation-amortising buffer for BoxCastNonAlloc. 8 is plenty
        // for a single cube's per-frame sweep — same logic as
        // ProjectileHit's hit buffer.
        readonly RaycastHit[] _hitBuffer = new RaycastHit[8];

        int _layerMask;
        Transform _construct;

        const string TAG = "Crash";

        void Start()
        {
            if (flyController == null) flyController = FindAnyObjectByType<FlyController>();
            if (flyController == null)
            {
                Debug.unityLogger.LogWarning(TAG, "No FlyController in scene; crash detection disabled.");
                enabled = false;
                return;
            }
            _construct = flyController.Construct;
            if (_construct == null)
            {
                Debug.unityLogger.LogWarning(TAG, "FlyController has no construct transform; crash detection disabled.");
                enabled = false;
                return;
            }

            // Defensive layer mask — same fallback pattern used by
            // ProjectileHit and BuildManager. Include the construct
            // layers so we still detect collisions with other cubes
            // (world targets, future enemy ships); self-hits are
            // filtered out at the IsSelf check below.
            _layerMask = LayerMask.GetMask("Default", "PlacedCube", "AlphaCube");
            if (_layerMask == 0)
            {
                int ignoreRaycast = 1 << LayerMask.NameToLayer("Ignore Raycast");
                int previewLayer = LayerMask.NameToLayer("PreviewCube");
                int previewBit = previewLayer >= 0 ? (1 << previewLayer) : 0;
                _layerMask = ~(ignoreRaycast | previewBit);
            }
            Debug.unityLogger.Log(TAG, $"Crash detector armed. Layer mask 0x{_layerMask:X}.");
        }

        void FixedUpdate()
        {
            if (_construct == null) return;
            if (PauseMenu.Instance != null && PauseMenu.Instance.IsOpen) return;

            int n = _construct.childCount;
            for (int i = 0; i < n; i++)
            {
                Transform cube = _construct.GetChild(i);
                if (cube == null) continue;
                ProcessCube(cube);
            }
        }

        void ProcessCube(Transform cube)
        {
            Vector3 curr = cube.position;

            // First frame we see this cube — record its position and
            // skip the sweep. Next frame's delta will be meaningful.
            if (!_prevPos.TryGetValue(cube, out Vector3 prev))
            {
                _prevPos[cube] = curr;
                _inContact[cube] = false;
                return;
            }

            Vector3 delta = curr - prev;
            float step = delta.magnitude;
            _prevPos[cube] = curr;

            // Stationary or near-stationary — clear contact state so
            // we re-arm if we later bump into something. Skip the cast.
            if (step < 1e-4f)
            {
                _inContact[cube] = false;
                return;
            }

            BoxCollider box = cube.GetComponent<BoxCollider>();
            if (box == null) return; // shape without a BoxCollider — skip

            // Local box → world center, world half-extents.
            Vector3 worldCenter = cube.TransformPoint(box.center);
            Vector3 worldHalfExtents = Vector3.Scale(box.size, cube.lossyScale) * 0.5f;
            // Start of the sweep is where the cube WAS last frame,
            // offset by the same prev→curr direction worth of box
            // center.
            Vector3 castStart = worldCenter - delta;
            Vector3 dir = delta / step;

            int hits = Physics.BoxCastNonAlloc(castStart, worldHalfExtents, dir, _hitBuffer,
                cube.rotation, step, _layerMask);

            bool hitValid = TryGetNonSelfHit(hits, out RaycastHit hit);
            if (!hitValid)
            {
                // Lost contact this frame — re-arm for the next bump.
                _inContact[cube] = false;
                return;
            }

            // Already in contact (sliding/scraping) — don't pile damage
            // every frame. Only the entry-frame fires damage.
            if (_inContact.TryGetValue(cube, out bool inContact) && inContact) return;
            _inContact[cube] = true;

            float impactSpeed = step / Time.fixedDeltaTime;
            if (impactSpeed < minSpeedForDamage) return; // gentle landing — no damage

            float damage = Mathf.Clamp(impactSpeed * damageScale, minDamage, maxDamage);

            CubeStats stats = cube.GetComponent<CubeStats>();
            if (stats == null) return;

            Debug.unityLogger.Log(TAG,
                $"'{cube.name}' crashed into '{hit.collider.name}' at {impactSpeed:F1} u/s.");

            // Kinetic impact — armour doesn't mitigate. A bullet ricochets
            // off a steel plate; a 35 u/s collision doesn't care how thick
            // the plating is. Death-drift origin is the construct center
            // so the dying cube flies outward as in projectile kills.
            CubeDamage.ApplyAndLog(stats, damage, _construct.position, TAG, ignoreArmour: true);
        }

        // Returns the closest hit that isn't on the construct itself.
        bool TryGetNonSelfHit(int hitCount, out RaycastHit closest)
        {
            closest = default;
            if (hitCount <= 0) return false;

            // Insertion sort by distance — same pattern as ProjectileHit.
            // The buffer is small (8 max), no GC.
            for (int i = 1; i < hitCount; i++)
            {
                RaycastHit current = _hitBuffer[i];
                int j = i - 1;
                while (j >= 0 && _hitBuffer[j].distance > current.distance)
                {
                    _hitBuffer[j + 1] = _hitBuffer[j];
                    j--;
                }
                _hitBuffer[j + 1] = current;
            }

            for (int i = 0; i < hitCount; i++)
            {
                Transform t = _hitBuffer[i].collider.transform;
                if (t == _construct || t.IsChildOf(_construct)) continue;
                closest = _hitBuffer[i];
                return true;
            }
            return false;
        }
    }
}
