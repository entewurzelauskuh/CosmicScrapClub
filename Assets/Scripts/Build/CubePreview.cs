using CubeFly.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace CubeFly.Build
{
    // Build-mode placement preview. The grid system is cell-based: every
    // placeable mesh is required to fit inside one 1×1×1 grid cell, even
    // when the mesh itself isn't a cube (e.g. a triangular-prism ramp).
    //
    // The preview reflects that contract by rendering a *composite* ghost:
    //
    //   1. An outer translucent CUBE — the cell-bounds visualisation. It
    //      shows the player which grid cell will be occupied. Always a
    //      unit cube regardless of what type is selected.
    //
    //   2. An inner mesh — an instance of the currently selected cube
    //      type's prefab, scaled down so it fits visibly inside the
    //      bounds ghost. This shows the player WHAT they'll be placing.
    //
    // The inner mesh inherits the active rotation (R/T) so the player can
    // see the prism ramp's orientation. The bounds cube stays world-axis
    // aligned — rotating a cube within its own cell doesn't change which
    // cell is occupied.
    public class CubePreview : MonoBehaviour
    {
        // The bounds-ghost prefab (transparent unit cube). Must be a
        // 1×1×1 cube on the PreviewCube layer with a transparent
        // material. The shipped PreviewCube prefab fits this role.
        [SerializeField] GameObject previewPrefab;
        [SerializeField] Camera buildCamera;
        [SerializeField] float maxDistance = 100f;

        [Tooltip("Scale of the outer bounds ghost. Slightly under 1.0 avoids z-fighting with adjacent placed cubes.")]
        [SerializeField] float boundsGhostScale = 0.99f;

        [Tooltip("Scale of the inner mesh inside the bounds ghost. < 1.0 leaves a clear visible gap so the player sees both the cell-bounds cube and the actual mesh that will be placed.")]
        [SerializeField] float innerMeshScale = 0.7f;

        public Vector3Int CandidateCell { get; private set; }
        public bool IsValid { get; private set; }

        BuildManager _buildManager;

        // Composite preview root — child of nothing, repositioned each
        // frame to the candidate cell. Parents both the bounds ghost and
        // the inner mesh so they move/show together.
        GameObject _previewRoot;
        GameObject _boundsGhost;
        GameObject _innerMesh;
        int _innerSourceShapeIndex = -1;
        int _innerSourceMaterialIndex = -1;

        // Tint state for the bounds ghost. Valid placements clear the
        // property block (so the material's default green shows
        // through); invalid placements paint it red. Tracked so we
        // don't push the same colour every frame.
        Renderer _boundsRenderer;
        MaterialPropertyBlock _validityPropertyBlock;
        bool? _currentValidityTint;
        static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        // Alpha matches PreviewCubeMat's transparent baseline so the
        // inner mesh stays visible inside the red ghost.
        static readonly Color InvalidTint     = new Color(1f, 0.30f, 0.30f, 0.40f);
        static readonly Color InvalidEmission = new Color(0.40f, 0f, 0f, 1f);

        int _layerMask;
        int _previewLayer;
        bool _wasVisible;
        BuildTool _currentTool = BuildTool.Place;

        const string TAG = "CubePreview";

        void Awake()
        {
            _layerMask = LayerMask.GetMask("PlacedCube", "AlphaCube");
            if (_layerMask == 0)
            {
                int ignoreRaycast = 1 << LayerMask.NameToLayer("Ignore Raycast");
                int previewLayer = LayerMask.NameToLayer("PreviewCube");
                int previewBit = previewLayer >= 0 ? (1 << previewLayer) : 0;
                _layerMask = ~(ignoreRaycast | previewBit);
            }
            _previewLayer = LayerMask.NameToLayer("PreviewCube");
            if (buildCamera == null) buildCamera = Camera.main;

            _buildManager = GetComponent<BuildManager>();
            if (_buildManager == null) _buildManager = FindAnyObjectByType<BuildManager>();
            if (_buildManager != null)
            {
                _buildManager.CurrentShapeChanged    += OnCurrentShapeChanged;
                _buildManager.CurrentMaterialChanged += OnCurrentMaterialChanged;
                _buildManager.CurrentRotationChanged += OnCurrentRotationChanged;
                _buildManager.CurrentToolChanged     += OnCurrentToolChanged;
                _currentTool = _buildManager.CurrentTool;
            }

            EnsurePreviewRoot();
            EnsureBoundsGhost();
            Debug.unityLogger.Log(TAG, "CubePreview composite (bounds ghost + inner mesh) ready.");
        }

        void Start()
        {
            // Build the initial inner now that BuildManager has fully
            // initialised. After this, OnCurrentShapeChanged /
            // OnCurrentMaterialChanged keep it in sync.
            if (_buildManager != null)
                EnsureInnerMatchesActive();
            ApplyCurrentRotation();
        }

        void OnDestroy()
        {
            if (_buildManager != null)
            {
                _buildManager.CurrentShapeChanged    -= OnCurrentShapeChanged;
                _buildManager.CurrentMaterialChanged -= OnCurrentMaterialChanged;
                _buildManager.CurrentRotationChanged -= OnCurrentRotationChanged;
                _buildManager.CurrentToolChanged     -= OnCurrentToolChanged;
            }
        }

        void OnCurrentShapeChanged(int shapeIndex)
        {
            EnsureInnerMatchesActive();
            ApplyCurrentRotation();
        }

        void OnCurrentMaterialChanged(int shapeIndex, int materialIndex)
        {
            // Only rebuild if the change touches the active shape — we
            // don't want a Slope-material swap to redraw the inner when
            // Cube is the armed shape.
            if (_buildManager == null) return;
            if (shapeIndex != _buildManager.CurrentShapeIndex) return;
            EnsureInnerMatchesActive();
            ApplyCurrentRotation();
        }

        void OnCurrentRotationChanged(Quaternion _) => ApplyCurrentRotation();

        void OnCurrentToolChanged(BuildTool tool)
        {
            _currentTool = tool;
            // Any non-Place tool owns its own visualisation (Delete tints
            // the hovered cube directly), so the placement ghost just hides.
            if (tool != BuildTool.Place) Show(false);
        }

        // ---------- Composite construction ----------

        void EnsurePreviewRoot()
        {
            if (_previewRoot != null) return;
            _previewRoot = new GameObject("PreviewRoot");
            if (_previewLayer >= 0) _previewRoot.layer = _previewLayer;
            _previewRoot.SetActive(false);
        }

        void EnsureBoundsGhost()
        {
            if (_boundsGhost != null || previewPrefab == null) return;
            _boundsGhost = Instantiate(previewPrefab, _previewRoot.transform);
            _boundsGhost.name = "BoundsGhost";
            _boundsGhost.transform.localPosition = Vector3.zero;
            _boundsGhost.transform.localRotation = Quaternion.identity;
            _boundsGhost.transform.localScale = Vector3.one * boundsGhostScale;
            StripGameplayComponents(_boundsGhost);
            if (_previewLayer >= 0) SetLayerRecursive(_boundsGhost, _previewLayer);
            // Cache the renderer so ApplyValidityTint can paint it red
            // for invalid placements without a per-frame component lookup.
            _boundsRenderer = _boundsGhost.GetComponentInChildren<Renderer>(true);
        }

        // Tints the bounds ghost red for invalid placements; clears the
        // property block (restoring the material's default green) for
        // valid ones. No-ops when the validity hasn't changed since the
        // last call to avoid redundant property-block writes.
        void ApplyValidityTint(bool valid)
        {
            if (_boundsRenderer == null) return;
            if (_currentValidityTint == valid) return;
            _currentValidityTint = valid;

            if (valid)
            {
                _boundsRenderer.SetPropertyBlock(null);
                return;
            }

            if (_validityPropertyBlock == null) _validityPropertyBlock = new MaterialPropertyBlock();
            _boundsRenderer.GetPropertyBlock(_validityPropertyBlock);
            _validityPropertyBlock.SetColor(BaseColorId,    InvalidTint);
            _validityPropertyBlock.SetColor(EmissionColorId, InvalidEmission);
            _boundsRenderer.SetPropertyBlock(_validityPropertyBlock);
        }

        void EnsureInnerMatchesActive()
        {
            if (_buildManager == null) return;
            int shapeIndex = _buildManager.CurrentShapeIndex;
            int materialIndex = _buildManager.CurrentMaterialIndex;

            // No-op if both axes match what we already built.
            if (_innerMesh != null
                && _innerSourceShapeIndex == shapeIndex
                && _innerSourceMaterialIndex == materialIndex)
                return;

            ShapeRegistry shapes = _buildManager.Shapes;
            MaterialRegistry materials = _buildManager.Materials;
            if (shapes == null) return;

            ShapeDefinition shape = shapes.Get(shapeIndex);
            if (shape == null || shape.prefab == null) return;

            if (_innerMesh != null) Destroy(_innerMesh);
            _innerMesh = Instantiate(shape.prefab, _previewRoot.transform);
            _innerMesh.name = "InnerMesh";
            _innerMesh.transform.localPosition = Vector3.zero;
            _innerMesh.transform.localRotation = Quaternion.identity;
            _innerMesh.transform.localScale = Vector3.one * innerMeshScale;

            // Apply the active material to mirror the spawn pipeline —
            // the player sees exactly what they'll place.
            if (materials != null)
            {
                MaterialDefinition mdef = materials.Get(materialIndex);
                mdef?.ApplyTo(_innerMesh);
            }

            StripGameplayComponents(_innerMesh);
            if (_previewLayer >= 0) SetLayerRecursive(_innerMesh, _previewLayer);

            _innerSourceShapeIndex = shapeIndex;
            _innerSourceMaterialIndex = materialIndex;
        }

        // Disable colliders so build raycasts can't hit the preview, drop
        // PlacedCubeData (any leftover would be confused with a real placed
        // cube), and turn off shadow casting so the preview doesn't leave
        // ghostly shadow artifacts on the construct.
        static void StripGameplayComponents(GameObject root)
        {
            foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
            foreach (PlacedCubeData data in root.GetComponentsInChildren<PlacedCubeData>(true))
                Destroy(data);
            foreach (Renderer rend in root.GetComponentsInChildren<Renderer>(true))
                rend.shadowCastingMode = ShadowCastingMode.Off;
        }

        void ApplyCurrentRotation()
        {
            // Rotation is applied to the inner mesh only — the bounds
            // cube is rotation-invariant (same cell occupied either way),
            // and rotating the cube would just produce flicker without
            // any visible change. The prism, by contrast, clearly shows
            // its facing.
            if (_innerMesh == null) return;
            Quaternion rot = _buildManager != null ? _buildManager.CurrentRotation : Quaternion.identity;
            _innerMesh.transform.localRotation = rot;
        }

        static void SetLayerRecursive(GameObject root, int layer)
        {
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursive(root.transform.GetChild(i).gameObject, layer);
        }

        // ---------- Per-frame placement query ----------

        void Update()
        {
            IsValid = false;

            // The placement ghost is meaningless outside the Place tool.
            if (_currentTool != BuildTool.Place) { Show(false); return; }

            if (buildCamera == null)
            {
                buildCamera = Camera.main;
                if (buildCamera == null) { Show(false); return; }
            }
            if (_previewRoot == null) return;

            Mouse mouse = Mouse.current;
            if (mouse == null) { Show(false); return; }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            { Show(false); return; }

            Vector2 mousePos = mouse.position.ReadValue();
            Ray ray = buildCamera.ScreenPointToRay(mousePos);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, _layerMask))
            { Show(false); return; }

            Vector3Int candidate = FaceToCandidateCell(hit);

            if (candidate == Vector3Int.zero || GameData.IsOccupied(candidate))
            { Show(false); return; }
            if (_buildManager == null) { Show(false); return; }

            // Symmetric face-validity check — both the new piece's face
            // toward an existing piece AND that piece's face toward us
            // must be backed by real surface area. Unlike the other
            // early-outs above (which hide the preview entirely), an
            // invalid attachment still has a meaningful candidate cell;
            // we show the bounds ghost in red so the player gets
            // immediate feedback about where they're aiming, while
            // IsValid stays false to block the actual placement.
            bool validAttachment = GameData.IsValidAttachment(
                candidate,
                _buildManager.CurrentShapeIndex,
                _buildManager.CurrentRotation,
                _buildManager.Shapes);

            CandidateCell = candidate;
            IsValid = validAttachment;
            _previewRoot.transform.position = new Vector3(candidate.x, candidate.y, candidate.z);
            ApplyCurrentRotation();
            ApplyValidityTint(validAttachment);
            Show(true);
        }

        // Nudge the hit point along the face normal before rounding. This skirts
        // floating-point ambiguity right at cube edges that would otherwise round
        // to the wrong adjacent cell.
        static Vector3Int FaceToCandidateCell(RaycastHit hit)
        {
            Vector3 nudged = hit.point + hit.normal * 0.01f;
            return Vector3Int.RoundToInt(nudged);
        }

        void Show(bool visible)
        {
            if (_previewRoot == null) return;
            if (_previewRoot.activeSelf != visible) _previewRoot.SetActive(visible);

            // Log on state changes only — Update runs every frame and per-frame
            // log lines would flood the file.
            if (visible && !_wasVisible)
                Debug.unityLogger.Log(TAG, $"Preview snapped to candidate cell {CandidateCell}.");
            else if (!visible && _wasVisible)
                Debug.unityLogger.Log(TAG, "Preview hidden (no valid target).");
            _wasVisible = visible;
        }
    }
}
