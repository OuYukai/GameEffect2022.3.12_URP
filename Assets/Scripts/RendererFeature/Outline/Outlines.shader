Shader "Hidden/Outlines"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Outline Color", Color) = (1,0,0,1)
        _Scale ("_Scale", Range(0, 10)) = 1
        _DepthThreshold ("_DepthThreshold", Range(0, 10)) = 1
        _DepthNormalThresholdScale ("_DepthNormalThresholdScale", Range(0, 10)) = 1
        _NormalThreshold ("_NormalThreshold", Range(0, 10)) = 1
        
        
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 viewSpaceDir : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_SceneViewSpaceNormals);
            SAMPLER(sampler_SceneViewSpaceNormals);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                float _Scale;
                float4 _Color;
                float _DepthThreshold;
                float _DepthNormalThreshold;
                float _DepthNormalThresholdScale;
                float _NormalThreshold;
                float4x4 _ClipToView;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.viewSpaceDir = mul(_ClipToView, output.positionCS).xyz;
                return output;
            }

            float4 alphaBlend(float4 top, float4 bottom)
            {
                float3 color = (top.rgb * top.a) + (bottom.rgb * (1 - top.a));
                float alpha = top.a + bottom.a * (1 - top.a);
                return float4(color, alpha);
            }

            float4 frag(Varyings input) : SV_Target
            {
                float halfScaleFloor = floor(_Scale * 0.5);
                float halfScaleCeil = ceil(_Scale * 0.5);

                float2 bottomLeftUV = input.uv - float2(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * halfScaleFloor;
                float2 topRightUV = input.uv + float2(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * halfScaleCeil;  
                float2 bottomRightUV = input.uv + float2(_MainTex_TexelSize.x * halfScaleCeil, -_MainTex_TexelSize.y * halfScaleFloor);
                float2 topLeftUV = input.uv + float2(-_MainTex_TexelSize.x * halfScaleFloor, _MainTex_TexelSize.y * halfScaleCeil);

                float3 normal0 = SampleSceneNormals(bottomLeftUV);
                //float3 normal0 = SAMPLE_TEXTURE2D_X(_SceneViewSpaceNormals, sampler_SceneViewSpaceNormals, UnityStereoTransformScreenSpaceTex(bottomLeftUV)).xyz;
                
                float3 normal1 = SampleSceneNormals(topRightUV);
                //float3 normal1 = SAMPLE_TEXTURE2D_X(_SceneViewSpaceNormals, sampler_SceneViewSpaceNormals, UnityStereoTransformScreenSpaceTex(topRightUV)).xyz;
                
                float3 normal2 = SampleSceneNormals(bottomRightUV);
                //float3 normal2 = SAMPLE_TEXTURE2D_X(_SceneViewSpaceNormals, sampler_SceneViewSpaceNormals, UnityStereoTransformScreenSpaceTex(bottomRightUV)).xyz;
                
                float3 normal3 = SampleSceneNormals(topLeftUV);
                //float3 normal3 = SAMPLE_TEXTURE2D_X(_SceneViewSpaceNormals, sampler_SceneViewSpaceNormals, UnityStereoTransformScreenSpaceTex(topLeftUV)).xyz;
                

                float depth0 = SampleSceneDepth(bottomLeftUV);
                float depth1 = SampleSceneDepth(topRightUV);
                float depth2 = SampleSceneDepth(bottomRightUV);
                float depth3 = SampleSceneDepth(topLeftUV);

                float3 viewNormal = normal0 * 2 - 1;
                float NdotV = 1 - dot(viewNormal, -input.viewSpaceDir);

                float normalThreshold01 = saturate((NdotV - _DepthNormalThreshold) / (1 - _DepthNormalThreshold));
                float normalThreshold = normalThreshold01 * _DepthNormalThresholdScale + 1;

                float depthThreshold = _DepthThreshold * depth0 * normalThreshold;

                float depthFiniteDifference0 = depth1 - depth0;
                float depthFiniteDifference1 = depth3 - depth2;
                float edgeDepth = sqrt(pow(depthFiniteDifference0, 2) + pow(depthFiniteDifference1, 2)) * 100;
                edgeDepth = edgeDepth > depthThreshold ? 1 : 0;

                float3 normalFiniteDifference0 = normal1 - normal0;
                float3 normalFiniteDifference1 = normal3 - normal2;
                float edgeNormal = sqrt(dot(normalFiniteDifference0, normalFiniteDifference0) + dot(normalFiniteDifference1, normalFiniteDifference1));
                edgeNormal = edgeNormal > _NormalThreshold ? 1 : 0;

                float edge = max(edgeDepth, edgeNormal);

                float4 edgeColor = float4(_Color.rgb, _Color.a * edge);
                float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                return alphaBlend(edgeColor, color);
            }
            ENDHLSL
        }
    }
}