using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ScreenSpaceOutlines : ScriptableRendererFeature
{
    [System.Serializable]
    private class ScreenSpaceOutlineSettings
    {

        [Header("Layer Control")] 
        public LayerMask outlineLayer = -1;
        
        [Header("General Outline Settings")]
        public Color outlineColor = Color.black;
        [Range(0.0f, 20.0f)]
        public float outlineScale = 1.0f;
        
        [Header("Depth Settings")]
        [Range(0.0f, 100.0f)]
        public float depthThreshold = 1.5f;
        [Range(0.0f, 500.0f)]
        public float robertsCrossMultiplier = 100.0f;

        [Header("Normal Settings")]
        [Range(0.0f, 1.0f)]
        public float normalThreshold = 0.4f;

        [Header("Depth Normal Relation Settings")]
        [Range(0.0f, 2.0f)]
        public float steepAngleThreshold = 0.2f;
        [Range(0.0f, 500.0f)]
        public float steepAngleMultiplier = 25.0f;
        
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
        private readonly Material normalsMaterial;

        private RTHandle normals;
        private RTHandle temporaryBuffer;
        
        private RendererList normalsRenderersList;
        
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
            
            filteringSettings = new FilteringSettings(RenderQueueRange.opaque, settings.outlineLayer);

            shaderTagIdList = new List<ShaderTagId> {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("LightweightForward"),
                new ShaderTagId("SRPDefaultUnlit")
            };

            normalsMaterial = new Material(Shader.Find("Hidden/ViewSpaceNormals"));
            
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor textureDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            textureDescriptor.colorFormat = settings.colorFormat;
            textureDescriptor.depthBufferBits = settings.depthBufferBits;

            // 使用 RTHandles.Alloc 來分配 RTHandle
            RenderingUtils.ReAllocateIfNeeded(ref normals, textureDescriptor, name: "NormalsTexture");
            
            textureDescriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref temporaryBuffer, textureDescriptor, name: "TemporaryBuffer");
            
            Camera camera = renderingData.cameraData.camera;
            Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
            clipToView = projMatrix.inverse;

            ConfigureTarget(normals);
            ConfigureClear(ClearFlag.Color, settings.backgroundColor);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!screenSpaceOutlineMaterial || !normalsMaterial || 
                renderingData.cameraData.renderer.cameraColorTargetHandle == null || temporaryBuffer == null)
                return;
            
            CommandBuffer cmd = CommandBufferPool.Get();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
                
            // Normals
            DrawingSettings drawSettings = CreateDrawingSettings(shaderTagIdList, ref renderingData, renderingData.cameraData.defaultOpaqueSortFlags);
            drawSettings.perObjectData = settings.perObjectData;
            drawSettings.enableDynamicBatching = settings.enableDynamicBatching;
            drawSettings.enableInstancing = settings.enableInstancing;
            drawSettings.overrideMaterial = normalsMaterial;
            
            RendererListParams normalsRenderersParams = new RendererListParams(renderingData.cullResults, drawSettings, filteringSettings);
            normalsRenderersList = context.CreateRendererList(ref normalsRenderersParams);
            cmd.DrawRendererList(normalsRenderersList);

            // 僅為受影響的層渲染 outline
            FilteringSettings outlineFilteringSettings = new FilteringSettings(RenderQueueRange.opaque, settings.outlineLayer);
            DrawingSettings outlineDrawSettings = CreateDrawingSettings(shaderTagIdList, ref renderingData, renderingData.cameraData.defaultOpaqueSortFlags);
    
            RendererListParams outlineRenderersParams = new RendererListParams(renderingData.cullResults, outlineDrawSettings, outlineFilteringSettings);
            RendererList outlineRenderersList = context.CreateRendererList(ref outlineRenderersParams);
            
            // 修改 SetGlobalTexture 的調用
            cmd.SetGlobalTexture(Shader.PropertyToID("_SceneViewSpaceNormals"), normals);
            
            cmd.SetGlobalMatrix("_ClipToView", clipToView);
            
            using (new ProfilingScope(cmd, new ProfilingSampler("ScreenSpaceOutlines")))
            {
                var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
                
                cmd.Blit(source, temporaryBuffer, screenSpaceOutlineMaterial, 0);
                cmd.DrawRendererList(outlineRenderersList);
                cmd.Blit(temporaryBuffer, source);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Release()
        {
            CoreUtils.Destroy(screenSpaceOutlineMaterial);
            CoreUtils.Destroy(normalsMaterial);
            normals?.Release();
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