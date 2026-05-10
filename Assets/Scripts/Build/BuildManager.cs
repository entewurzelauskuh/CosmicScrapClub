using System;
using System.Collections.Generic;
using CubeFly.Core;
using CubeFly.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CubeFly.Build
{
    // Active build-mode tool selected via the toolbar.
    public enum BuildTool
    {
        Place,
        Delete
    }

    public class BuildManager : MonoBehaviour
    {
        [SerializeField] CubeTypeRegistry cubeTypeRegistry;
        [SerializeField] GameObject alphaCubePrefab;
        [SerializeField] CubePreview preview;
        [SerializeField] Camera buildCamera;
        [SerializeField] Transform cubeRoot;
        [SerializeField] float maxRayDistance = 100f;

        [Header("Mass budget")]
        [SerializeField] float massLimit = 100f;
        [SerializeField] string massDeniedMessage = "Too much mass!";
        [SerializeField] float massDeniedMessageDuration = 5f;

        readonly Dictionary<Vector3Int, GameObject> _spawned = new();

        CubeFlyInputActions _input;
        int _buildLayerMask;
        int _placedLayerMask;
        int _currentTypeIndex;
        Quaternion _currentRotation = Quaternion.identity;
        BuildTool _currentTool = BuildTool.Place;
        BuildToolbarController _toolbar;
        GameObject _alphaCubeInstance;

        // Delete-tool hover state. The hovered cube is tinted red via
        // MaterialPropertyBlock; the block is cleared on un-hover or when
        // the player switches back to the Place tool.
        Renderer _deleteHoverRenderer;
        MaterialPropertyBlock _deletePropertyBlock;
        static readonly int BaseColorId    = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly Color DeleteTint     = new Color(1f, 0.25f, 0.25f, 1f);
        static readonly Color DeleteEmission = new Color(0.5f, 0.0f, 0.0f, 1f);

        const string TAG = "BuildManager";

        public int BuildLayerMask => _buildLayerMask;
        public CubeTypeRegistry Registry => cubeTypeRegistry;
        public int CurrentTypeIndex => _currentTypeIndex;
        public Quaternion CurrentRotation => _currentRotation;
        public BuildTool CurrentTool => _currentTool;
        public float MassLimit => massLimit;

        // Fired when SetCurrentType changes the active selection — the build
        // toolbar listens for this to highlight the matching button.
        public event Action<int> CurrentTypeChanged;

        // Fired whenever the set of placed cubes changes (Place / Remove,
        // including flood-fill cleanup) and once during Start after the
        // initial reinstantiation. The forward indicator listens for this
        // to reparent itself to the frontmost cube.
        public event Action ConstructChanged;

        // Fired whenever R/T rotates the active placement orientation —
        // CubePreview listens for this to apply the new orientation.
        public event Action<Quaternion> CurrentRotationChanged;

        // Fired when the active tool (Place vs Delete) changes — the
        // toolbar uses it to update button highlights, and CubePreview
        // hides itself in any non-Place tool.
        public event Action<BuildTool> CurrentToolChanged;

        public void SetCurrentTool(BuildTool tool)
        {
            if (_currentTool == tool) return;
            _currentTool = tool;
            ClearDeleteHover();
            Debug.unityLogger.Log(TAG, $"Active tool: {tool}.");
            CurrentToolChanged?.Invoke(_currentTool);
        }

        public bool TryGetSpawnedCube(Vector3Int cell, out GameObject go)
            => _spawned.TryGetValue(cell, out go);

        public void SetCurrentType(int typeIndex)
        {
            if (cubeTypeRegistry == null) return;
            if (typeIndex < 0 || typeIndex >= cubeTypeRegistry.Count) return;

            // Selecting a cube type implies the player wants to place — flip
            // back to Place if the Delete tool was active.
            if (_currentTool != BuildTool.Place) SetCurrentTool(BuildTool.Place);

            if (_currentTypeIndex == typeIndex) return;
            _currentTypeIndex = typeIndex;
            CubeTypeDefinition def = cubeTypeRegistry.Get(typeIndex);
            string label = def != null ? def.displayName : $"Cube #{typeIndex}";
            Debug.unityLogger.Log(TAG, $"Active cube type set to '{label}' (index {typeIndex}).");
            CurrentTypeChanged?.Invoke(typeIndex);
        }

        void Awake()
        {
            ResolveLayerMasks();

            _input = new CubeFlyInputActions();

            Debug.unityLogger.Log(TAG, "BuildManager initialised.");
        }

        // Resolve the build/remove layer masks. If the named layers aren't
        // defined in Project Settings → Tags and Layers, LayerMask.GetMask
        // silently returns 0 and Physics.Raycast hits nothing. Detect that and
        // fall back to "everything except Ignore Raycast" so the build/remove
        // raycasts still function.
        void ResolveLayerMasks()
        {
            int placed = LayerMask.NameToLayer("PlacedCube");
            int alpha = LayerMask.NameToLayer("AlphaCube");
            int previewLayer = LayerMask.NameToLayer("PreviewCube");

            _buildLayerMask = LayerMask.GetMask("PlacedCube", "AlphaCube");
            _placedLayerMask = LayerMask.GetMask("PlacedCube");

            Debug.unityLogger.Log(TAG,
                $"Layer indices — PlacedCube={placed}, AlphaCube={alpha}, PreviewCube={previewLayer}. " +
                $"build mask=0x{_buildLayerMask:X}, placed mask=0x{_placedLayerMask:X}");

            if (_buildLayerMask == 0 || _placedLayerMask == 0)
            {
                int ignoreRaycast = 1 << LayerMask.NameToLayer("Ignore Raycast");
                int previewBit = previewLayer >= 0 ? (1 << previewLayer) : 0;
                int fallback = ~(ignoreRaycast | previewBit);

                Debug.unityLogger.LogError(TAG,
                    "Custom layers not found. Add 'PlacedCube', 'AlphaCube', and 'PreviewCube' " +
                    "in Project Settings → Tags and Layers. Falling back to all layers (minus " +
                    "Ignore Raycast and PreviewCube).");

                if (_buildLayerMask == 0) _buildLayerMask = fallback;
                if (_placedLayerMask == 0) _placedLayerMask = fallback;
            }
        }

        void OnEnable() => _input.Build.Enable();
        void OnDisable() => _input.Build.Disable();
        void OnDestroy() => _input?.Dispose();

        void Start()
        {
            if (buildCamera == null) buildCamera = Camera.main;
            if (preview == null)
            {
                preview = GetComponent<CubePreview>();
                if (preview == null) preview = FindAnyObjectByType<CubePreview>();
            }
            if (cubeRoot == null)
            {
                GameObject root = GameObject.Find("CubeRoot");
                if (root == null) root = new GameObject("CubeRoot");
                cubeRoot = root.transform;
            }
            EnsureAlphaCube();
            int restored = GameData.PlacedCubes.Count;
            ReinstantiateExistingCubes();
            Debug.unityLogger.Log(TAG, $"BuildScene ready. Restored {restored} cubes from GameData.");

            _toolbar = FindAnyObjectByType<BuildToolbarController>();

            // Surface the initial state so listeners can render the
            // toolbar's selected state and stat labels without waiting
            // for the first placement.
            CurrentTypeChanged?.Invoke(_currentTypeIndex);
            CurrentToolChanged?.Invoke(_currentTool);
            ConstructChanged?.Invoke();
        }

        // Sums a CubeStats field across the alpha cube + all spawned placed
        // cubes. Instance-based so the labels reflect any runtime mutation
        // (damage, rebalances) the gameplay layer applies later.
        float SumStat(System.Func<CubeStats, float> selector)
        {
            float total = 0f;
            if (_alphaCubeInstance != null)
            {
                CubeStats stats = _alphaCubeInstance.GetComponent<CubeStats>();
                if (stats != null) total += selector(stats);
            }
            foreach (var kv in _spawned)
            {
                if (kv.Value == null) continue;
                CubeStats stats = kv.Value.GetComponent<CubeStats>();
                if (stats != null) total += selector(stats);
            }
            return total;
        }

        public float ComputeCurrentMass()         => SumStat(s => s.mass);
        public float ComputeCurrentHealthPoints() => SumStat(s => s.healthPoints);

        void EnsureAlphaCube()
        {
            if (alphaCubePrefab == null) return;
            GameObject existing = GameObject.FindGameObjectWithTag("AlphaCube");
            if (existing != null) { _alphaCubeInstance = existing; return; }
            _alphaCubeInstance = Instantiate(alphaCubePrefab, Vector3.zero, Quaternion.identity);
        }

        // Poll in Update rather than subscribing to InputAction.performed.
        // EventSystem.IsPointerOverGameObject is only valid during a regular
        // frame update; calling it from an InputAction callback warns about
        // querying stale UI state.
        void Update()
        {
            if (IsPointerOverUI())
            {
                ClearDeleteHover();
                return;
            }

            // The Delete tool tints whatever placed cube the cursor is over.
            // The Place tool defers visualization to CubePreview.
            if (_currentTool == BuildTool.Delete) UpdateDeleteHover();
            else                                  ClearDeleteHover();

            if (_input.Build.Place.WasPerformedThisFrame())
            {
                if (_currentTool == BuildTool.Place)        TryPlace();
                else if (_currentTool == BuildTool.Delete)  DeleteHoveredCube();
            }
            if (_input.Build.RotateZ.WasPerformedThisFrame()) RotateBy(0f, 0f, 90f);
            if (_input.Build.RotateX.WasPerformedThisFrame()) RotateBy(90f, 0f, 0f);
        }

        void RotateBy(float xDeg, float yDeg, float zDeg)
        {
            _currentRotation = Quaternion.Euler(xDeg, yDeg, zDeg) * _currentRotation;
            Debug.unityLogger.Log(TAG, $"Active rotation now: {_currentRotation.eulerAngles}");
            CurrentRotationChanged?.Invoke(_currentRotation);
        }

        static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        void ReinstantiateExistingCubes()
        {
            if (cubeRoot == null) return;
            for (int i = 0; i < GameData.PlacedCubes.Count; i++)
            {
                Placement p = GameData.PlacedCubes[i];
                SpawnPlacedCube(p.Cell, p.TypeIndex, p.Rotation);
            }
        }

        void SpawnPlacedCube(Vector3Int cell, int typeIndex, Quaternion rotation)
        {
            GameObject prefab = ResolvePrefab(typeIndex);
            if (prefab == null) return;
            Vector3 pos = new Vector3(cell.x, cell.y, cell.z);
            GameObject go = Instantiate(prefab, pos, rotation, cubeRoot);
            PlacedCubeData data = go.GetComponent<PlacedCubeData>();
            if (data == null) data = go.AddComponent<PlacedCubeData>();
            data.cell = cell;

            // Seed any zero-valued CubeStats fields from the type's defaults.
            // No-op when the prefab carries explicit values.
            if (cubeTypeRegistry != null)
            {
                CubeTypeDefinition def = cubeTypeRegistry.Get(typeIndex);
                def?.ApplyDefaultsTo(go.GetComponent<CubeStats>());
            }

            _spawned[cell] = go;
        }

        GameObject ResolvePrefab(int typeIndex)
        {
            if (cubeTypeRegistry == null)
            {
                Debug.unityLogger.LogError(TAG, "No CubeTypeRegistry assigned on BuildManager.");
                return null;
            }
            CubeTypeDefinition def = cubeTypeRegistry.Get(typeIndex);
            if (def == null || def.prefab == null)
            {
                Debug.unityLogger.LogError(TAG, $"CubeTypeDefinition or prefab missing for index {typeIndex}.");
                return null;
            }
            return def.prefab;
        }

        void TryPlace()
        {
            if (preview == null || !preview.IsValid)
            {
                Debug.unityLogger.LogWarning(TAG, "Place attempted with no valid preview cell — ignored.");
                return;
            }
            Vector3Int cell = preview.CandidateCell;

            // Mass-budget gate: deny placement if the additional cube would
            // push the construct over the limit, surface a transient message.
            float prospectiveCubeMass = 0f;
            if (cubeTypeRegistry != null)
            {
                CubeTypeDefinition def = cubeTypeRegistry.Get(_currentTypeIndex);
                if (def != null) prospectiveCubeMass = def.EffectiveMass();
            }
            float prospectiveTotal = ComputeCurrentMass() + prospectiveCubeMass;
            if (prospectiveTotal > massLimit)
            {
                Debug.unityLogger.LogWarning(TAG,
                    $"Place denied: total mass would be {prospectiveTotal:F1} (limit {massLimit:F0}).");
                if (_toolbar != null)
                    _toolbar.ShowFloatingMessage(massDeniedMessage, massDeniedMessageDuration);
                return;
            }

            if (!GameData.TryAdd(cell, _currentTypeIndex, _currentRotation)) return;
            SpawnPlacedCube(cell, _currentTypeIndex, _currentRotation);
            Debug.unityLogger.Log(TAG,
                $"Cube placed at {cell} (type {_currentTypeIndex}, rot {_currentRotation.eulerAngles}). " +
                $"Total mass: {ComputeCurrentMass():F1}/{massLimit:F0}.");
            ConstructChanged?.Invoke();
        }

        // Raycasts placed cubes only (alpha is on a different layer and is
        // therefore never targetable for deletion). Tints the hovered cube
        // red and tracks the renderer so we can restore it on un-hover.
        void UpdateDeleteHover()
        {
            if (buildCamera == null) { ClearDeleteHover(); return; }
            Mouse mouse = Mouse.current;
            if (mouse == null) { ClearDeleteHover(); return; }

            Vector2 mousePos = mouse.position.ReadValue();
            Ray ray = buildCamera.ScreenPointToRay(mousePos);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, _placedLayerMask))
            {
                ClearDeleteHover();
                return;
            }

            Renderer hoverRenderer = hit.collider.GetComponentInChildren<Renderer>();
            if (hoverRenderer == _deleteHoverRenderer) return; // unchanged

            ClearDeleteHover();
            ApplyDeleteHover(hoverRenderer);
        }

        void ApplyDeleteHover(Renderer rend)
        {
            if (rend == null) return;
            if (_deletePropertyBlock == null) _deletePropertyBlock = new MaterialPropertyBlock();
            rend.GetPropertyBlock(_deletePropertyBlock);
            _deletePropertyBlock.SetColor(BaseColorId,    DeleteTint);
            _deletePropertyBlock.SetColor(EmissionColorId, DeleteEmission);
            rend.SetPropertyBlock(_deletePropertyBlock);
            _deleteHoverRenderer = rend;
        }

        void ClearDeleteHover()
        {
            if (_deleteHoverRenderer == null) return;
            _deleteHoverRenderer.SetPropertyBlock(null); // restore baseline
            _deleteHoverRenderer = null;
        }

        // Mirror of the old TryRemove flow but driven by the Delete tool +
        // LMB rather than RMB. Deletes the cube currently under the cursor
        // (if any) and runs the flood-fill cleanup.
        void DeleteHoveredCube()
        {
            if (_deleteHoverRenderer == null)
            {
                Debug.unityLogger.LogWarning(TAG, "Delete tool clicked with nothing hovered.");
                return;
            }

            PlacedCubeData data = _deleteHoverRenderer.GetComponentInParent<PlacedCubeData>();
            if (data == null) data = _deleteHoverRenderer.GetComponent<PlacedCubeData>();
            if (data == null)
            {
                Debug.unityLogger.LogWarning(TAG, "Delete tool clicked but the hovered object isn't a PlacedCube.");
                return;
            }

            // Clear the hover BEFORE destroying — otherwise we hold onto a
            // dead Renderer and SetPropertyBlock would no-op-or-error later.
            Vector3Int cell = data.cell;
            ClearDeleteHover();

            RemoveCell(cell);
            Debug.unityLogger.Log(TAG, $"Cube removed at {cell}.");
            RemoveDanglingCubes();
            ConstructChanged?.Invoke();
        }

        void RemoveCell(Vector3Int cell)
        {
            GameData.Remove(cell);
            if (_spawned.TryGetValue(cell, out GameObject go))
            {
                if (go != null) Destroy(go);
                _spawned.Remove(cell);
            }
        }

        // BFS from origin across occupied cells; anything not visited is dangling.
        void RemoveDanglingCubes()
        {
            Debug.unityLogger.Log(TAG, "Running flood-fill to detect dangling cubes.");

            HashSet<Vector3Int> visited = new HashSet<Vector3Int> { Vector3Int.zero };
            Queue<Vector3Int> queue = new Queue<Vector3Int>();
            queue.Enqueue(Vector3Int.zero);

            while (queue.Count > 0)
            {
                Vector3Int curr = queue.Dequeue();
                for (int i = 0; i < GameData.Neighbors.Length; i++)
                {
                    Vector3Int nb = curr + GameData.Neighbors[i];
                    if (visited.Contains(nb)) continue;
                    if (!GameData.IsOccupied(nb)) continue;
                    visited.Add(nb);
                    queue.Enqueue(nb);
                }
            }

            // Snapshot cells before mutation.
            List<Vector3Int> snapshot = new List<Vector3Int>(GameData.PlacedCubes.Count);
            for (int i = 0; i < GameData.PlacedCubes.Count; i++)
                snapshot.Add(GameData.PlacedCubes[i].Cell);

            int removed = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                if (!visited.Contains(snapshot[i]))
                {
                    RemoveCell(snapshot[i]);
                    removed++;
                }
            }

            Debug.unityLogger.Log(TAG, $"Flood-fill removed {removed} dangling cube(s).");
        }
    }
}
