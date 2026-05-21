using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Adds a sketchy black outline to all geometry edges (silhouette + creases).
// Uses Sobel on the camera depth + normals textures, with hash-driven UV
// jitter so the outline reads like an ink/brush stroke instead of a clean
// vector line.
//
// To enable: PC_Renderer.asset → Renderer Features → Add → SketchyOutline
public class SketchyOutlineFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        // Run AFTER paint pass so the crisp black lines aren't smeared by jitter.
        public RenderPassEvent renderEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [Header("Outline shape")]
        [Range(0.5f, 4f)]  public float thickness        = 1.5f;
        public Color outlineColor                        = Color.black;
        [Range(0f, 1f)]    public float outlineOpacity   = 1f;

        [Header("Edge detection sensitivity")]
        [Range(0f, 5f)]    public float depthThreshold   = 0.5f;
        [Range(0f, 1f)]    public float normalThreshold  = 0.4f;

        [Header("Sketchiness")]
        [Tooltip("UV jitter amount — higher = more 'hand-drawn' wobble.")]
        [Range(0f, 3f)]    public float noiseStrength    = 0.6f;
        [Tooltip("Hash frequency for the UV jitter.")]
        public float                    noiseFrequency   = 480f;
    }

    public Settings settings = new Settings();

    SketchyOutlinePass _pass;
    Material           _material;

    public override void Create()
    {
        if (_material == null)
        {
            var sh = Shader.Find("GeoWorld/SketchyOutline");
            if (sh != null) _material = CoreUtils.CreateEngineMaterial(sh);
            else Debug.LogError("[SketchyOutline] Shader 'GeoWorld/SketchyOutline' not found.");
        }

        _pass = new SketchyOutlinePass(_material, settings)
        {
            renderPassEvent = settings.renderEvent,
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_material == null) return;
        if (renderingData.cameraData.cameraType != CameraType.Game) return; // skip preview / scene cams

        _pass.SetSource(renderer.cameraColorTargetHandle);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        if (_material != null) CoreUtils.Destroy(_material);
        _pass?.Dispose();
    }

    // ── Pass ────────────────────────────────────────────────────────────────
    class SketchyOutlinePass : ScriptableRenderPass
    {
        readonly Material              _mat;
        readonly Settings              _settings;
        RTHandle                       _source;
        RTHandle                       _temp;

        public SketchyOutlinePass(Material mat, Settings s)
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
                                              TextureWrapMode.Clamp, name: "_SketchyOutline_Temp");
        }

        // ── Compatibility-mode path (RenderGraph off) ───────────────────────
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_mat == null || _source == null) return;
            UpdateMaterial();

            var cmd = CommandBufferPool.Get("SketchyOutline");
            Blitter.BlitCameraTexture(cmd, _source, _temp, _mat, 0);
            Blitter.BlitCameraTexture(cmd, _temp,   _source);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        // ── RenderGraph path (URP 17+ / Unity 6) ────────────────────────────
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
            desc.name             = "_SketchyOutline_Temp";
            desc.depthBufferBits  = 0;
            desc.clearBuffer      = false;
            var temp = renderGraph.CreateTexture(desc);

            // Pass 1: src → temp through outline material.
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SketchyOutline_Apply", out var data))
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

            // Pass 2: temp → src (plain copy).
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("SketchyOutline_Copy", out var data))
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
            _mat.SetFloat("_DepthThreshold",  _settings.depthThreshold);
            _mat.SetFloat("_NormalThreshold", _settings.normalThreshold);
            _mat.SetFloat("_NoiseStrength",   _settings.noiseStrength);
            _mat.SetFloat("_NoiseFrequency",  _settings.noiseFrequency);
            _mat.SetFloat("_OutlineOpacity",  _settings.outlineOpacity);
        }

        public void Dispose()
        {
            _temp?.Release();
        }
    }
}
