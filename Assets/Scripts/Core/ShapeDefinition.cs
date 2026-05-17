using UnityEngine;

namespace CubeFly.Core
{
    // Shape categories — orthogonal axis to material. Armour shapes
    // (Cube, Slope) pull their material from the MaterialRegistry's
    // A/B/C/D pool, with the per-shape memory dict in BuildManager
    // remembering each shape's last-armed choice. Weapon shapes
    // (Pyramid, Cylinder) and Utility shapes (Thruster) are 1:1
    // coupled with their own dedicated MaterialDefinition referenced
    // by `coupledMaterial`; the regular material flyout is suppressed
    // for them. Utility is the non-armour, non-weapon category — the
    // Thruster is its first member.
    public enum ShapeCategory
    {
        Armour,
        Weapon,
        Utility,
    }

    // One placeable shape — geometry + collider only. For armour
    // shapes, stats / colour / gameplay identity come from the chosen
    // MaterialDefinition at spawn time. For weapon and utility shapes,
    // the MaterialDefinition is fixed (`coupledMaterial`) and the
    // toolbar doesn't offer alternatives.
    [CreateAssetMenu(fileName = "Shape", menuName = "CubeFly/Shape")]
    public class ShapeDefinition : ScriptableObject
    {
        [Tooltip("User-facing shape name shown on the build toolbar.")]
        public string displayName = "Shape";

        [Tooltip("Prefab spawned for this shape. Must carry a CubeStats and a 1×1×1 collider so adjacency/face-detection works in the cell-based grid. The MaterialDefinition supplies the renderer material and stat values at spawn.")]
        public GameObject prefab;

        [Tooltip("Armour shapes use the MaterialRegistry's A/B/C/D pool. Weapon and Utility shapes use their own coupled `coupledMaterial`.")]
        public ShapeCategory category = ShapeCategory.Armour;

        [Tooltip("Material applied at spawn time for non-armour shapes (Weapon, Utility). Ignored for armour shapes (the chosen MaterialDefinition from MaterialRegistry is used instead).")]
        [UnityEngine.Serialization.FormerlySerializedAs("weaponMaterial")]
        public MaterialDefinition coupledMaterial;

        // ---------- Valid attachment faces ----------
        //
        // For each of the six cube-cell face directions (±X, ±Y, ±Z) in
        // local space, declare whether THIS shape has a real surface
        // there. A cube has all six valid. A slope (default rotation)
        // has +Y and +Z invalid because those faces are cut away. An
        // antenna might have a single valid face (its mounting base).
        //
        // The adjacency check is symmetric: a placement is valid only
        // if BOTH the new piece's face toward the neighbour AND the
        // neighbour's face toward the new cell are valid. Rotation is
        // handled by inverse-rotating world directions back to local
        // space before the lookup, so the bools always describe the
        // shape in its identity rotation.

        [Header("Valid attachment faces (local space, identity rotation)")]
        [Tooltip("True if this shape has a real surface on its local -X face.")]
        public bool faceNegX = true;
        [Tooltip("True if this shape has a real surface on its local +X face.")]
        public bool facePosX = true;
        [Tooltip("True if this shape has a real surface on its local -Y face.")]
        public bool faceNegY = true;
        [Tooltip("True if this shape has a real surface on its local +Y face.")]
        public bool facePosY = true;
        [Tooltip("True if this shape has a real surface on its local -Z face.")]
        public bool faceNegZ = true;
        [Tooltip("True if this shape has a real surface on its local +Z face.")]
        public bool facePosZ = true;

        // Whether the given LOCAL face direction (a unit vector along
        // ±X / ±Y / ±Z) is backed by a real surface. Anything that
        // doesn't round to one of the six cardinal directions returns
        // false.
        public bool IsLocalFaceValid(Vector3Int localDir)
        {
            if (localDir.x == -1 && localDir.y ==  0 && localDir.z ==  0) return faceNegX;
            if (localDir.x ==  1 && localDir.y ==  0 && localDir.z ==  0) return facePosX;
            if (localDir.x ==  0 && localDir.y == -1 && localDir.z ==  0) return faceNegY;
            if (localDir.x ==  0 && localDir.y ==  1 && localDir.z ==  0) return facePosY;
            if (localDir.x ==  0 && localDir.y ==  0 && localDir.z == -1) return faceNegZ;
            if (localDir.x ==  0 && localDir.y ==  0 && localDir.z ==  1) return facePosZ;
            return false;
        }

        // Convenience: take a WORLD-space cell-face direction and the
        // placement's rotation, inverse-rotate into local space, and
        // look up validity. Rotations are 90°-stepped so the inverse
        // maps cleanly back to a cardinal direction; rounding absorbs
        // any floating-point drift.
        public bool IsWorldFaceValid(Vector3Int worldDir, Quaternion rotation)
        {
            Vector3 localF = Quaternion.Inverse(rotation) * (Vector3)worldDir;
            Vector3Int localDir = Vector3Int.RoundToInt(localF);
            return IsLocalFaceValid(localDir);
        }

        // Resolves the MaterialDefinition that should be applied to a
        // placement of this shape. Armour shapes consult the supplied
        // registry by index; non-armour shapes (Weapon, Utility)
        // ignore the registry and return their coupled
        // `coupledMaterial`. Returns null when either the registry is
        // missing or the index is out of range (armour) /
        // coupledMaterial is unassigned (non-armour).
        public MaterialDefinition ResolveMaterial(int materialIndex, MaterialRegistry materialRegistry)
        {
            if (category != ShapeCategory.Armour) return coupledMaterial;
            return materialRegistry != null ? materialRegistry.Get(materialIndex) : null;
        }

        // Category == Weapon. Kept for the toolbar's weapon-specific
        // readout ("(Weapon)" label) and any weapon-only gameplay.
        public bool IsWeapon => category == ShapeCategory.Weapon;

        // Category == Armour — pulls its material from MaterialRegistry.
        public bool IsArmour => category == ShapeCategory.Armour;

        // Category != Armour (Weapon or Utility) — uses the coupled
        // `coupledMaterial` instead of the MaterialRegistry pool. The
        // toolbar (non-armour category flyouts) and the save layer
        // group on this rather than on IsWeapon.
        public bool UsesCoupledMaterial => category != ShapeCategory.Armour;
    }
}
