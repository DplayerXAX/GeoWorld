using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class OutlineRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public LayerMask objectLayer = 0;
        public Material outlineMaterial = null;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents; // 放在所有透明物体渲染完后，保证拿到完整画面
    }

    public Settings settings = new Settings();
    private OutlineRenderPass m_ScriptablePass;

    public override void Create()
    {
        m_ScriptablePass = new OutlineRenderPass(settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.outlineMaterial == null) return;
        renderer.EnqueuePass(m_ScriptablePass);
    }

    class OutlineRenderPass : ScriptableRenderPass
    {
        private Settings m_Settings;
        private FilteringSettings m_FilteringSettings;
        private RTHandle m_SilhouetteTexHandle;
        private ShaderTagId m_ShaderTagId = new ShaderTagId("UniversalForward");

        public OutlineRenderPass(Settings settings)
        {
            m_Settings = settings;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            m_FilteringSettings = new FilteringSettings(RenderQueueRange.all, m_Settings.objectLayer);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.colorFormat = RenderTextureFormat.R8;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref m_SilhouetteTexHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SilhouetteTex");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Settings.outlineMaterial == null) return;

            CommandBuffer cmd = CommandBufferPool.Get("Block Silhouette Pass");

            // 1. 绘制方块剪影到临时纹理
            CoreUtils.SetRenderTarget(cmd, m_SilhouetteTexHandle, ClearFlag.Color, Color.clear);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            DrawingSettings drawingSettings = CreateDrawingSettings(m_ShaderTagId, ref renderingData, SortingCriteria.CommonOpaque);
            drawingSettings.overrideMaterial = m_Settings.outlineMaterial;
            drawingSettings.overrideMaterialPassIndex = 0;
            context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref m_FilteringSettings);

            // 2. 核心修正：获取当前真实的渲染目标
            var currentRenderer = renderingData.cameraData.renderer;

            // 3. 强制把目标设为当前相机的色彩缓冲（不绑定深度缓冲），这样全屏大三角渲染时就完全没有深度检测的干扰！
            CoreUtils.SetRenderTarget(cmd, currentRenderer.cameraColorTargetHandle);

            cmd.SetGlobalTexture("_SilhouetteTex", m_SilhouetteTexHandle);

            // 4. 执行全屏幕大三角
            cmd.DrawProcedural(Matrix4x4.identity, m_Settings.outlineMaterial, 1, MeshTopology.Triangles, 3);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd) { }

        public void Dispose()
        {
            m_SilhouetteTexHandle?.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (m_ScriptablePass != null) m_ScriptablePass.Dispose();
    }
}