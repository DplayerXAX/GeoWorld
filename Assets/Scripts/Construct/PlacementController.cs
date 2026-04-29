using System.Collections.Generic;
using UnityEngine;

public enum PlacementMode { Edit, Select }

[RequireComponent(typeof(GridSystem))]
public class PlacementController : MonoBehaviour
{
    public PlacementMode mode = PlacementMode.Select;
    public BlockData[] blocks;
    public GridSystem grid;
    public BlockData currentBlock;
    public GameObject cubePrefab;
    public Transform previewParent;
    public OrbitCamera cam;
    public Vector3Int SnappedGridPos => baseGridPos;

    [Range(0.5f, 4f)] public float snapGridRadius = 1.5f;
    public float minDepth = 2f, maxDepth = 40f, scrollSpeed = 3f, rotateSpeed = 10f;

    private Vector3Int baseGridPos, currentGridPos, manualOffset;
    private float _depth = 10f;
    private Quaternion _currentRotation = Quaternion.identity, _targetRotation = Quaternion.identity;
    private List<GameObject> previewCubes = new();

    private static readonly Vector3Int[] Directions =
    {
        Vector3Int.up, Vector3Int.down,
        Vector3Int.left, Vector3Int.right,
        new(0,0,1), new(0,0,-1)
    };

    private PlacedBlockInstance selectedInstance;
    private GameObject activePhysicsObject;
    private Transform editFocusAnchor;

    private GameObject lastHighlightedObject;
    private Color currentColor;

    private bool isPickingUpObject = false;
    private Vector3 lastObjectPos;
    private Quaternion lastObjectRot;
    private Vector3Int[] lastObjectCells;

    void Awake()
    {
        editFocusAnchor = new GameObject("EditFocusAnchor").transform;
    }

    void Start()
    {
        currentBlock = GetRandomBlock();
        currentColor = GetRandomColor();
    }

    public void SpawnRoundBlocks(int count)
    {
        if (cubePrefab == null || blocks == null || blocks.Length == 0)
            return;

        for (int i = 0; i < count; i++)
        {
            BlockData data = blocks[Random.Range(0, blocks.Length)];
            if (data == null || data.cells == null) continue;

            Vector3 pos =
                cam.transform.position +
                cam.transform.forward * 5f +
                Random.insideUnitSphere * 2f;

            GameObject obj = new GameObject("PhysicsBlock");

            obj.transform.position = pos;
            obj.transform.rotation = Quaternion.identity;
            Color co=GetRandomColor();
            foreach (var cell in data.cells)
            {
                GameObject c = Instantiate(cubePrefab, obj.transform);
                c.transform.localPosition = (Vector3)cell * grid.cellSize;
                c.GetComponent<Renderer>().material.color = co;
            }

            obj.AddComponent<Rigidbody>();
            obj.AddComponent<SelectableBlock>().data = data;

            var col = obj.AddComponent<BoxCollider>();
            col.size = Vector3.one * 2.5f;
        }
    }
    void Update()
    {
        _currentRotation = Quaternion.Slerp(
            _currentRotation,
            _targetRotation,
            1f - Mathf.Exp(-rotateSpeed * Time.deltaTime)
        );

        HandleScroll();
        HandleMouseMove();
        HandleKeyboardOffset();
        HandleModeSwitch();
        HandleRotate();

        UpdatePreview();

        if (Input.GetMouseButtonDown(0))
        {
            if (mode == PlacementMode.Edit)
            {
                if (currentBlock != null)
                    TryPlace();
            }
            else
            {
                TrySelectObject();
            }
        }

        currentGridPos = baseGridPos + manualOffset;

        if (mode == PlacementMode.Edit && currentBlock != null)
            cam.SetFocus(editFocusAnchor);
    }

    // =========================
    // INPUT
    // =========================

    void HandleScroll()
    {
        float s = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(s) < 0.001f) return;

        if (mode == PlacementMode.Select)
            cam.AddDistance(-s * scrollSpeed * 10f);
        else
            _depth = Mathf.Clamp(_depth - s * scrollSpeed * _depth, minDepth, maxDepth);
    }

    void HandleMouseMove()
    {
        Ray r = cam.myCam.ScreenPointToRay(Input.mousePosition);
        Vector3 world = r.origin + r.direction * _depth;
        baseGridPos = grid.WorldToGrid(world);
    }

    void HandleKeyboardOffset()
    {
        if (Input.GetKeyDown(KeyCode.W)) manualOffset += Vector3Int.up;
        if (Input.GetKeyDown(KeyCode.S)) manualOffset += Vector3Int.down;
        if (Input.GetKeyDown(KeyCode.D)) manualOffset += Vector3Int.right;
        if (Input.GetKeyDown(KeyCode.A)) manualOffset += Vector3Int.left;
    }

    void HandleRotate()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1)) _targetRotation *= Quaternion.Euler(90, 0, 0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) _targetRotation *= Quaternion.Euler(0, 90, 0);
        if (Input.GetKeyDown(KeyCode.Alpha3)) _targetRotation *= Quaternion.Euler(0, 0, 90);
    }

    void HandleModeSwitch()
    {
        if (!Input.GetKeyDown(KeyCode.Tab)) return;

        if (mode == PlacementMode.Select)
        {
            if (selectedInstance != null)
            {
                isPickingUpObject = true;
                lastObjectPos = selectedInstance.visualObject.transform.position;
                lastObjectRot = selectedInstance.visualObject.transform.rotation;
                lastObjectCells = selectedInstance.occupiedCells.ToArray();

                grid.RemoveInstance(selectedInstance);
                selectedInstance = null;
                EnterEditMode();
            }
            else
            {
                EnterEditMode();
            }
        }
        else
        {
            if (isPickingUpObject)
                CancelAndReturnObject();

            mode = PlacementMode.Select;
            previewParent.gameObject.SetActive(false);
        }
    }

    // =========================
    // SELECT
    // =========================

    void TrySelectObject()
    {
        Ray ray = cam.myCam.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit))
        {
            UpdateHighlight(null);
            return;
        }

        var sb = hit.transform.GetComponentInParent<SelectableBlock>();
        if (sb != null)
        {
            currentBlock = sb.data;
            currentColor = sb.GetComponentInChildren<Renderer>().material.color;
            activePhysicsObject = sb.gameObject;
            selectedInstance = null;

            UpdateHighlight(activePhysicsObject);
            cam.SetFocus(sb.transform);
            return;
        }

        Vector3Int gPos = grid.WorldToGrid(hit.point);
        var instance = grid.GetInstanceAt(gPos);

        if (instance != null)
        {
            selectedInstance = instance;
            currentBlock = instance.data;
            currentColor = instance.visualObject.GetComponentInChildren<Renderer>().material.color;

            activePhysicsObject = null;
            UpdateHighlight(instance.visualObject);
            cam.SetFocus(instance.visualObject.transform);
        }
    }

    void UpdateHighlight(GameObject target)
    {
        if (lastHighlightedObject != null && lastHighlightedObject != target)
        {
            var old = lastHighlightedObject.GetComponent<Outline>();
            if (old) Destroy(old);
        }

        if (target != null && target != lastHighlightedObject)
        {
            var ol = target.AddComponent<Outline>();
            ol.OutlineColor = Color.yellow;
        }

        lastHighlightedObject = target;
    }

    // =========================
    // EDIT
    // =========================

    void EnterEditMode()
    {
        mode = PlacementMode.Edit;
        previewParent.gameObject.SetActive(currentBlock != null);
        manualOffset = Vector3Int.zero;
        UpdateHighlight(null);
    }

    void TryPlace()
    {
        if (currentBlock == null) return;

        var cells = GetRotatedCells();
        if (cells.Length == 0) return;

        if (!CanPlace(currentGridPos, cells)) return;

        Vector3 center = Vector3.zero;
        foreach (var c in cells)
            center += grid.GridToWorld(currentGridPos + c);

        center /= cells.Length;

        GameObject obj = new GameObject("PlacedBlock");
        obj.transform.position = center;
        obj.transform.rotation = _currentRotation;

        var br = obj.AddComponent<BlockRenderer>();
        br.cubePrefab = cubePrefab;
        br.Render(currentGridPos, cells, grid.cellSize, grid);

        foreach (var r in obj.GetComponentsInChildren<Renderer>())
            r.material.color = currentColor;

        PlacedBlockInstance ins = new()
        {
            data = currentBlock,
            visualObject = obj
        };

        foreach (var c in cells)
            ins.occupiedCells.Add(currentGridPos + c);

        grid.RegisterInstance(ins);

        currentBlock = GetRandomBlock();
        mode = PlacementMode.Select;
        previewParent.gameObject.SetActive(false);

        cam.SetFocus(obj.transform);
    }

    void UpdatePreview()
    {
        if (currentBlock == null || mode != PlacementMode.Edit)
        {
            previewParent.gameObject.SetActive(false);
            return;
        }

        var cells = GetRotatedCells();
        if (cells.Length == 0) return;

        bool valid = CanPlace(currentGridPos, cells);

        while (previewCubes.Count < cells.Length)
        {
            var c = Instantiate(cubePrefab, previewParent);
            foreach (var col in c.GetComponentsInChildren<Collider>())
                col.enabled = false;

            previewCubes.Add(c);
        }

        for (int i = 0; i < previewCubes.Count; i++)
            previewCubes[i].SetActive(i < cells.Length);

        Color tint = currentColor;
        tint.a = valid ? 0.5f : 0.3f;

        for (int i = 0; i < cells.Length; i++)
        {
            previewCubes[i].transform.position =
                grid.GridToWorld(currentGridPos + cells[i]);

            previewCubes[i].GetComponent<Renderer>().material.color = tint;
        }
    }

    Vector3Int[] GetRotatedCells()
    {
        if (currentBlock == null || currentBlock.cells == null)
            return System.Array.Empty<Vector3Int>();

        Vector3Int[] res = new Vector3Int[currentBlock.cells.Length];

        for (int i = 0; i < res.Length; i++)
            res[i] = Vector3Int.RoundToInt(_currentRotation * (Vector3)currentBlock.cells[i]);

        return res;
    }

    bool CanPlace(Vector3Int bp, Vector3Int[] cs)
    {
        foreach (var c in cs)
        {
            var p = bp + c;
            if (grid.IsOccupied(p) || p.y < 0)
                return false;
        }
        return true;
    }

    // =========================
    // PICKUP RETURN
    // =========================

    void CancelAndReturnObject()
    {
        if (lastObjectCells != null)
        {
            GameObject obj = new GameObject("ReturnedBlock");
            obj.transform.position = lastObjectPos;
            obj.transform.rotation = lastObjectRot;

            var br = obj.AddComponent<BlockRenderer>();
            br.cubePrefab = cubePrefab;

            Vector3Int origin = grid.WorldToGrid(lastObjectPos);

            Vector3Int[] rel = new Vector3Int[lastObjectCells.Length];
            for (int i = 0; i < rel.Length; i++)
                rel[i] = lastObjectCells[i] - origin;

            br.Render(origin, rel, grid.cellSize, grid);

            PlacedBlockInstance ins = new()
            {
                data = currentBlock,
                visualObject = obj
            };

            foreach (var c in lastObjectCells)
                ins.occupiedCells.Add(c);

            grid.RegisterInstance(ins);
        }
        else
        {
            SpawnPhysicsBlockAt(lastObjectPos, lastObjectRot);
        }

        isPickingUpObject = false;
    }

    void SpawnPhysicsBlockAt(Vector3 pos, Quaternion rot)
    {
        GameObject obj = new GameObject("PhysicsBlock");
        obj.transform.position = pos;
        obj.transform.rotation = rot;

        if (currentBlock == null) return;

        foreach (var cell in currentBlock.cells)
        {
            GameObject c = Instantiate(cubePrefab, obj.transform);
            c.transform.localPosition = (Vector3)cell * grid.cellSize;
            c.GetComponent<Renderer>().material.color = currentColor;
        }

        obj.AddComponent<Rigidbody>();
        obj.AddComponent<SelectableBlock>().data = currentBlock;
        obj.AddComponent<BoxCollider>().size = Vector3.one * 2.5f;
    }

    // =========================
    // UTIL
    // =========================

    BlockData GetRandomBlock()
    {
        if (blocks == null || blocks.Length == 0) return null;
        return blocks[Random.Range(0, blocks.Length)];
    }

    Color GetRandomColor()
    {
        return Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.7f, 1f);
    }
}