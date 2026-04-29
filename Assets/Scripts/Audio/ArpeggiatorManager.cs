// ArpeggiatorManager.cs 新建一个专门管琶音的 class
using System.Collections;
using UnityEngine;

public class ArpeggiatorManager : MonoBehaviour
{
    public static ArpeggiatorManager Instance;

    // C大调音阶半音值（相对C4）
    // C  D  E  F  G  A  B  C5
    // 0  2  4  5  7  9  11 12

    // 每种 block 的琶音 pattern（半音数组）
    static readonly int[] Pattern_Home = { 0, 4, 7, 12 };      // C E G C5  上行
    static readonly int[] Pattern_Lift = { 5, 9, 12, 17 };     // F A C5 F5 上行
    static readonly int[] Pattern_Pull = { 7, 11, 14, 19 };    // G B D5 G5 紧张
    static readonly int[] Pattern_Shadow = { -3, 0, 4, 9 };      // A3 C E A  从低开始

    float bpm = 120f;
    Coroutine currentArp;

    void Awake() => Instance = this;

    public void SetBPM(float newBpm) => bpm = newBpm;

    public void PlayArp(BlockType type)
    {
        if (currentArp != null)
            StopCoroutine(currentArp);

        int[] pattern = type switch
        {
            BlockType.Home => Pattern_Home,
            BlockType.Lift => Pattern_Lift,
            BlockType.Pull => Pattern_Pull,
            BlockType.Shadow => Pattern_Shadow,
            _ => Pattern_Home
        };

        currentArp = StartCoroutine(ArpRoutine(pattern));
    }

    public void StopArp()
    {
        if (currentArp != null)
            StopCoroutine(currentArp);
    }

    IEnumerator ArpRoutine(int[] pattern)
    {
        // 把一个 beat 均分给 pattern 里的每个音
        float secPerBeat = 60f / bpm;
        float secPerNote = secPerBeat / pattern.Length;

        foreach (int semitone in pattern)
        {
            AudioManager.Instance.PlayArpNote(semitone);
            yield return new WaitForSeconds(secPerNote);
        }
    }
}