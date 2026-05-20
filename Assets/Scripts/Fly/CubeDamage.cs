using CubeFly.Build;
using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // Shared damage-application pipeline. ANY damage source in the Fly
    // scene (projectiles, crash impacts, future AI weapons, …)
    // constructs a HitContext and routes through here so the four steps
    // below stay consistent across sources:
    //
    //   1. Apply damage via CubeStats.TakeDamage (armour-aware) OR
    //      CubeStats.TakeRawDamage (HitFlags.BypassArmour, for kinetic
    //      impacts and future armour-piercing weapons).
    //   2. Log the hit with context.SourceTag, including the
    //      DamageType for diagnostics.
    //   3. If HP reached zero AND the cube isn't the alpha (end-of-run
    //      owns that case), remove its GameData entry (only relevant for
    //      player-construct cubes that carry a PlacedCubeData) and
    //      kick off the CubeDeath animation toward context.OutwardOrigin.
    //   4. Return the actual HP lost so callers can chain logic on it.
    //
    // The alpha-skip is duplicated on CubeDeath itself as belt-and-braces:
    // a future damage source that forgets to route through here still
    // won't accidentally animate the alpha away.
    public static class CubeDamage
    {
        public static float ApplyAndLog(in HitContext context)
        {
            CubeStats stats = context.Target;
            if (stats == null) return 0f;

            float hpBefore = stats.healthPoints;
            bool bypassArmour = (context.Flags & HitFlags.BypassArmour) != 0;
            float applied = bypassArmour
                ? stats.TakeRawDamage(context.Amount)
                : stats.TakeDamage(context.Amount);

            // Different log format depending on whether armour is in play —
            // logging "AV 10" for a kinetic hit that bypasses AV would be
            // actively misleading.
            if (bypassArmour)
            {
                Debug.unityLogger.Log(context.SourceTag,
                    $"Hit '{stats.name}' for {applied:F1} damage " +
                    $"(raw {context.Amount:F1}, type {context.Type}, armour bypassed). " +
                    $"HP: {hpBefore:F1} → {stats.healthPoints:F1}.");
            }
            else
            {
                Debug.unityLogger.Log(context.SourceTag,
                    $"Hit '{stats.name}' for {applied:F1} damage " +
                    $"(raw {context.Amount:F1}, type {context.Type}, AV {stats.armourValue:F1}). " +
                    $"HP: {hpBefore:F1} → {stats.healthPoints:F1}.");
            }

            if (stats.healthPoints > 0f) return applied;

            // Fatal hit on the alpha cube → end-of-run. The alpha
            // doesn't run CubeDeath's drift animation (it's the
            // construct's anchor; spinning it off would look wrong);
            // instead, the GameOverMenu overlay shows and the player
            // is sent back to the main menu. TriggerGameOver is
            // idempotent — repeat calls while the overlay is already
            // up no-op.
            if (stats.CompareTag("AlphaCube"))
            {
                GameOverMenu.Instance?.TriggerGameOver();
                return applied;
            }

            // Player-construct cubes carry a PlacedCubeData whose cell
            // identifies their slot in GameData. Removing here keeps
            // the mass budget and Hangar re-entry consistent. World
            // targets have no PlacedCubeData and skip this branch.
            // GameData.Remove returns true only when a real construct
            // cube actually left the placement list — world props and
            // turret pyramids (a PlacedCubeData whose cell isn't in
            // GameData) return false.
            PlacedCubeData placed = stats.GetComponent<PlacedCubeData>();
            bool removedFromConstruct = placed != null && GameData.Remove(placed.cell);

            CubeDeath death = stats.GetComponent<CubeDeath>()
                           ?? stats.gameObject.AddComponent<CubeDeath>();
            death.BeginDeath(context.OutwardOrigin);

            // Notify the flight controller to recompute construct mass —
            // only for genuine construct cubes, so destroying world
            // targets / turrets doesn't spam ResolveRigidbody.
            if (removedFromConstruct) CubeDeath.RaiseCubeDied();

            return applied;
        }
    }
}
