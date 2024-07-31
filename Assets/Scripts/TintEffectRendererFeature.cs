using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class TintEffectRendererFeature : ScriptableRendererFeature
{
    class TintEffectPass : ScriptableRenderPass
    {
        private Material _material;
        private Material _normalMaterial;
        private RTHandle temporaryColorTexture;

        public TintEffectPass(Material material, Material normalMaterial)
        {
            _material = material;
            _normalMaterial = normalMaterial;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var renderer = renderingData.cameraData.renderer;
            
            // Check for null or invalid handles
            if (renderer.cameraColorTargetHandle == null)
            {
                Debug.LogError("cameraColorTargetHandle is not valid.");
                return;
            }
            
            // Configure descriptor for temporary texture
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0; // No need for a depth buffer
            
            // Allocate temporary RTHandle
            RenderingUtils.ReAllocateIfNeeded(ref temporaryColorTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_TemporaryColorTexture");
        }

        public override void Execute(ScriptableRenderContext context,
            ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get(name: "TintEffectPass");
            
            cmd.Clear();
            
            if (renderingData.cameraData.camera.name == "Preview Scene Camera")
            {
                // 忽略 Preview Scene Camera
                CommandBufferPool.Release(cmd);
                return;
            }

            // Check if temporaryColorTexture and cameraColorTargetHandle are valid
            if (temporaryColorTexture == null || temporaryColorTexture.rt == null)
            {
                Debug.LogError("Temporary RTHandle is not valid.");
                CommandBufferPool.Release(cmd);
                return;
            }

            var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
    
            if (source == null || source.rt == null)
            {
                Debug.LogError("cameraColorTargetHandle is not valid. Renderer: " + renderingData.cameraData.renderer + " Camera: " + renderingData.cameraData.camera);
                CommandBufferPool.Release(cmd);
                return;
            }
    
            cmd.Blit(source, temporaryColorTexture, _material, 0);
            cmd.Blit(temporaryColorTexture, source);
    
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (temporaryColorTexture != null)
            {
                RTHandles.Release(temporaryColorTexture);
                temporaryColorTexture = null;
            }
        }
    }

    private TintEffectPass _tintEffect;
    public Material material;
    public Material normalMaterial;

    public override void Create()
    {
        _tintEffect = new TintEffectPass(material, normalMaterial);
        
        _tintEffect.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material != null)
        {
            renderer.EnqueuePass(_tintEffect);
        }
    }
}
