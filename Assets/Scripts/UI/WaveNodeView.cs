using UnityEngine;
using UnityEngine.UI;
using TMPro;

// One wave on the wave bar: a hollow ring, a solid dot inside it (on for a wave
// that opens a new portal), and the wave number. A template in the WaveBar prefab —
// style it there; WaveProgressBar clones it per wave.
public class WaveNodeView : MonoBehaviour
{
    public RectTransform rect;
    public Image    ring;
    public Image    dot;
    public TMP_Text label;
}
