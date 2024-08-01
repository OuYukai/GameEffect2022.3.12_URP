using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class OutlineRendererFeature : ScriptableRendererFeature
{
    [System.Serializable]
    private class ScreenSpaceOutlineSettings
    {
        [Header("General Outline Settings")]
        public Color outlineColor = Color.black;
        [Range(0.0f, 20.0f)]
        public float outlineScale = 1.0f;
        
        [Header("Depth Settings")]
        [Range(0.0f, 100.0f)]
        public float depthThreshold = 1.5f;

        [Header("Normal Settings")]
        [Range(0.0f, 1.0f)]
        public float normalThreshold = 0.4f;
        
        [Header("General Scene View Space Normal Texture Settings")]
        public RenderTextureFormat colorFormat;
        public int depthBufferBits;
        public FilterMode filterMode;
        public Color backgroundColor = Color.clear;

        [Header("View Space Normal Texture Object Draw Settings")]
        public PerObjectData perObjectData;
        public bool enableDynamicBatching;
        public bool enableInstancing;

    }

    private class ScreenSpaceOutlinePass : ScriptableRenderPass
    {
        private readonly Material screenSpaceOutlineMaterial;
        private ScreenSpaceOutlineSettings settings;

        private FilteringSettings filteringSettings;

        private readonly List<ShaderTagId> shaderTagIdList;
        
        private RTHandle temporaryBuffer;
        
        private Matrix4x4 clipToView;

        public ScreenSpaceOutlinePass(RenderPassEvent renderPassEvent, LayerMask layerMask,
            ScreenSpaceOutlineSettings settings) {
            this.settings = settings;
            this.renderPassEvent = renderPassEvent;
            
            screenSpaceOutlineMaterial = new Material(Shader.Find("Hidden/Outlines"));
            screenSpaceOutlineMaterial.SetColor("_Color", settings.outlineColor);
            screenSpaceOutlineMaterial.SetFloat("_Scale", settings.outlineScale);
            screenSpaceOutlineMaterial.SetFloat("_DepthThreshold", settings.depthThreshold);
            screenSpaceOutlineMaterial.SetFloat("_NormalThreshold", settings.normalThreshold);
            
            filteringSettings = new FilteringSettings(RenderQueueRange.opaque, layerMask);

            shaderTagIdList = new List<ShaderTagId> {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("LightweightForward"),
                new ShaderTagId("SRPDefaultUnlit")
            };
            
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor textureDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            textureDescriptor.colorFormat = settings.colorFormat;
            textureDescriptor.depthBufferBits = settings.depthBufferBits;
            
            textureDescriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref temporaryBuffer, textureDescriptor, name: "TemporaryBuffer");
            
            Camera camera = renderingData.cameraData.camera;
            Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            clipToView = projMatrix.inverse;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!screenSpaceOutlineMaterial || renderingData.cameraData.renderer.cameraColorTargetHandle == null || temporaryBuffer == null)
                return;
            
            CommandBuffer cmd = CommandBufferPool.Get();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            cmd.SetGlobalMatrix("_ClipToView", clipToView);
            
            using (new ProfilingScope(cmd, new ProfilingSampler("ScreenSpaceOutlines")))
            {
                var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
                
                cmd.Blit(source, temporaryBuffer, screenSpaceOutlineMaterial, 0);
                cmd.Blit(temporaryBuffer, source);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Release()
        {
            CoreUtils.Destroy(screenSpaceOutlineMaterial);
            temporaryBuffer?.Release();
        }
    }

    [SerializeField] private RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingSkybox;
    [SerializeField] private LayerMask outlinesLayerMask;
    
    [SerializeField] private ScreenSpaceOutlineSettings outlineSettings = new ScreenSpaceOutlineSettings();

    private ScreenSpaceOutlinePass screenSpaceOutlinePass;
    
    public override void Create() {
        if (renderPassEvent < RenderPassEvent.BeforeRenderingPrePasses)
            renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;

        screenSpaceOutlinePass = new ScreenSpaceOutlinePass(renderPassEvent, outlinesLayerMask, outlineSettings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        renderer.EnqueuePass(screenSpaceOutlinePass);
    }

    protected override void Dispose(bool disposing){
        if (disposing)
        {
            screenSpaceOutlinePass?.Release();
        }
    }
}