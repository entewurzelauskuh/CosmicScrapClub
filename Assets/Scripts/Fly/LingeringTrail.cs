using UnityEngine;

namespace CubeFly.Fly
{
    // Tiny detach helper for TrailRenderers that are children of
    // short-lived projectiles. Without this, killing the projectile
    // vanishes the trail mid-air with a visible pop.
    //
    // The detach must happen BEFORE Unity destroys the parent's
    // hierarchy (not during the child's OnDestroy) — by then the
    // hierarchy cleanup race makes SetParent(null) unreliable across
    // Unity versions. So the parent (Bullet / Rocket) calls
    // DetachAndFade() explicitly from its own OnDestroy.
    //
    // After DetachAndFade:
    //  • The TrailRenderer's GameObject is unparented.
    //  • emitting = false  — no new vertices appear.
    //  • autodestruct = true — the GameObject removes itself once
    //    the last surviving trail segment expires per TrailRenderer.time.
    [RequireComponent(typeof(TrailRenderer))]
    public class LingeringTrail : MonoBehaviour
    {
        public void DetachAndFade()
        {
            transform.SetParent(null, true);   // worldPositionStays = true
            TrailRenderer trail = GetComponent<TrailRenderer>();
            if (trail == null) return;
            trail.emitting = false;
            trail.autodestruct = true;
        }
    }
}
