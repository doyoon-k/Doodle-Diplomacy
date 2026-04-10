Shader "Hidden/ShaderGlass/Newpixie/Accumulate"
{
    Properties
    {
        [HideInInspector]_MainTex ("Texture", 2D) = "white" {}
        acc_modulate ("Accumulate Modulation", Range(0.0, 1.0)) = 0.65
        [HideInInspector]SourceSize ("SourceSize", Vector) = (1, 1, 1, 1)
        [HideInInspector]OriginalSize ("OriginalSize", Vector) = (1, 1, 1, 1)
        [HideInInspector]OutputSize ("OutputSize", Vector) = (1, 1, 1, 1)
        [HideInInspector]FrameCount ("FrameCount", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Accumulate"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(Source);
            SAMPLER(sampler_Source);
            TEXTURE2D(PassFeedback1);
            SAMPLER(sampler_PassFeedback1);

            CBUFFER_START(UnityPerMaterial)
                float4 SourceSize;
                float4 OriginalSize;
                float4 OutputSize;
                float FrameCount;
                float acc_modulate;
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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 a = SAMPLE_TEXTURE2D(PassFeedback1, sampler_PassFeedback1, input.uv) * acc_modulate;
                float4 b = SAMPLE_TEXTURE2D(Source, sampler_Source, input.uv);
                return max(a, b * 0.96);
            }
            ENDHLSL
        }
    }
}
