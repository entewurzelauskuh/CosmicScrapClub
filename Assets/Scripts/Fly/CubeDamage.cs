using CubeFly.Build;
using CubeFly.Core;
using UnityEngine;

namespace CubeFly.Fly
{
    // Shared damage-application pipeline. ANY damage source in the Fly
    // scene (projectiles, crash impacts, future AI weapons, …) routes
    // through here so the four steps below stay consistent across
    // sources:
    //
    //   1. Apply damage via CubeStats.TakeDamage (armour-aware) OR
    //      CubeStats.TakeRawDamage (armour-bypass for kinetic impacts).
    //   2. Log the hit with the source's tag.
    //   3. If HP reached zero AND the cube isn't the alpha (end-of-run
    //      owns that case), remove its GameData entry (only relevant for
    //      player-construct cubes that carry a PlacedCubeData) and
    //      kick off the CubeDeath animation.
    //   4. Return the actual HP lost so callers can chain logic on it.
    //
    // The alpha-skip is duplicated on CubeDeath itself as belt-and-braces:
    // a future damage source that forgets to route through here still
    // won't accidentally animate the alpha away.
    public static class CubeDamage
    {
        public static float ApplyAndLog(CubeStats stats, float incoming,
            Vector3 outwardOrigin, string sourceTag, bool ignoreArmour = false)
        {
            if (stats == null) return 0f;

            float hpBefore = stats.healthPoints;
            float applied = ignoreArmour
                ? stats.TakeRawDamage(incoming)
                : stats.TakeDamage(incoming);

            // Different log format depending on whether armour is in play —
            // logging "AV 10" for a kinetic hit that bypasses AV would be
            // actively misleading.
            if (ignoreArmour)
            {
                Debug.unityLogger.Log(sourceTag,
                    $"Hit '{stats.name}' for {applied:F1} damage " +
                    $"(raw {incoming:F1}, armour bypassed). " +
                    $"HP: {hpBefore:F1} → {stats.healthPoints:F1}.");
            }
            else
            {
                Debug.unityLogger.Log(sourceTag,
                    $"Hit '{stats.name}' for {applied:F1} damage " +
                    $"(raw {incoming:F1}, AV {stats.armourValue:F1}). " +
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
            PlacedCubeData placed = stats.GetComponent<PlacedCubeData>();
            if (placed != null) GameData.Remove(placed.cell);

            CubeDeath death = stats.GetComponent<CubeDeath>()
                           ?? stats.gameObject.AddComponent<CubeDeath>();
            death.BeginDeath(outwardOrigin);

            return applied;
        }
    }
}
