Shader "Custom/TabletSoftGlow"
{
    Properties
    {
        [HDR]_GlowColor("Glow Color", Color) = (0.65, 0.95, 1.0, 1.0)
        _Intensity("Intensity", Range(0, 3)) = 0.35
        _Falloff("Falloff", Range(0.5, 4)) = 1.8
        _EdgeBoost("Edge Boost", Range(0, 2)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalRenderPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half _Intensity;
                half _Falloff;
                half _EdgeBoost;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = IN.uv * 2.0 - 1.0;
                half radial = saturate(1.0 - length(p));
                half center = pow(radial, max((half)0.001, _Falloff));

                half edgeX = saturate(1.0 - abs(p.x));
                half edgeY = saturate(1.0 - abs(p.y));
                half edge = pow(edgeX * edgeY, 0.5);

                half glow = saturate(center + edge * _EdgeBoost);
                half alpha = glow * _Intensity;

                return half4(_GlowColor.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}

