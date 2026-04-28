using System.Collections.Generic;
using System.Net;
using UnityEngine;

public class SurfaceUnit : MonoBehaviour
{
    public float speed = 5f;

    [Header("Music")]
    public GridSystem grid;
    public System.Func<Vector3Int, BlockData> getBlockFromCell;
    BlockData lastCheckedBlock;
    List<FaceNode> path;
    int index;

    BlockData currentBlock;

    private void Start()
    {
        getBlockFromCell = GridSystem.instance.GetBlock;
        grid = GridSystem.instance;
    }


    public void SetPath(List<FaceNode> newPath)
    {
        path = newPath;
        index = 0;
        currentBlock = null;
    }

    void Update()
    {
        if (path == null || index >= path.Count)
            return;

        FaceNode node = path[index];

        MoveTo(node);

        if (Vector3.Distance(transform.position, node.worldPos) < 0.01f)
        {
            OnReachNode(node); 
            index++;
        }
    }

    void MoveTo(FaceNode node)
    {
        transform.position = Vector3.MoveTowards(
            transform.position,
            node.worldPos,
            speed * Time.deltaTime
        );
    }

    void OnReachNode(FaceNode node)
    {
        if (grid == null || getBlockFromCell == null) 
        {
            Debug.LogWarning("No grid system");
            return;
        }

        Vector3Int cell = node.cell;
        BlockData block = getBlockFromCell(node.cell);

        if (block == null)
        {
            Debug.LogWarning("Can't find right block");
            return;
        }

        if (block == currentBlock)
            return;

        currentBlock = block;

        OnEnterBlock(block, cell);
    }

    void OnEnterBlock(BlockData block, Vector3Int cell)
    {
        //GameObject blockObj = grid.GetBlockObject(cell);

        //if (blockObj == null) return;

        ChordData chord = PickChord(block);
        if (chord == null) Debug.LogWarning("chord is null!");
        chord.myChord.Post(this.gameObject);
        //AudioManager.Instance.PlayChord(chord);
       
        Debug.Log("Play chord at block: " + cell);
    }

    ChordData PickChord(BlockData block)
    {
        if (block.chords == null || block.chords.Count == 0)
            return null;

        return block.chords[Random.Range(0, block.chords.Count)];
    }
}