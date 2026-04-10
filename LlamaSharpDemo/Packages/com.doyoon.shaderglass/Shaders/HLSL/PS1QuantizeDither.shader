Shader "Hidden/ShaderGlass/PS1/QuantizeDither"
{
    Properties
    {
        [HideInInspector]_MainTex ("Texture", 2D) = "white" {}

        ColorSteps ("Color Steps", Range(4.0, 64.0)) = 32.0
        DitherStrength ("Dither Strength", Range(0.0, 2.0)) = 1.0
        ChromaShift ("Chroma Shift", Range(0.0, 2.0)) = 0.6
        ScanlineStrength ("Scanline Strength", Range(0.0, 1.0)) = 0.16
        NoiseStrength ("Noise Strength", Range(0.0, 0.2)) = 0.03
        JitterStrength ("Jitter Strength", Range(0.0, 2.0)) = 0.4

        Saturation ("Saturation", Range(0.0, 2.0)) = 0.9
        Contrast ("Contrast", Range(0.5, 1.5)) = 0.96
        Brightness ("Brightness", Range(-0.5, 0.5)) = 0.02
        TimestampEnable ("Timestamp Enable", Range(0.0, 1.0)) = 1.0
        TimestampOpacity ("Timestamp Opacity", Range(0.0, 1.0)) = 0.72
        TimestampScale ("Timestamp Scale", Range(0.5, 3.0)) = 1.0
        TimestampPosX ("Timestamp Pos X", Range(0.0, 1.0)) = 0.04
        TimestampPosY ("Timestamp Pos Y", Range(0.0, 1.0)) = 0.06
        TimestampJitter ("Timestamp Jitter", Range(0.0, 0.5)) = 0.08
        TimestampYear ("Timestamp Year", Range(0.0, 99.0)) = 98.0
        TimestampMonth ("Timestamp Month", Range(1.0, 12.0)) = 9.0
        TimestampDay ("Timestamp Day", Range(1.0, 31.0)) = 9.0
        TimestampHour ("Timestamp Hour", Range(0.0, 23.0)) = 23.0
        TimestampMinute ("Timestamp Minute", Range(0.0, 59.0)) = 17.0
        TimestampSecond ("Timestamp Second", Range(0.0, 59.0)) = 45.0
        TimestampColor ("Timestamp Color", Color) = (1.0, 0.62, 0.35, 1.0)

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
            Name "PS1QuantizeDither"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(Source);
            SAMPLER(sampler_Source);

            CBUFFER_START(UnityPerMaterial)
                float4 SourceSize;
                float4 OriginalSize;
                float4 OutputSize;
                float FrameCount;

                float ColorSteps;
                float DitherStrength;
                float ChromaShift;
                float ScanlineStrength;
                float NoiseStrength;
                float JitterStrength;
                float Saturation;
                float Contrast;
                float Brightness;
                float TimestampEnable;
                float TimestampOpacity;
                float TimestampScale;
                float TimestampPosX;
                float TimestampPosY;
                float TimestampJitter;
                float TimestampYear;
                float TimestampMonth;
                float TimestampDay;
                float TimestampHour;
                float TimestampMinute;
                float TimestampSecond;
                float4 TimestampColor;
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

            float Random01(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            float Bayer4x4(int2 p)
            {
                int x = p.x & 3;
                int y = p.y & 3;

                if (y == 0)
                {
                    if (x == 0) return 0.0;
                    if (x == 1) return 8.0;
                    if (x == 2) return 2.0;
                    return 10.0;
                }

                if (y == 1)
                {
                    if (x == 0) return 12.0;
                    if (x == 1) return 4.0;
                    if (x == 2) return 14.0;
                    return 6.0;
                }

                if (y == 2)
                {
                    if (x == 0) return 3.0;
                    if (x == 1) return 11.0;
                    if (x == 2) return 1.0;
                    return 9.0;
                }

                if (x == 0) return 15.0;
                if (x == 1) return 7.0;
                if (x == 2) return 13.0;
                return 5.0;
            }

            float3 ApplyColorTone(float3 color)
            {
                float luma = dot(color, float3(0.299, 0.587, 0.114));
                color = lerp(luma.xxx, color, Saturation);
                color = (color - 0.5) * Contrast + 0.5;
                color += Brightness;
                return saturate(color);
            }

            float RectMask(float2 uv, float2 minV, float2 maxV)
            {
                float2 insideMin = step(minV, uv);
                float2 insideMax = step(uv, maxV);
                return insideMin.x * insideMin.y * insideMax.x * insideMax.y;
            }

            int GetDigitSegments(int digit)
            {
                if (digit == 0) return 0x3F;
                if (digit == 1) return 0x06;
                if (digit == 2) return 0x5B;
                if (digit == 3) return 0x4F;
                if (digit == 4) return 0x66;
                if (digit == 5) return 0x6D;
                if (digit == 6) return 0x7D;
                if (digit == 7) return 0x07;
                if (digit == 8) return 0x7F;
                if (digit == 9) return 0x6F;
                return 0;
            }

            float RenderDigit7Seg(float2 uv, int digit)
            {
                int mask = GetDigitSegments(digit);
                float m = 0.0;

                if ((mask & 1) != 0) m = max(m, RectMask(uv, float2(0.16, 0.86), float2(0.84, 0.98))); // a
                if ((mask & 2) != 0) m = max(m, RectMask(uv, float2(0.86, 0.52), float2(0.98, 0.96))); // b
                if ((mask & 4) != 0) m = max(m, RectMask(uv, float2(0.86, 0.04), float2(0.98, 0.48))); // c
                if ((mask & 8) != 0) m = max(m, RectMask(uv, float2(0.16, 0.02), float2(0.84, 0.14))); // d
                if ((mask & 16) != 0) m = max(m, RectMask(uv, float2(0.02, 0.04), float2(0.14, 0.48))); // e
                if ((mask & 32) != 0) m = max(m, RectMask(uv, float2(0.02, 0.52), float2(0.14, 0.96))); // f
                if ((mask & 64) != 0) m = max(m, RectMask(uv, float2(0.16, 0.44), float2(0.84, 0.56))); // g

                return m;
            }

            float RenderSlash(float2 uv)
            {
                float lineDist = abs(uv.x - (0.22 + 0.56 * uv.y));
                float slash = 1.0 - step(0.085, lineDist);
                return slash * RectMask(uv, float2(0.08, 0.08), float2(0.92, 0.92));
            }

            float RenderColon(float2 uv)
            {
                float top = RectMask(uv, float2(0.40, 0.62), float2(0.60, 0.78));
                float bottom = RectMask(uv, float2(0.40, 0.22), float2(0.60, 0.38));
                return max(top, bottom);
            }

            float DrawTimestamp(
                float2 uv,
                float2 outSize,
                int year,
                int month,
                int day,
                int hour,
                int minute,
                int second)
            {
                float2 screenPx = uv * outSize;
                float charH = 8.0 * TimestampScale;
                float charW = 5.0 * TimestampScale;
                float charSpacing = 1.5 * TimestampScale;
                float2 startPx = float2(TimestampPosX * outSize.x, TimestampPosY * outSize.y);

                float stamp = 0.0;
                [unroll]
                for (int i = 0; i < 17; i++)
                {
                    int glyphType = 0; // 0=digit, 1=slash, 2=colon, 3=space
                    int digit = 0;

                    if (i == 0) digit = year / 10;
                    else if (i == 1) digit = year % 10;
                    else if (i == 2) glyphType = 1;
                    else if (i == 3) digit = month / 10;
                    else if (i == 4) digit = month % 10;
                    else if (i == 5) glyphType = 1;
                    else if (i == 6) digit = day / 10;
                    else if (i == 7) digit = day % 10;
                    else if (i == 8) glyphType = 3;
                    else if (i == 9) digit = hour / 10;
                    else if (i == 10) digit = hour % 10;
                    else if (i == 11) glyphType = 2;
                    else if (i == 12) digit = minute / 10;
                    else if (i == 13) digit = minute % 10;
                    else if (i == 14) glyphType = 2;
                    else if (i == 15) digit = second / 10;
                    else digit = second % 10;

                    float2 charOffset = float2(startPx.x + i * (charW + charSpacing), startPx.y);
                    float2 charUv = (screenPx - charOffset) / float2(charW, charH);
                    if (charUv.x < 0.0 || charUv.x > 1.0 || charUv.y < 0.0 || charUv.y > 1.0)
                        continue;

                    float2 glyphUv = float2(charUv.x, 1.0 - charUv.y);
                    float glyphMask = 0.0;
                    if (glyphType == 0) glyphMask = RenderDigit7Seg(glyphUv, digit);
                    else if (glyphType == 1) glyphMask = RenderSlash(glyphUv);
                    else if (glyphType == 2) glyphMask = RenderColon(glyphUv);

                    stamp = max(stamp, glyphMask);
                }

                return stamp;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 outSize = max(OutputSize.xy, float2(1.0, 1.0));
                float2 pixelF = floor(input.uv * outSize);
                int2 pixelI = int2(pixelF);

                float2 jitterCell = floor(pixelF / 8.0);
                float2 jitterRand = float2(
                    Random01(jitterCell + FrameCount * float2(0.013, 0.037)),
                    Random01(jitterCell + FrameCount * float2(0.071, 0.019))
                );
                float2 jitterOffset = (jitterRand - 0.5) * (JitterStrength / outSize);

                float2 uv = input.uv + jitterOffset;
                float2 chromaOffset = float2(ChromaShift / outSize.x, 0.0);

                float r = SAMPLE_TEXTURE2D(Source, sampler_Source, uv + chromaOffset).r;
                float g = SAMPLE_TEXTURE2D(Source, sampler_Source, uv).g;
                float b = SAMPLE_TEXTURE2D(Source, sampler_Source, uv - chromaOffset).b;
                float3 color = float3(r, g, b);

                float scanSin = sin((pixelF.y + FrameCount * 0.15) * 3.14159265);
                float scanMul = lerp(1.0, 0.92 + 0.08 * scanSin, saturate(ScanlineStrength));
                color *= scanMul;

                float noise = (Random01(pixelF + FrameCount * float2(0.11, 0.37)) - 0.5) * NoiseStrength;
                color += noise.xxx;

                float levels = max(2.0, ColorSteps);
                float bayer = (Bayer4x4(pixelI) - 7.5) / 16.0;
                color = saturate(color + (bayer * DitherStrength / levels).xxx);
                color = floor(color * (levels - 1.0) + 0.5) / (levels - 1.0);

                color = ApplyColorTone(color);

                if (TimestampEnable > 0.001)
                {
                    int baseYear = clamp((int)round(TimestampYear), 0, 99);
                    int baseMonth = clamp((int)round(TimestampMonth), 1, 12);
                    int baseDay = clamp((int)round(TimestampDay), 1, 31);
                    int baseHour = clamp((int)round(TimestampHour), 0, 23);
                    int baseMinute = clamp((int)round(TimestampMinute), 0, 59);
                    int baseSecond = clamp((int)round(TimestampSecond), 0, 59);

                    int elapsedSeconds = (int)floor(FrameCount / 60.0);
                    int totalSeconds = baseHour * 3600 + baseMinute * 60 + baseSecond + elapsedSeconds;
                    int dayCarry = totalSeconds / 86400;
                    int secondOfDay = totalSeconds - dayCarry * 86400;

                    int hour = secondOfDay / 3600;
                    int minute = (secondOfDay % 3600) / 60;
                    int second = secondOfDay % 60;

                    int dayIndex = (baseDay - 1) + dayCarry;
                    int monthCarry = dayIndex / 31;
                    int day = (dayIndex % 31) + 1;

                    int monthIndex = (baseMonth - 1) + monthCarry;
                    int yearCarry = monthIndex / 12;
                    int month = (monthIndex % 12) + 1;
                    int year = (baseYear + yearCarry) % 100;

                    float stampMask = DrawTimestamp(input.uv, outSize, year, month, day, hour, minute, second);
                    float stampNoise = (Random01(pixelF * 0.5 + FrameCount * float2(0.17, 0.23)) - 0.5) * TimestampJitter;
                    float stampFlicker = 0.94 + 0.06 * sin(FrameCount * 0.09);
                    stampMask = saturate(stampMask * stampFlicker + stampNoise * stampMask);

                    float stampAlpha = stampMask * TimestampOpacity * TimestampEnable;
                    color = lerp(color, TimestampColor.rgb, stampAlpha);
                }

                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
