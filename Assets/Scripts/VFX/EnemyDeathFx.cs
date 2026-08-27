using UnityEngine;

// One-shot death burst: a handful of small primitive shards flying outward from
// the kill point, tumbling, shrinking, and fading before self-destructing.
// Deliberately rhymes with EnemyChaoticVisual's own idle shard body (primitives,
// MPB-tinted, no shadows) rather than introducing a new visual language — this
// reads as "the enemy's own body flying apart", not a generic effects flash.
//
// No ParticleSystem, no prefab: same runtime-built, self-contained convention as
// every other one-shot VFX in this project (CurrencyFlyFx, SynergyBuffFx).
public static class EnemyDeathFx
{
    const int   ShardCount   = 6;
    const float Life         = 0.55f;
    const float ShardSize    = 0.16f;
    const float OutwardSpeed = 2.6f;
    const float SpinSpeed    = 420f;

    static Material _mat;
    static readonly PrimitiveType[] Shapes = { PrimitiveType.Cube, PrimitiveType.Sphere };

    // Transparent so the alpha fade (see DeathShard.Update) actually shows —
    // same recipe as every other translucent runtime material in this project
    // (e.g. LevelMapController.RewardSuggestMaterial).
    static Material Mat()
    {
        if (_mat != null) return _mat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _mat = new Material(sh) { name = "EnemyDeathShard" };
        if (_mat.HasProperty("_Surface"))
        {
            _mat.SetFloat("_Surface", 1f);
            _mat.SetFloat("_ZWrite", 0f);
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return _mat;
    }

    // `palette` = colors to draw shards from (e.g. the dying enemy's own
    // EnemyChaoticVisual.shardPalette, so the burst matches what just broke
    // apart). Falls back to GeoPalette.Signal when null/empty.
    public static void Explode(Vector3 worldPos, Color[] palette = null)
    {
        for (int i = 0; i < ShardCount; i++)
        {
            var shape = Shapes[Random.Range(0, Shapes.Length)];
            var go = GameObject.CreatePrimitive(shape);
            go.name = "DeathShard";
            go.transform.position   = worldPos;
            go.transform.rotation   = Random.rotation;
            go.transform.localScale = Vector3.one * ShardSize * Random.Range(0.7f, 1.15f);

            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial    = Mat();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows    = false;

            Color c = (palette != null && palette.Length > 0)
                    ? palette[Random.Range(0, palette.Length)]
                    : GeoPalette.Signal;
            MpbColor.Set(rend, c);

            // Roughly spherical outward spray, biased upward a touch so the burst
            // reads as "popping" rather than "spraying flat along the ground".
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y) * 0.6f + 0.2f;
            Vector3 velocity = dir.normalized * OutwardSpeed * Random.Range(0.6f, 1.3f);
            Vector3 spinAxis = Random.onUnitSphere;

            go.AddComponent<DeathShard>().Init(velocity, spinAxis, c);
        }
    }

    class DeathShard : MonoBehaviour
    {
        Vector3 _velocity, _spinAxis;
        Color   _color;
        float   _t;
        Renderer _rend;

        public void Init(Vector3 velocity, Vector3 spinAxis, Color color)
        {
            _velocity = velocity;
            _spinAxis = spinAxis;
            _color    = color;
            _rend     = GetComponent<Renderer>();
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / Life);

            // Gravity-ish drop plus a mild drag, so shards arc rather than fly
            // straight — cheap enough to just hand-roll instead of using Rigidbody.
            _velocity += Vector3.down * (6f * Time.deltaTime);
            _velocity *= 1f - 1.5f * Time.deltaTime;
            transform.position += _velocity * Time.deltaTime;
            transform.Rotate(_spinAxis, SpinSpeed * Time.deltaTime, Space.World);

            float shrink = 1f - k;
            transform.localScale = Vector3.one * (ShardSize * shrink);

            var c = _color; c.a = shrink;
            if (_rend != null) MpbColor.Set(_rend, c);

            if (_t >= Life) Destroy(gameObject);
        }
    }
}
