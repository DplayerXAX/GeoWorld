using UnityEngine;


public enum HarmonicFunction
{
    Tonic,      
    Subdominant, 
    Dominant,    
    Color        
}
[CreateAssetMenu(menuName = "Music/Block Music Data")]
public class BlockMusicData : ScriptableObject
{
    
    public HarmonicFunction function;
    public int weight;      
    public int inversion;     
    public bool useSeventh;   
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