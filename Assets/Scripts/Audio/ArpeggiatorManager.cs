// ArpeggiatorManager.cs �½�һ��ר�Ź������� class
using System.Collections;
using UnityEngine;

public class ArpeggiatorManager : MonoBehaviour
{
    public static ArpeggiatorManager Instance;

    // C������װ���ֵ�����C4��
    // C  D  E  F  G  A  B  C5
    // 0  2  4  5  7  9  11 12

    // ÿ�� block ������ pattern���������飩
    static readonly int[] Pattern_Home = { 0, 4, 7, 12 };      // C E G C5  ����
    static readonly int[] Pattern_Lift = { 5, 9, 12, 17 };     // F A C5 F5 ����
    static readonly int[] Pattern_Pull = { 7, 11, 14, 19 };    // G B D5 G5 ����
    static readonly int[] Pattern_Shadow = { -3, 0, 4, 9 };      // A3 C E A  �ӵͿ�ʼ

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
        // ��һ�� beat ���ָ� pattern ���ÿ����
        float secPerBeat = 60f / bpm;
        float secPerNote = secPerBeat / pattern.Length;

        foreach (int semitone in pattern)
        {
            AudioManager.Instance.PlayArpNote(semitone);
            BackgroundReactor.Instance?.OnNote(1f);
            yield return new WaitForSeconds(secPerNote);
        }
    }
}