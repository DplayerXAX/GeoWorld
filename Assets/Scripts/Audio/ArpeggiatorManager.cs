using UnityEngine;

public class ArpeggiatorManager : MonoBehaviour
{
    public static ArpeggiatorManager Instance;

    class MelodyState
    {
        public int lastNote = 0;
        public int direction = 1;
        public float energy = 0.2f;
    }

    MelodyState state = new MelodyState();

    static readonly int[] Scale_Major = { 0, 2, 4, 5, 7, 9, 11, 12 };

    [Header("Feel")]
    [Range(0f, 1f)] public float noteDensity = 0.8f;   // 控制留白
    [Range(0f, 1f)] public float repetition = 0.3f;    // motif重复概率

    void Awake() => Instance = this;

    // ===== 主旋律入口 =====
    public void PlayMelodyNote(BlockType type, FaceNode node, FaceNode prevNode, float progress, int pathIndex)
    {
        // 留白（很关键）
        if (Random.value > noteDensity)
            return;

        UpdateMelodyFromSpace(node, prevNode);

        int note = PickAmbientNote(Scale_Major);

        // 轻微弧线（避免机械）
        int arc = Mathf.RoundToInt(Mathf.Sin(progress * Mathf.PI) * 2f);
        note += arc;

        // 力度：随进度变化（神圣感 = 平滑）
        float velocity = Mathf.Lerp(0.5f, 0.8f, progress);

        AudioManager.Instance.PlayArpNote(note, velocity);

        BackgroundReactor.Instance?.OnNote(0.6f);
    }

    // ===== 空间 → 旋律行为 =====
    void UpdateMelodyFromSpace(FaceNode current, FaceNode previous)
    {
        if (previous == null) return;

        Vector3 dir = current.worldPos - previous.worldPos;

        // 方向（但不要频繁抖动）
        if (Mathf.Abs(dir.x) > 0.1f)
            state.direction = dir.x > 0 ? 1 : -1;

        // 高度 → energy（但限制很弱）
        float height = current.worldPos.y;
        state.energy = Mathf.Clamp01(height * 0.15f);
    }

    // ===== 旋律核心（级进 + 秩序）=====
    int PickAmbientNote(int[] scale)
    {
        int best = scale[0];
        float bestScore = float.MinValue;

        foreach (int note in scale)
        {
            int dist = note - state.lastNote;

            float score = 0f;

            // 强烈惩罚大跳
            score -= Mathf.Abs(dist) * 2f;

            // 鼓励级进
            if (Mathf.Abs(dist) <= 2)
                score += 3f;

            // 轻微方向一致
            if (Mathf.Sign(dist) == state.direction)
                score += 1f;

            // 中音区偏好
            score -= Mathf.Abs(note - 6) * 0.2f;

            if (score > bestScore)
            {
                bestScore = score;
                best = note;
            }
        }

        // motif（重复）
        if (Random.value < repetition)
            best = state.lastNote;

        state.lastNote = best;
        return best;
    }

    // ===== 和弦低音（保留你原逻辑但更稳）=====
    public void PlayBassRoot(BlockType type)
    {
        int root = type switch
        {
            BlockType.Home => 0,
            BlockType.Lift => 5,
            BlockType.Pull => 7,
            BlockType.Shadow => -3,
            _ => 0
        };

        int bass = root - 12;
        if (bass < -12) bass += 12;

        AudioManager.Instance.PlayArpNote(bass, 0.4f);
        BackgroundReactor.Instance?.OnNote(0.4f);
    }

    public void StopArp() { }
}