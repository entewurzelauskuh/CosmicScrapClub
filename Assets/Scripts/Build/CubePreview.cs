using CubeFly.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace CubeFly.Build
{
    public class CubePreview : MonoBehaviour
    {
        // Kept as a fallback for early-init / missing-registry scenarios; the
        // primary preview source is BuildManager.Registry's current cube type.
        [SerializeField] GameObject previewPrefab;
        [SerializeField] Camera buildCamera;
        [SerializeField] float maxDistance = 100f;
        [SerializeField] float previewScale = 0.99f;

        public Vector3Int CandidateCell { get; private set; }
        public bool IsValid { get; private set; }

        BuildManager _buildManager;
        GameObject _previewInstance;
        int _previewSourceTypeIndex = -1;
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
                _buildManager.CurrentTypeChanged     += OnCurrentTypeChanged;
                _buildManager.CurrentRotationChanged += OnCurrentRotationChanged;
                _buildManager.CurrentToolChanged     += OnCurrentToolChanged;
                _currentTool = _buildManager.CurrentTool;
            }

            Debug.unityLogger.Log(TAG, "CubePreview ghost cube spawned.");
        }

        void Start()
        {
            // Build the initial preview now that BuildManager has fully
            // initialised. After this, OnCurrentTypeChanged keeps it in sync.
            if (_buildManager != null)
                EnsurePreviewMatchesType(_buildManager.CurrentTypeIndex);
            else
                EnsurePreviewFromFallback();
            ApplyCurrentRotation();
        }

        void OnDestroy()
        {
            if (_buildManager != null)
            {
                _buildManager.CurrentTypeChanged     -= OnCurrentTypeChanged;
                _buildManager.CurrentRotationChanged -= OnCurrentRotationChanged;
                _buildManager.CurrentToolChanged     -= OnCurrentToolChanged;
            }
        }

        void OnCurrentTypeChanged(int typeIndex)
        {
            EnsurePreviewMatchesType(typeIndex);
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

        void EnsurePreviewMatchesType(int typeIndex)
        {
            if (_previewSourceTypeIndex == typeIndex && _previewInstance != null) return;
            if (_buildManager == null || _buildManager.Registry == null)
            {
                EnsurePreviewFromFallback();
                return;
            }
            CubeTypeDefinition def = _buildManager.Registry.Get(typeIndex);
            if (def == null || def.prefab == null)
            {
                EnsurePreviewFromFallback();
                return;
            }
            RebuildPreview(def.prefab, typeIndex);
        }

        void EnsurePreviewFromFallback()
        {
            if (previewPrefab == null) return;
            if (_previewInstance != null && _previewSourceTypeIndex == -2) return;
            RebuildPreview(previewPrefab, -2);
        }

        void RebuildPreview(GameObject sourcePrefab, int typeIndexTag)
        {
            if (_previewInstance != null) Destroy(_previewInstance);
            _previewInstance = Instantiate(sourcePrefab);
            _previewInstance.name = "PreviewCube";

            // Disable colliders so build raycasts can't hit the preview, and
            // strip any PlacedCubeData copies from the prefab so leftover data
            // never gets confused with a real placed cube.
            foreach (Collider col in _previewInstance.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
            foreach (PlacedCubeData data in _previewInstance.GetComponentsInChildren<PlacedCubeData>(true))
                Destroy(data);
            foreach (Renderer rend in _previewInstance.GetComponentsInChildren<Renderer>(true))
                rend.shadowCastingMode = ShadowCastingMode.Off;

            // Drop the entire hierarchy onto the PreviewCube layer so we
            // additionally protect against raycast hits, and inset the cube
            // very slightly so it doesn't z-fight with adjacent placed cubes.
            if (_previewLayer >= 0) SetLayerRecursive(_previewInstance, _previewLayer);
            _previewInstance.transform.localScale = Vector3.one * previewScale;
            _previewInstance.SetActive(false);

            _previewSourceTypeIndex = typeIndexTag;
        }

        void ApplyCurrentRotation()
        {
            if (_previewInstance == null) return;
            Quaternion rot = _buildManager != null ? _buildManager.CurrentRotation : Quaternion.identity;
            _previewInstance.transform.rotation = rot;
        }

        static void SetLayerRecursive(GameObject root, int layer)
        {
            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursive(root.transform.GetChild(i).gameObject, layer);
        }

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
            if (_previewInstance == null) return;

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
            if (!GameData.IsAdjacentToExisting(candidate))
            { Show(false); return; }

            CandidateCell = candidate;
            IsValid = true;
            _previewInstance.transform.position = new Vector3(candidate.x, candidate.y, candidate.z);
            ApplyCurrentRotation();
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
            if (_previewInstance == null) return;
            if (_previewInstance.activeSelf != visible) _previewInstance.SetActive(visible);

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
