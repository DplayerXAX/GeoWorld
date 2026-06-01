using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Suppress URP Compatibility-Mode obsolete-API warnings (Configure /
// Execute / cameraColorTargetHandle / RenderingUtils.ReAllocateIfNeeded).
// The compat path is intentionally retained as a fallback alongside the
// modern RecordRenderGraph implementation.
#pragma warning disable CS0618, CS0672


// Fullscreen "oil paint" overlay: UV jitter + canvas grain + sat/contrast bump.
// Pair with SketchyOutlineFeature for the full painted-canvas look.
//
// To enable: PC_Renderer.asset → Renderer Features → Add → PaintTexture
public class PaintTextureFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        [Header("Flow field — large-scale wet-pigment warping")]
        [Tooltip("Lower = bigger, more sweeping colour regions. Higher = tighter swirls.")]
        [Range(0.3f, 20f)] public float flowScale       = 3f;
        [Tooltip("How far colours get pushed (in pixels).")]
        [Range(0f, 80f)]   public float flowStrength    = 28f;
        [Range(1, 6)]      public int   flowOctaves     = 4;
        [Tooltip("0 = pure fbm. >0 = swirls/curls — fluid-like motion.")]
        [Range(0f, 1.5f)]  public float flowDomainWarp  = 0.7f;
        [Tooltip("Slow animated drift. 0 = static painting.")]
        [Range(0f, 0.5f)]  public float flowTimeSpeed   = 0.03f;

        [Header("Brush layer")]
        [Range(0f, 1f)]      public float brushStrength = 0.30f;
        public float                       brushScale   = 12f;
        [Range(-180f, 180f)] public float brushAngle    = 35f;
        [Range(0f, 1f)]      public float brushTint     = 0.30f;

        [Header("Flatten into colour patches")]
        [Range(2f, 32f)] public float posterizeLevels = 14f;
        [Range(0f, 1f)]  public float posterizeMix    = 0.35f;

        [Header("Grain")]
        [Range(0f, 0.3f)] public float grainStrength  = 0.08f;
        public float                    grainFrequency = 1200f;

        [Header("Palette")]
        [Range(0.5f, 2f)] public float saturation = 1.20f;
        [Range(0.5f, 2f)] public float contrast   = 1.08f;
    }

    public Settings settings = new Settings();

    PaintTexturePass _pass;
    Material         _material;

    public override void Create()
    {
        if (_material == null)
        {
            var sh = Shader.Find("GeoWorld/PaintTexture");
            if (sh != null) _material = CoreUtils.CreateEngineMaterial(sh);
            else Debug.LogError("[PaintTexture] Shader 'GeoWorld/PaintTexture' not found.");
        }

        _pass = new PaintTexturePass(_material, settings)
        {
            renderPassEvent = settings.renderEvent,
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null) return;
        if (renderingData.cameraData.cameraType != CameraType.Game) return;

        _pass.SetSource(renderer.cameraColorTargetHandle);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        if (_material != null) CoreUtils.Destroy(_material);
        _pass?.Dispose();
    }

    // ── Pass ────────────────────────────────────────────────────────────────
    class PaintTexturePass : ScriptableRenderPass
    {
        readonly Material _mat;
        readonly Settings _settings;
        RTHandle          _source;
        RTHandle          _temp;

        public PaintTexturePass(Material mat, Settings s)
        {
            _mat      = mat;
            _settings = s;
        }

        public void SetSource(RTHandle src) => _source = src;

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor desc)
        {
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref _temp, desc, FilterMode.Bilinear,
                                              TextureWrapMode.Clamp, name: "_PaintTexture_Temp");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_mat == null || _source == null) return;
            UpdateMaterial();

            var cmd = CommandBufferPool.Get("PaintTexture");
            Blitter.BlitCameraTexture(cmd, _source, _temp, _mat, 0);
            Blitter.BlitCameraTexture(cmd, _temp,   _source);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // ── RenderGraph path ────────────────────────────────────────────────
        class PassData
        {
            public TextureHandle source;
            public Material      material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_mat == null) return;

            var resourceData = frameData.Get<UniversalResourceData>();
            if (resourceData.isActiveTargetBackBuffer) return;

            UpdateMaterial();

            var src  = resourceData.activeColorTexture;
            var desc = renderGraph.GetTextureDesc(src);
            desc.name            = "_PaintTexture_Temp";
            desc.depthBufferBits = 0;
            desc.clearBuffer     = false;
            var temp = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("PaintTexture_Apply", out var data))
            {
                data.source   = src;
                data.material = _mat;

                builder.UseTexture(src);
                builder.SetRenderAttachment(temp, 0);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, 0);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("PaintTexture_Copy", out var data))
            {
                data.source   = temp;
                data.material = null;

                builder.UseTexture(temp);
                builder.SetRenderAttachment(src, 0);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), 0, false);
                });
            }
        }

        void UpdateMaterial()
        {
            _mat.SetFloat("_FlowScale",       _settings.flowScale);
            _mat.SetFloat("_FlowStrength",    _settings.flowStrength);
            _mat.SetFloat("_FlowOctaves",     _settings.flowOctaves);
            _mat.SetFloat("_FlowDomainWarp",  _settings.flowDomainWarp);
            _mat.SetFloat("_FlowTimeSpeed",   _settings.flowTimeSpeed);

            _mat.SetFloat("_BrushStrength",   _settings.brushStrength);
            _mat.SetFloat("_BrushScale",      _settings.brushScale);
            _mat.SetFloat("_BrushAngle",      _settings.brushAngle);
            _mat.SetFloat("_BrushTint",       _settings.brushTint);

            _mat.SetFloat("_PosterizeLevels", _settings.posterizeLevels);
            _mat.SetFloat("_PosterizeMix",    _settings.posterizeMix);

            _mat.SetFloat("_GrainStrength",   _settings.grainStrength);
            _mat.SetFloat("_GrainFrequency",  _settings.grainFrequency);

            _mat.SetFloat("_Saturation",      _settings.saturation);
            _mat.SetFloat("_Contrast",        _settings.contrast);
        }

        public void Dispose()
        {
            _temp?.Release();
        }
    }
}
