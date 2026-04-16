Shader "DoodleDiplomacy/SharedMonitorCRT"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _ScanlineDensity ("Scanline Density", Range(100, 1200)) = 420
        _ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.22
        _NoiseStrength ("Noise Strength", Range(0, 0.3)) = 0.05
        _VignetteStrength ("Vignette Strength", Range(0, 1)) = 0.28
        _Curvature ("Curvature", Range(0, 0.2)) = 0.08
        _ChromaticAberration ("Chromatic Aberration", Range(0, 0.01)) = 0.0018
        _PixelResolution ("Pixel Resolution (XY)", Vector) = (320, 180, 0, 0)
        _PixelateStrength ("Pixelate Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardUnlit"
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _ScanlineDensity;
                float _ScanlineStrength;
                float _NoiseStrength;
                float _VignetteStrength;
                float _Curvature;
                float _ChromaticAberration;
                float4 _PixelResolution;
                float _PixelateStrength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float2 CurveUV(float2 uv, float curvature)
            {
                float2 centered = uv * 2.0 - 1.0;

                float aspect = _BaseMap_TexelSize.w / max(_BaseMap_TexelSize.z, 1.0);
                centered.x *= aspect;

                float radius2 = dot(centered, centered);
                centered *= 1.0 + curvature * radius2;

                centered.x /= aspect;
                return centered * 0.5 + 0.5;
            }

            float3 SampleCrtColor(float2 uv, float chroma)
            {
                float r = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv + float2(chroma, 0)).r;
                float g = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).g;
                float b = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv - float2(chroma, 0)).b;
                return float3(r, g, b);
            }

            float2 PixelateUV(float2 uv)
            {
                float2 resolution = max(_PixelResolution.xy, 1.0.xx);
                float2 quantized = (floor(uv * resolution) + 0.5.xx) / resolution;
                return lerp(uv, quantized, saturate(_PixelateStrength));
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = CurveUV(input.uv, _Curvature);

                if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
                    return float4(0, 0, 0, 1);

                float2 pixelUv = PixelateUV(uv);
                float3 color = SampleCrtColor(pixelUv, _ChromaticAberration);

                float scanlineValue = 0.5 + 0.5 * sin(uv.y * _ScanlineDensity + _Time.y * 30.0);
                color *= lerp(1.0, scanlineValue, _ScanlineStrength);

                float noise = Hash12(pixelUv * 1024.0 + _Time.yy) - 0.5;
                color += noise * _NoiseStrength;

                float2 v = uv * (1.0 - uv.yx);
                float vignette = saturate(pow(16.0 * v.x * v.y, 0.35));
                color *= lerp(1.0, vignette, _VignetteStrength);

                color *= _BaseColor.rgb;
                return float4(saturate(color), 1);
            }
            ENDHLSL
        }
    }
}
