using UnityEngine;

public enum BlockType
{
    Home,       // I  - 稳定
    Lift,       // IV - 上扬
    Pull,       // V  - 张力
    Shadow,     // vi - 情绪
    Turret,     // 炮台 - 触发鼓点
    Empty       // 纯路径，不触发音乐
}

[CreateAssetMenu(menuName = "Game/Block")]
public class BlockData : ScriptableObject
{
    [Header("Type")]
    public BlockType blockType;

    [Header("Shape")]
    public Vector3Int[] cells;

    [Header("Audio")]
    public AK.Wwise.Event onStepEvent;   
    public AK.Wwise.Event previewEvent;   
}