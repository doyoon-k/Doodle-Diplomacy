Shader "Hidden/ShaderGlass/Preprocess"
{
    Properties
    {
        [HideInInspector]_MainTex ("Texture", 2D) = "white" {}
        [HideInInspector]_ColorConversion ("Color Conversion", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Preprocess"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _UVScaleOffset;
            float _ColorConversion;

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
                float2 uv = input.uv * _UVScaleOffset.xy + _UVScaleOffset.zw;
                float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
            #if !defined(UNITY_COLORSPACE_GAMMA)
                if (_ColorConversion > 1.5)
                {
                    color.rgb = SRGBToLinear(color.rgb);
                }
                else if (_ColorConversion > 0.5)
                {
                    color.rgb = LinearToSRGB(color.rgb);
                }
            #endif
                return color;
            }
            ENDHLSL
        }
    }
}
