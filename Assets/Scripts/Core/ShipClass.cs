namespace CubeFly.Core
{
    // The three construct classes a player picks from. Chosen via a
    // dropdown in BuildScene, stored per save slot, and applied at
    // spawn time in both BuildScene and FlyScene.
    //
    // Stored in saves by NAME (see ConstructSave) rather than by the
    // underlying int — reordering this enum then can't silently
    // re-map existing saves to the wrong class.
    public enum ShipClass
    {
        Allrounder,
        Tank,
        Scout,
    }

    // Per-class stat block. Three levers (per the ROADMAP):
    //   • AlphaHealthPoints — HP of the construct's anchor cube.
    //   • MassCap           — the build budget (BuildManager.massLimit).
    //   • MovementMultiplier — scales FlyController thrust + torque.
    public readonly struct ShipClassStats
    {
        public readonly float AlphaHealthPoints;
        public readonly float MassCap;
        public readonly float MovementMultiplier;

        public ShipClassStats(float alphaHealthPoints, float massCap, float movementMultiplier)
        {
            AlphaHealthPoints = alphaHealthPoints;
            MassCap = massCap;
            MovementMultiplier = movementMultiplier;
        }
    }

    // Static lookup for ShipClass stats + name parsing. Kept as a
    // plain table rather than ScriptableObjects: there are exactly
    // three classes and the numbers are gameplay constants, not
    // designer-facing content. If the roster grows past a handful or
    // wants per-class prefabs, migrating to an SO registry (the
    // pattern ShapeRegistry / MaterialRegistry use) would be the move.
    public static class ShipClasses
    {
        // Tuning values — see the ROADMAP Flight & Movement section.
        // Allrounder mirrors the project's historical defaults.
        public static ShipClassStats StatsFor(ShipClass shipClass)
        {
            switch (shipClass)
            {
                case ShipClass.Tank:
                    return new ShipClassStats(alphaHealthPoints: 200f, massCap: 180f, movementMultiplier: 0.7f);
                case ShipClass.Scout:
                    return new ShipClassStats(alphaHealthPoints: 60f, massCap: 60f, movementMultiplier: 1.4f);
                case ShipClass.Allrounder:
                default:
                    return new ShipClassStats(alphaHealthPoints: 100f, massCap: 100f, movementMultiplier: 1.0f);
            }
        }

        public static string DisplayName(ShipClass shipClass) => shipClass.ToString();

        // Parse a class name back to the enum. Unknown / empty input
        // (e.g. a save written before ship classes existed) falls back
        // to Allrounder. Ordinal case-sensitive, matching the
        // shape / material name-lookup convention in the registries.
        public static ShipClass Parse(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                foreach (ShipClass c in System.Enum.GetValues(typeof(ShipClass)))
                {
                    if (c.ToString() == name) return c;
                }
            }
            return ShipClass.Allrounder;
        }
    }
}
