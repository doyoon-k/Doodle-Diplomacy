Shader "Hidden/ShaderGlass/Newpixie/CRT"
{
    Properties
    {
        [HideInInspector]_MainTex ("Texture", 2D) = "white" {}
        use_frame ("Use Frame Image", Range(0.0, 1.0)) = 0.0
        curvature ("Curvature", Range(0.0001, 4.0)) = 2.0
        wiggle_toggle ("Interference", Range(0.0, 5.0)) = 0.0
        interference_speed ("Interference Speed", Range(0.0, 5.0)) = 1.0
        scanroll ("Rolling Scanlines", Range(0.0, 1.0)) = 1.0
        scanroll_speed ("Scanline Roll Speed", Range(0.0, 5.0)) = 1.0
        vignette ("Vignette", Range(0.0, 1.0)) = 0.5
        ghosting ("Ghosting", Range(0.0, 2.0)) = 1.0
        [HideInInspector]SourceSize ("SourceSize", Vector) = (1, 1, 1, 1)
        [HideInInspector]OutputSize ("OutputSize", Vector) = (1, 1, 1, 1)
        [HideInInspector]FrameCount ("FrameCount", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "CRT"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(Source);
            SAMPLER(sampler_Source);
            TEXTURE2D(accum1);
            SAMPLER(sampler_accum1);
            TEXTURE2D(blur2);
            SAMPLER(sampler_blur2);
            TEXTURE2D(frametexture);
            SAMPLER(sampler_frametexture);

            CBUFFER_START(UnityPerMaterial)
                float4 SourceSize;
                float4 OutputSize;
                float FrameCount;
                float use_frame;
                float curvature;
                float wiggle_toggle;
                float interference_speed;
                float scanroll;
                float scanroll_speed;
                float vignette;
                float ghosting;
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
                output.uv = float2(input.uv.x, 1.0 - input.uv.y);
                return output;
            }

            float mod(float x, float y)
            {
                return x - y * floor(x / y);
            }

            float2 curve(float2 uv)
            {
                uv = uv - 0.5;
                uv *= float2(0.925, 1.095);
                uv *= curvature;
                uv.x *= 1.0 + pow(abs(uv.y) / 4.0, 2.0);
                uv.y *= 1.0 + pow(abs(uv.x) / 3.0, 2.0);
                uv /= curvature;
                uv += 0.5;
                uv = uv * 0.92 + 0.04;
                return uv;
            }

            float3 tsample(TEXTURE2D_PARAM(samp, sampler_samp), float2 tc, float offs, float2 resolution)
            {
                tc = tc * float2(1.025, 0.92) + float2(-0.0125, 0.04);
                float3 s = pow(abs(SAMPLE_TEXTURE2D(samp, sampler_samp, float2(tc.x, 1.0 - tc.y)).rgb), float3(2.2, 2.2, 2.2));
                return s * float3(1.25, 1.25, 1.25);
            }

            float3 filmic(float3 LinearColor)
            {
                float3 x = max(float3(0.0, 0.0, 0.0), LinearColor - float3(0.004, 0.004, 0.004));
                return (x * (6.2 * x + 0.5)) / (x * (6.2 * x + 1.7) + 0.06);
            }

            float rand(float2 co)
            {
                return frac(sin(dot(co, float2(12.9898, 78.233))) * 43758.5453);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float time = mod(FrameCount, 849.0) * 36.0;
                float2 vTexCoord = input.uv;
                float2 uv = vTexCoord;

                float2 curved_uv = lerp(curve(uv), uv, 0.4);
                float scale = -0.101;
                float2 scuv = curved_uv * (1.0 - scale) + (scale / 2.0) + float2(0.003, -0.001);
                uv = scuv;

                float2 resolution = OutputSize.xy;
                float2 fragCoord = vTexCoord * resolution;

                float3 col;
                float itime = time * interference_speed;
                float x = wiggle_toggle * sin(0.1 * itime + curved_uv.y * 13.0)
                        * sin(0.23 * itime + curved_uv.y * 19.0)
                        * sin(0.3 + 0.11 * itime + curved_uv.y * 23.0) * 0.0012;
                float o = sin(fragCoord.y * 1.5) / resolution.x;
                x += o * 0.25;

                time = mod(FrameCount, 640.0) * 1.0;

                float3 sampleR = tsample(TEXTURE2D_ARGS(accum1, sampler_accum1),
                    float2(x + scuv.x + 0.0009, scuv.y + 0.0009),
                    resolution.y / 800.0, resolution);
                col.r = sampleR.x + 0.02;

                float3 sampleG = tsample(TEXTURE2D_ARGS(accum1, sampler_accum1),
                    float2(x + scuv.x + 0.0000, scuv.y - 0.0011),
                    resolution.y / 800.0, resolution);
                col.g = sampleG.y + 0.02;

                float3 sampleB = tsample(TEXTURE2D_ARGS(accum1, sampler_accum1),
                    float2(x + scuv.x - 0.0015, scuv.y + 0.0000),
                    resolution.y / 800.0, resolution);
                col.b = sampleB.z + 0.02;

                float i = clamp(col.r * 0.299 + col.g * 0.587 + col.b * 0.114, 0.0, 1.0);
                i = pow(1.0 - pow(i, 2.0), 1.0);
                i = (1.0 - i) * 0.85 + 0.15;

                float ghs = 0.15 * ghosting;

                float3 r = tsample(TEXTURE2D_ARGS(blur2, sampler_blur2),
                    (float2(x - 0.014, -0.027) * 0.85) +
                    (0.007 * float2(0.35 * sin(1.0 / 7.0 + 15.0 * curved_uv.y + 0.9 * time),
                                    0.35 * sin(2.0 / 7.0 + 10.0 * curved_uv.y + 1.37 * time))) +
                    float2(scuv.x + 0.001, scuv.y + 0.001),
                    5.5 + 1.3 * sin(3.0 / 9.0 + 31.0 * curved_uv.x + 1.70 * time),
                    resolution) * float3(0.5, 0.25, 0.25);

                float3 g = tsample(TEXTURE2D_ARGS(blur2, sampler_blur2),
                    (float2(x - 0.019, -0.020) * 0.85) +
                    (0.007 * float2(0.35 * cos(1.0 / 9.0 + 15.0 * curved_uv.y + 0.5 * time),
                                    0.35 * sin(2.0 / 9.0 + 10.0 * curved_uv.y + 1.50 * time))) +
                    float2(scuv.x + 0.000, scuv.y - 0.002),
                    5.4 + 1.3 * sin(3.0 / 3.0 + 71.0 * curved_uv.x + 1.90 * time),
                    resolution) * float3(0.25, 0.5, 0.25);

                float3 b = tsample(TEXTURE2D_ARGS(blur2, sampler_blur2),
                    (float2(x - 0.017, -0.003) * 0.85) +
                    (0.007 * float2(0.35 * sin(2.0 / 3.0 + 15.0 * curved_uv.y + 0.7 * time),
                                    0.35 * cos(2.0 / 3.0 + 10.0 * curved_uv.y + 1.63 * time))) +
                    float2(scuv.x - 0.002, scuv.y + 0.000),
                    5.3 + 1.3 * sin(3.0 / 7.0 + 91.0 * curved_uv.x + 1.65 * time),
                    resolution) * float3(0.25, 0.25, 0.5);

                col += (ghs * (1.0 - 0.299)) * pow(clamp(3.0 * r, 0.0, 1.0), 2.0) * i;
                col += (ghs * (1.0 - 0.587)) * pow(clamp(3.0 * g, 0.0, 1.0), 2.0) * i;
                col += (ghs * (1.0 - 0.114)) * pow(clamp(3.0 * b, 0.0, 1.0), 2.0) * i;

                col *= float3(0.95, 1.05, 0.95);
                col = clamp(col * 1.3 + 0.75 * col * col + 1.25 * col * col * col * col * col, 0.0, 10.0);

                float vig = (1.0 - 0.99 * vignette) + 16.0 * curved_uv.x * curved_uv.y * (1.0 - curved_uv.x) * (1.0 - curved_uv.y);
                vig = 1.3 * pow(vig, 0.5);
                col *= vig;

                time *= (scanroll * scanroll_speed);

                float scans = clamp(0.35 + 0.18 * sin(6.0 * time - vTexCoord.y * resolution.y * 1.5), 0.0, 1.0);
                float s = pow(scans, 0.9);
                col *= s;

                col *= 1.0 - 0.23 * clamp(mod(fragCoord.x, 3.0) / 2.0, 0.0, 1.0);

                col = filmic(col);

                float2 seed = curved_uv * resolution;
                col -= 0.015 * pow(float3(rand(seed + time), rand(seed + time * 2.0), rand(seed + time * 3.0)), 1.5);

                col *= 1.0 - 0.004 * (sin(50.0 * time + curved_uv.y * 2.0) * 0.5 + 0.5);

                uv = curved_uv;
                uv = float2(uv.x, 1.0 - uv.y);
                float4 f = SAMPLE_TEXTURE2D(frametexture, sampler_frametexture, vTexCoord);
                f.xyz = lerp(f.xyz, float3(0.5, 0.5, 0.5), 0.5);
                float fvig = clamp(512.0 * uv.x * uv.y * (1.0 - uv.x) * (1.0 - uv.y), 0.2, 0.8);
                col = lerp(col, lerp(max(col, 0.0), pow(abs(f.xyz), 1.4) * fvig, f.w * f.w), use_frame);

                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
