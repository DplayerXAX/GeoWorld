using UnityEngine;


public enum HarmonicFunction
{
    Tonic,        // �ȶ���I / vi��
    Subdominant,  // չ����ii / IV��
    Dominant,     // ���ţ�V / vii�㣩
    Color         // ɫ��/��չ��sus, add9, open��
}
[CreateAssetMenu(menuName = "Music/Block Music Data")]
public class BlockMusicData : ScriptableObject
{
    
    public HarmonicFunction function;
    public int weight;        // Ӱ��ǿ�ȣ���ѡ��
    public int inversion;     // תλ��0,1,2��
    public bool useSeventh;   // �Ƿ��7��
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