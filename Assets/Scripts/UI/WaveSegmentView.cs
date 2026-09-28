using UnityEngine;
using UnityEngine.UI;

// The line joining two waves on the wave bar; `fill` (an Image set to Filled,
// horizontal) grows across it as the wave it leads to is fought. A template in the
// WaveBar prefab — style it there.
public class WaveSegmentView : MonoBehaviour
{
    public RectTransform rect;
    public Image line;
    public Image fill;
}
