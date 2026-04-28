using UnityEngine;
using System.Collections.Generic;

public enum PlacementMode
{
    Edit,
    Select
}

[RequireComponent(typeof(GridSystem))]
public class PlacementController : MonoBehaviour
{
    public PlacementMode mode = PlacementMode.Edit;

    [Header("Block Library")]
    public BlockData[] blocks;
    [Header("References")]
    public GridSystem grid;
    public BlockData currentBlock;
    public GameObject cubePrefab;
    public Transform previewParent;
    public OrbitCamera cam;

    [Header("Snapping")]
    [Range(0.5f, 4f)]
    public float snapGridRadius = 1.5f;
    private Vector3Int baseGridPos;
    public Vector3Int SnappedGridPos { get; private set; }

    [Header("Placement Depth")]
    public float minDepth = 2f;
    public float maxDepth = 40f;
    public float scrollSpeed = 3f;

    [Header("Rotation")]
    public float rotateSpeed = 10f;

    private Vector3Int currentGridPos;
    private Vector3Int manualOffset = Vector3Int.zero;
    private float _depth = 10f;
    private Quaternion _currentRotation = Quaternion.identity;
    private Quaternion _targetRotation = Quaternion.identity;

    private readonly List<GameObject> previewCubes = new();

    private static readonly Vector3Int[] Directions =
    {
        Vector3Int.up,
        Vector3Int.down,
        Vector3Int.left,
        Vector3Int.right,
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1)
    };
    void Start()
    {
        currentBlock = GetRandomBlock();
    }

    void TrySelectBlock()
    {
        Ray ray = cam.myCam.ScreenPointToRay(Input.mousePosition);

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Transform root = hit.transform;

            while (root != null && !root.name.Contains("Block"))
                root = root.parent;

            if (root != null && cam != null)
            {
                Debug.Log("Setting!");
                cam.SetFocus(root);
            }
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
                TryPlace();
            else if (mode == PlacementMode.Select)
                TrySelectBlock();

        }
        currentGridPos = baseGridPos + manualOffset;
    }

    void HandleScroll()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

        if (mode == PlacementMode.Select)
        {
            cam.AddDistance(-scroll * scrollSpeed * 10f);
        }
        else
        {
            _depth -= scroll * scrollSpeed * _depth;
            _depth = Mathf.Clamp(_depth, minDepth, maxDepth);
        }
    }
    Color GetRandomColor()
    {
        return Random.ColorHSV(
            0f, 1f,  
            0.6f, 1f, 
            0.7f, 1f 
        );
    }
    void HandleMouseMove()
    {
        Ray ray = cam.myCam.ScreenPointToRay(Input.mousePosition);
        Vector3 worldPoint = ray.origin + ray.direction * _depth;

        Vector3Int naturalPos = grid.WorldToGrid(worldPoint);
        Vector3Int snapped = TrySnap(naturalPos, ray);

        SnappedGridPos = snapped;

        baseGridPos = snapped; 
    }

    void HandleKeyboardOffset()
    {
        if (Input.GetKeyDown(KeyCode.W)) manualOffset += Vector3Int.up;
        if (Input.GetKeyDown(KeyCode.S)) manualOffset += Vector3Int.down;
        if (Input.GetKeyDown(KeyCode.D)) manualOffset += GetCameraRight();
        if (Input.GetKeyDown(KeyCode.A)) manualOffset -= GetCameraRight();

        if (Input.GetKeyDown(KeyCode.R)) manualOffset += Vector3Int.up;
        if (Input.GetKeyDown(KeyCode.F)) manualOffset += Vector3Int.down;
    }

    void HandleModeSwitch()
    {
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            mode = mode == PlacementMode.Edit
                ? PlacementMode.Select
                : PlacementMode.Edit;

            OnModeChanged(mode);
        }
    }

    void OnModeChanged(PlacementMode newMode)
    {
        switch (newMode)
        {
            case PlacementMode.Select:
                //Cursor.visible = true;
                //Cursor.lockState = CursorLockMode.None;

                previewParent.gameObject.SetActive(false);
                break;

            case PlacementMode.Edit:
                //Cursor.visible = false;
                //Cursor.lockState = CursorLockMode.Locked;

                previewParent.gameObject.SetActive(true);
                break;
        }
    }

    Vector3Int GetCameraForward()
    {
        Vector3 flat = cam.transform.forward;
        flat.y = 0;

        if (flat.sqrMagnitude < 0.001f)
            flat = new Vector3(cam.transform.up.x, 0, cam.transform.up.z);

        return RoundToAxis(flat.normalized);
    }

    Vector3Int GetCameraRight()
    {
        Vector3 flat = cam.transform.right;
        flat.y = 0;
        return RoundToAxis(flat.normalized);
    }

    void HandleRotate()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            _targetRotation *= Quaternion.Euler(90, 0, 0);
            AudioManager.Instance.PlayRotate();
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            _targetRotation *= Quaternion.Euler(0, 90, 0);
            AudioManager.Instance.PlayRotate();
        }

        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            _targetRotation *= Quaternion.Euler(0, 0, 90);
            AudioManager.Instance.PlayRotate();
        }
    }

    Vector3Int[] GetRotatedCells()
    {
        Vector3Int[] result = new Vector3Int[currentBlock.cells.Length];

        for (int i = 0; i < result.Length; i++)
        {
            Vector3 rotated = _currentRotation * (Vector3)currentBlock.cells[i];
            result[i] = Vector3Int.RoundToInt(rotated);
        }

        return result;
    }

    Vector3Int TrySnap(Vector3Int naturalPos, Ray ray)
    {
        float snapWorldRadius = snapGridRadius * grid.cellSize;

        Vector3Int best = naturalPos;
        float bestDist = float.MaxValue;

        int r = Mathf.CeilToInt(snapGridRadius);

        for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
                for (int dz = -r; dz <= r; dz++)
                {
                    Vector3Int occupied = naturalPos + new Vector3Int(dx, dy, dz);
                    if (!grid.IsOccupied(occupied)) continue;

                    foreach (var dir in Directions)
                    {
                        Vector3Int candidate = occupied + dir;
                        if (grid.IsOccupied(candidate)) continue;

                        float dist = DistancePointToRay(grid.GridToWorld(candidate), ray);

                        if (dist < snapWorldRadius && dist < bestDist)
                        {
                            bestDist = dist;
                            best = candidate;
                        }
                    }
                }

        return best;
    }

    bool CanPlace(Vector3Int basePos, Vector3Int[] cells)
    {
        foreach (var cell in cells)
        {
            var pos = basePos + cell;
            if (grid.IsOccupied(pos) || pos.y < 0)
                return false;
        }

        return true;
    }

    void TryPlace()
    {
        _currentRotation = _targetRotation;

        var cells = GetRotatedCells();
        if (!CanPlace(currentGridPos, cells)) return;

        Vector3 center = Vector3.zero;
        foreach (var cell in cells)
            center += grid.GridToWorld(currentGridPos + cell);
        center /= cells.Length;

        GameObject parent = new GameObject("Block");
        parent.transform.position = center;

        var renderer = parent.AddComponent<BlockRenderer>();
        renderer.cubePrefab = cubePrefab;
        renderer.Render(currentGridPos, cells, grid.cellSize, grid);

        Color color = GetRandomColor();
        foreach (var r in parent.GetComponentsInChildren<Renderer>())
            r.material.color = color;

        foreach (var cell in cells)
            grid.SetOccupied(currentGridPos + cell, currentBlock);

        currentBlock = GetRandomBlock();

        manualOffset = Vector3Int.zero;
    }

    void UpdatePreview()
    {
        var cells = GetRotatedCells();
        bool valid = CanPlace(currentGridPos, cells);

        while (previewCubes.Count < cells.Length)
        {
            var cube = Instantiate(cubePrefab, previewParent);

            foreach (var col in cube.GetComponentsInChildren<Collider>())
                col.enabled = false;

            previewCubes.Add(cube);
        }

        for (int i = 0; i < previewCubes.Count; i++)
            previewCubes[i].SetActive(i < cells.Length);

        Color tint = valid
            ? new Color(0f, 1f, 0f, 0.5f)
            : new Color(1f, 0f, 0f, 0.5f);

        for (int i = 0; i < cells.Length; i++)
        {
            previewCubes[i].transform.position =
                grid.GridToWorld(currentGridPos + cells[i]);

            previewCubes[i].GetComponent<Renderer>().material.color = tint;
        }
    }

    void OnGUI()
    {
        if (grid == null) return;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 18;
        style.normal.textColor = Color.white;

        string text =
            $"Mode: {mode}\n" +
            $"Snapped: {SnappedGridPos}\n" +
            $"Current: {currentGridPos}";

        GUI.Label(new Rect(10, 10, 300, 100), text, style);
    }
    BlockData GetRandomBlock()
    {
        if (blocks == null || blocks.Length == 0) return null;
        return blocks[Random.Range(0, blocks.Length)];
    }
    static Vector3Int RoundToAxis(Vector3 n)
    {
        float ax = Mathf.Abs(n.x);
        float ay = Mathf.Abs(n.y);
        float az = Mathf.Abs(n.z);

        if (ax >= ay && ax >= az)
            return new Vector3Int((int)Mathf.Sign(n.x), 0, 0);

        if (ay >= ax && ay >= az)
            return new Vector3Int(0, (int)Mathf.Sign(n.y), 0);

        return new Vector3Int(0, 0, (int)Mathf.Sign(n.z));
    }

    static float DistancePointToRay(Vector3 point, Ray ray)
    {
        return Vector3.Cross(ray.direction, point - ray.origin).magnitude;
    }
}