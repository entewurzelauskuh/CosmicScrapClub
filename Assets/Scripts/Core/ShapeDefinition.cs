using UnityEngine;

namespace CubeFly.Core
{
    // Shape categories — orthogonal axis to material. Armour shapes
    // (Cube, Slope) pull their material from the MaterialRegistry's
    // A/B/C/D pool, with the per-shape memory dict in BuildManager
    // remembering each shape's last-armed choice. Weapon shapes
    // (Pyramid, future) are 1:1 coupled with their own dedicated
    // MaterialDefinition referenced by `weaponMaterial`; the regular
    // material flyout is suppressed for them.
    public enum ShapeCategory
    {
        Armour,
        Weapon,
    }

    // One placeable shape — geometry + collider only. For armour
    // shapes, stats / colour / gameplay identity come from the chosen
    // MaterialDefinition at spawn time. For weapon shapes, the
    // MaterialDefinition is fixed (`weaponMaterial`) and the toolbar
    // doesn't offer alternatives.
    [CreateAssetMenu(fileName = "Shape", menuName = "CubeFly/Shape")]
    public class ShapeDefinition : ScriptableObject
    {
        [Tooltip("User-facing shape name shown on the build toolbar.")]
        public string displayName = "Shape";

        [Tooltip("Prefab spawned for this shape. Must carry a CubeStats and a 1×1×1 collider so adjacency/face-detection works in the cell-based grid. The MaterialDefinition supplies the renderer material and stat values at spawn.")]
        public GameObject prefab;

        [Tooltip("Armour shapes use the MaterialRegistry's A/B/C/D pool. Weapon shapes use their own coupled `weaponMaterial`.")]
        public ShapeCategory category = ShapeCategory.Armour;

        [Tooltip("Material applied at spawn time when category == Weapon. Ignored for armour shapes (the chosen MaterialDefinition from MaterialRegistry is used instead).")]
        public MaterialDefinition weaponMaterial;

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
        // registry by index; weapon shapes ignore the registry and
        // return their coupled `weaponMaterial`. Returns null when
        // either the registry is missing or the index is out of range
        // (armour) / weaponMaterial is unassigned (weapon).
        public MaterialDefinition ResolveMaterial(int materialIndex, MaterialRegistry materialRegistry)
        {
            if (category == ShapeCategory.Weapon) return weaponMaterial;
            return materialRegistry != null ? materialRegistry.Get(materialIndex) : null;
        }

        public bool IsWeapon => category == ShapeCategory.Weapon;
    }
}
