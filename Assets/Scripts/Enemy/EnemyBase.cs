using UnityEngine;

public class EnemyBase : MonoBehaviour
{
    [Tooltip("Optional scene marker for an enemy spawn/base. The v1 system uses the active path start by default.")]
    public bool useCurrentPathStart = true;
}
