Shader "Custom/OutlineURP"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (1,0,0,1)
        _OutlineWidth ("Outline Width", Range(0, 10)) = 1
    }
    SubShader
    {
        Tags {"RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_CameraDepthTexture);
        SAMPLER(sampler_CameraDepthTexture);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            float4 _OutlineColor;
            float _OutlineWidth;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        Varyings vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = TRANSFORM_TEX(input.uv, _MainTex);

            //output.uv.y = 1 - output.uv.y;
            return output;
        }

        ENDHLSL

        Pass
        {
            Name "Outline"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                
                float2 offsets[8] = {
                    float2(-1, -1), float2(-1, 0), float2(-1, 1),
                    float2(0, -1),                 float2(0, 1),
                    float2(1, -1),  float2(1, 0),  float2(1, 1)
                };

                float depth = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, uv).r;
                
                for (int i = 0; i < 8; i++)
                {
                    float2 offset_uv = uv + offsets[i] * _MainTex_TexelSize.xy * _OutlineWidth;
                    float depth_sample = SAMPLE_TEXTURE2D(_CameraDepthTexture, sampler_CameraDepthTexture, offset_uv).r;
                    
                    if (abs(depth - depth_sample) > 0.0001)
                    {
                        return _OutlineColor;
                    }
                }

                return float4(1,1,1,1);
            }
            ENDHLSL
        }
    }
}