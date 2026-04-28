using UnityEngine;


public enum HarmonicFunction
{
    Tonic,        // 稳定（I / vi）
    Subdominant,  // 展开（ii / IV）
    Dominant,     // 紧张（V / vii°）
    Color         // 色彩/扩展（sus, add9, open）
}
[CreateAssetMenu(menuName = "Music/Block Music Data")]
public class BlockMusicData : ScriptableObject
{
    public HarmonicFunction function;
    public int weight;        // 影响强度（可选）
    public int inversion;     // 转位（0,1,2）
    public bool useSeventh;   // 是否加7度
}
public class BlockRenderer : MonoBehaviour
{
    public GameObject cubePrefab;

    public void Render(Vector3Int basePos, Vector3Int[] cells, float size, GridSystem grid)
    {
        foreach (var cell in cells)
        {
            var cube = Instantiate(cubePrefab, transform);

            cube.transform.position = grid.GridToWorld(basePos + cell);
        }
    }
}