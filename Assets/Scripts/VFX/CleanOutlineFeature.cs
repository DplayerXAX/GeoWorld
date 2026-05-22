using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Constructivism-style hard black outline. Pure Sobel + binary threshold.
// No noise / no charcoal feel — a graphic line that separates geometry from
// the background as if cut from paper.
//
// To enable: PC_Renderer.asset → Renderer Features → Add → CleanOutline
public class CleanOutlineFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [Range(0.5f, 6f)] public float thickness        = 1.6f;
        [Tooltip("Soft dark — feels less cartoony than pure black.")]
        public Color                    outlineColor    = new Color(0.08f, 0.07f, 0.10f, 1f);
        [Range(0f, 1f)]   public float outlineOpacity   = 0.8f;

        [Header("Edge detection")]
        [Range(0f, 5f)]   public float depthThreshold   = 0.5f;
        [Tooltip("0 = outline every cube crease. 1 = silhouette only (recommended).")]
        [Range(0f, 1f)]   public float normalThreshold  = 0.95f;
        [Tooltip("Sub-pixel transition width. Higher = softer / less aliased.")]
        [Range(0.05f, 1f)] public float edgeSoftness    = 0.25f;
    }

    public Settings settings = new Settings();

    CleanOutlinePass _pass;
    Material         _material;

    public override void Create()
    {
        // Start from a clean slate. Domain-reload / inspector tweaks call
        // Create() again; ensure no stale references survive.
        DisposeResources();
        EnsureMaterialAndPass();
    }

    // Shader.Find() can return null during the first Create() pass (asset
    // reimport, domain reload, fresh editor open). Retry every frame in
    // AddRenderPasses until both material and pass are ready.
    void EnsureMaterialAndPass()
    {
        if (_material == null)
        {
            var sh = Shader.Find("GeoWorld/CleanOutline");
            if (sh != null) _material = CoreUtils.CreateEngineMaterial(sh);
        }
        if (_material != null && _pass == null)
        {
            _pass = new CleanOutlinePass(_material, settings)
            {
                renderPassEvent = settings.renderEvent,
            };
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        EnsureMaterialAndPass();
        if (_pass == null) return;
        if (renderingData.cameraData.cameraType != CameraType.Game) return;

        _pass.SetSource(renderer.cameraColorTargetHandle);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing) => DisposeResources();

    void DisposeResources()
    {
        if (_material != null) CoreUtils.Destroy(_material);
        _material = null;
        _pass?.Dispose();
        _pass = null;
    }

    class CleanOutlinePass : ScriptableRenderPass
    {
        readonly Material _mat;
        readonly Settings _settings;
        RTHandle          _source;
        RTHandle          _temp;

        public CleanOutlinePass(Material mat, Settings s)
        {
            _mat      = mat;
            _settings = s;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public void SetSource(RTHandle src) => _source = src;

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor desc)
        {
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref _temp, desc, FilterMode.Bilinear,
                                              TextureWrapMode.Clamp, name: "_CleanOutline_Temp");
        }

        // ── Compatibility mode ──────────────────────────────────────────────
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_mat == null || _source == null) return;
            UpdateMaterial();

            var cmd = CommandBufferPool.Get("CleanOutline");
            Blitter.BlitCameraTexture(cmd, _source, _temp, _mat, 0);
            Blitter.BlitCameraTexture(cmd, _temp,   _source);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // ── RenderGraph mode ────────────────────────────────────────────────
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
            desc.name            = "_CleanOutline_Temp";
            desc.depthBufferBits = 0;
            desc.clearBuffer     = false;
            var temp = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("CleanOutline_Apply", out var data))
            {
                data.source   = src;
                data.material = _mat;

                builder.UseTexture(src);
                if (resourceData.cameraDepthTexture.IsValid())
                    builder.UseTexture(resourceData.cameraDepthTexture);
                if (resourceData.cameraNormalsTexture.IsValid())
                    builder.UseTexture(resourceData.cameraNormalsTexture);
                builder.SetRenderAttachment(temp, 0);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1f, 1f, 0f, 0f), d.material, 0);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("CleanOutline_Copy", out var data))
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
            _mat.SetFloat("_Thickness",       _settings.thickness);
            _mat.SetColor("_OutlineColor",    _settings.outlineColor);
            _mat.SetFloat("_OutlineOpacity",  _settings.outlineOpacity);
            _mat.SetFloat("_DepthThreshold",  _settings.depthThreshold);
            _mat.SetFloat("_NormalThreshold", _settings.normalThreshold);
            _mat.SetFloat("_EdgeSoftness",    _settings.edgeSoftness);
        }

        public void Dispose() { _temp?.Release(); }
    }
}
