using System;
using System.Collections;
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
        [Header("Registries (decoupled shape × material)")]
        [SerializeField] ShapeRegistry shapeRegistry;
        [SerializeField] MaterialRegistry materialRegistry;

        [SerializeField] GameObject alphaCubePrefab;
        [SerializeField] CubePreview preview;
        [SerializeField] Camera buildCamera;
        [SerializeField] Transform cubeRoot;
        [SerializeField] float maxRayDistance = 100f;

        [Header("Mass budget")]
        [SerializeField] float massLimit = 100f;
        [SerializeField] string massDeniedMessage = "Too much mass!";
        [SerializeField] float massDeniedMessageDuration = 5f;

        [Header("Autosave")]
        [Tooltip("Debounce window before a ConstructChanged event triggers a save. Bursts of changes (e.g. flood-fill cleanup) collapse into one write.")]
        [SerializeField] float autosaveDebounceSeconds = 0.25f;
        [Tooltip("Slot label written into the save metadata. Display only — slot identity is the file name.")]
        [SerializeField] string autosaveSlotName = string.Empty;

        readonly Dictionary<Vector3Int, GameObject> _spawned = new();

        // Autosave state — single coroutine restarted on each
        // ConstructChanged so a burst of edits collapses into one
        // write at the end of the debounce window.
        Coroutine _autosaveRoutine;

        // Per-shape last-armed material. Key = shape index, value =
        // material index. Switching shape via the toolbar re-arms its
        // last material (default 0 if never set), so the player's choice
        // for one shape persists when they swap to another and back.
        readonly Dictionary<int, int> _shapeMaterialMemory = new();

        CubeFlyInputActions _input;
        int _buildLayerMask;
        int _placedLayerMask;
        int _currentShapeIndex;
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
        public ShapeRegistry Shapes => shapeRegistry;
        public MaterialRegistry Materials => materialRegistry;
        public int CurrentShapeIndex => _currentShapeIndex;
        public int CurrentMaterialIndex => GetMaterialForShape(_currentShapeIndex);
        public Quaternion CurrentRotation => _currentRotation;
        public BuildTool CurrentTool => _currentTool;
        public float MassLimit => massLimit;

        // Looks up the material that is currently armed for a given
        // shape — first checks the per-shape memory dict, falls back
        // to material 0 if the shape has never been selected.
        public int GetMaterialForShape(int shapeIndex)
            => _shapeMaterialMemory.TryGetValue(shapeIndex, out int m) ? m : 0;

        // Fired when SetCurrentShape changes the active shape — the
        // build toolbar listens to highlight the matching button.
        public event Action<int> CurrentShapeChanged;

        // Fired whenever a shape's material memory is updated, even
        // when the shape isn't the active one. The toolbar uses this
        // to refresh the corner swatch on each shape button.
        public event Action<int, int> CurrentMaterialChanged;

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

        public void SetCurrentShape(int shapeIndex)
        {
            if (shapeRegistry == null) return;
            if (shapeIndex < 0 || shapeIndex >= shapeRegistry.Count) return;

            // Selecting a shape implies the player wants to place — flip
            // back to Place if the Delete tool was active.
            if (_currentTool != BuildTool.Place) SetCurrentTool(BuildTool.Place);

            if (_currentShapeIndex == shapeIndex) return;
            _currentShapeIndex = shapeIndex;
            ShapeDefinition def = shapeRegistry.Get(shapeIndex);
            string label = def != null ? def.displayName : $"Shape #{shapeIndex}";
            Debug.unityLogger.Log(TAG,
                $"Active shape set to '{label}' (index {shapeIndex}, material {CurrentMaterialIndex}).");
            CurrentShapeChanged?.Invoke(shapeIndex);
        }

        // Set the material armed for the *active* shape. Each shape
        // remembers its own last-armed material independently.
        public void SetCurrentMaterial(int materialIndex)
            => SetMaterialForShape(_currentShapeIndex, materialIndex);

        // Set the material armed for a specific shape (allows the
        // toolbar's flyout to set materials for non-active shapes too,
        // e.g. via right-click on a shape button).
        public void SetMaterialForShape(int shapeIndex, int materialIndex)
        {
            if (materialRegistry == null) return;
            if (materialIndex < 0 || materialIndex >= materialRegistry.Count) return;
            if (shapeRegistry == null) return;
            if (shapeIndex < 0 || shapeIndex >= shapeRegistry.Count) return;

            int previous = GetMaterialForShape(shapeIndex);
            _shapeMaterialMemory[shapeIndex] = materialIndex;
            if (previous == materialIndex) return;

            MaterialDefinition def = materialRegistry.Get(materialIndex);
            string mlabel = def != null ? def.displayName : $"Material #{materialIndex}";
            ShapeDefinition sdef = shapeRegistry.Get(shapeIndex);
            string slabel = sdef != null ? sdef.displayName : $"Shape #{shapeIndex}";
            Debug.unityLogger.Log(TAG, $"Material for '{slabel}' set to '{mlabel}' (index {materialIndex}).");
            CurrentMaterialChanged?.Invoke(shapeIndex, materialIndex);
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
        void OnDestroy()
        {
            _input?.Dispose();
            ConstructChanged -= ScheduleAutosave;
            // If a save was pending, write it now so the slot file
            // ends the session current. The scene tear-down path has
            // already invoked ConstructChanged for any final edits.
            if (_autosaveRoutine != null && GameData.ActiveSlot >= 0)
            {
                StopCoroutine(_autosaveRoutine);
                _autosaveRoutine = null;
                FlushSaveNow();
            }
        }

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

            // Subscribe AFTER the initial ConstructChanged below so the
            // first reinstantiation doesn't kick off an autosave that
            // just re-writes what we loaded.
            CurrentShapeChanged?.Invoke(_currentShapeIndex);
            CurrentMaterialChanged?.Invoke(_currentShapeIndex, CurrentMaterialIndex);
            CurrentToolChanged?.Invoke(_currentTool);
            ConstructChanged?.Invoke();

            ConstructChanged += ScheduleAutosave;
            if (GameData.ActiveSlot < 0)
            {
                Debug.unityLogger.LogWarning(TAG,
                    "ActiveSlot is unset — autosave disabled. Enter BuildScene via HangarSelect to enable saving.");
            }
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
                SpawnPlacedCube(p.Cell, p.ShapeIndex, p.MaterialIndex, p.Rotation);
            }
        }

        // Instantiate a shape prefab at a cell, then apply the chosen
        // material's renderer + stats. Order matters: material apply
        // overwrites the prefab's stat fields so the per-cube CubeStats
        // reflects MaterialDefinition values, not prefab placeholders.
        void SpawnPlacedCube(Vector3Int cell, int shapeIndex, int materialIndex, Quaternion rotation)
        {
            GameObject prefab = ResolveShapePrefab(shapeIndex);
            if (prefab == null) return;
            Vector3 pos = new Vector3(cell.x, cell.y, cell.z);
            GameObject go = Instantiate(prefab, pos, rotation, cubeRoot);

            PlacedCubeData data = go.GetComponent<PlacedCubeData>();
            if (data == null) data = go.AddComponent<PlacedCubeData>();
            data.cell = cell;

            if (materialRegistry != null)
            {
                MaterialDefinition mdef = materialRegistry.Get(materialIndex);
                mdef?.ApplyTo(go);
            }

            _spawned[cell] = go;
        }

        GameObject ResolveShapePrefab(int shapeIndex)
        {
            if (shapeRegistry == null)
            {
                Debug.unityLogger.LogError(TAG, "No ShapeRegistry assigned on BuildManager.");
                return null;
            }
            ShapeDefinition def = shapeRegistry.Get(shapeIndex);
            if (def == null || def.prefab == null)
            {
                Debug.unityLogger.LogError(TAG, $"ShapeDefinition or prefab missing for index {shapeIndex}.");
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
            int materialIndex = CurrentMaterialIndex;

            // Mass-budget gate: deny placement if the additional cube would
            // push the construct over the limit, surface a transient message.
            float prospectiveCubeMass = 0f;
            if (materialRegistry != null)
            {
                MaterialDefinition mdef = materialRegistry.Get(materialIndex);
                if (mdef != null) prospectiveCubeMass = mdef.mass;
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

            if (!GameData.TryAdd(cell, _currentShapeIndex, materialIndex, _currentRotation)) return;
            SpawnPlacedCube(cell, _currentShapeIndex, materialIndex, _currentRotation);
            Debug.unityLogger.Log(TAG,
                $"Cube placed at {cell} (shape {_currentShapeIndex}, material {materialIndex}, rot {_currentRotation.eulerAngles}). " +
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

        // ---------- Autosave ----------

        // Restart-on-each-call debounce: every ConstructChanged kicks
        // off a fresh wait, so a burst of edits collapses into a single
        // write at the end of the window. No-op when no slot is armed.
        void ScheduleAutosave()
        {
            if (GameData.ActiveSlot < 0) return;
            if (shapeRegistry == null || materialRegistry == null) return;
            if (_autosaveRoutine != null) StopCoroutine(_autosaveRoutine);
            _autosaveRoutine = StartCoroutine(AutosaveAfterDelay());
        }

        IEnumerator AutosaveAfterDelay()
        {
            yield return new WaitForSeconds(autosaveDebounceSeconds);
            FlushSaveNow();
            _autosaveRoutine = null;
        }

        void FlushSaveNow()
        {
            int slot = GameData.ActiveSlot;
            if (slot < 0) return;
            string slotName = string.IsNullOrEmpty(autosaveSlotName)
                ? $"Slot {slot + 1}"
                : autosaveSlotName;
            ConstructSave save = GameData.ToSave(slotName, shapeRegistry, materialRegistry);
            SaveManager.Save(slot, save);
        }
    }
}
