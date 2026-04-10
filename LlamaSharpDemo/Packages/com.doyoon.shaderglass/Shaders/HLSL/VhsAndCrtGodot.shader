Shader "Hidden/ShaderGlass/VHS/VhsAndCrtGodot"
{
    Properties
    {
        [HideInInspector]_MainTex ("Texture", 2D) = "white" {}

        godot_scanlines_opacity ("Scanlines Opacity", Range(0.0, 1.0)) = 0.4
        godot_scanlines_width ("Scanlines Width", Range(0.0, 0.5)) = 0.25
        godot_grille_opacity ("Grille Opacity", Range(0.0, 1.0)) = 0.3
        godot_pixelate ("Pixelate", Range(0.0, 1.0)) = 0.0

        godot_roll ("Roll Toggle", Range(0.0, 1.0)) = 1.0
        godot_roll_speed ("Roll Speed", Range(-20.0, 20.0)) = 8.0
        godot_roll_size ("Roll Size", Range(0.0, 100.0)) = 15.0
        godot_roll_variation ("Roll Variation", Range(0.1, 5.0)) = 1.8
        godot_distort_intensity ("Distortion Intensity", Range(0.0, 0.2)) = 0.05

        godot_noise_opacity ("Noise Opacity", Range(0.0, 1.0)) = 0.4
        godot_noise_speed ("Noise Speed", Range(0.0, 20.0)) = 5.0
        godot_static_noise_intensity ("Static Noise Intensity", Range(0.0, 1.0)) = 0.06

        godot_aberration ("Aberration", Range(-1.0, 1.0)) = 0.03
        godot_brightness ("Brightness", Range(0.0, 2.0)) = 1.4
        godot_discolor ("Discolor Toggle", Range(0.0, 1.0)) = 0.0

        godot_warp_amount ("Warp Amount", Range(0.0, 5.0)) = 1.0
        godot_clip_warp ("Clip Warp Toggle", Range(0.0, 1.0)) = 0.0

        godot_vignette_intensity ("Vignette Intensity", Range(0.0, 1.0)) = 0.4
        godot_vignette_opacity ("Vignette Opacity", Range(0.0, 1.0)) = 0.5
        godot_moire ("Warp Scanlines/Mask (Moire)", Range(0.0, 1.0)) = 0.0

        [HideInInspector]SourceSize ("SourceSize", Vector) = (1, 1, 1, 1)
        [HideInInspector]OriginalSize ("OriginalSize", Vector) = (1, 1, 1, 1)
        [HideInInspector]OutputSize ("OutputSize", Vector) = (1, 1, 1, 1)
        [HideInInspector]FrameCount ("FrameCount", Float) = 0
        FrameDirection ("Frame Direction", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "VhsAndCrtGodot"
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
                float FrameDirection;

                float godot_scanlines_opacity;
                float godot_scanlines_width;
                float godot_grille_opacity;
                float godot_pixelate;

                float godot_roll;
                float godot_roll_speed;
                float godot_roll_size;
                float godot_roll_variation;
                float godot_distort_intensity;

                float godot_noise_opacity;
                float godot_noise_speed;
                float godot_static_noise_intensity;

                float godot_aberration;
                float godot_brightness;
                float godot_discolor;

                float godot_warp_amount;
                float godot_clip_warp;

                float godot_vignette_intensity;
                float godot_vignette_opacity;
                float godot_moire;
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

            float2 Random2(float2 uv)
            {
                float2 d = float2(
                    dot(uv, float2(127.1, 311.7)),
                    dot(uv, float2(269.5, 183.3))
                );

                return -1.0 + 2.0 * frac(sin(d) * 43758.5453123);
            }

            float Noise2D(float2 uv)
            {
                float2 uvIndex = floor(uv);
                float2 uvFract = frac(uv);

                float2 blur = smoothstep(0.0, 1.0, uvFract);

                float a = dot(Random2(uvIndex + float2(0.0, 0.0)), uvFract - float2(0.0, 0.0));
                float b = dot(Random2(uvIndex + float2(1.0, 0.0)), uvFract - float2(1.0, 0.0));
                float c = dot(Random2(uvIndex + float2(0.0, 1.0)), uvFract - float2(0.0, 1.0));
                float d = dot(Random2(uvIndex + float2(1.0, 1.0)), uvFract - float2(1.0, 1.0));

                float x1 = lerp(a, b, blur.x);
                float x2 = lerp(c, d, blur.x);
                return lerp(x1, x2, blur.y) * 0.5 + 0.5;
            }

            float2 WarpUv(float2 uv)
            {
                float2 delta = uv - 0.5;
                float delta2 = dot(delta, delta);
                float delta4 = delta2 * delta2;
                float deltaOffset = delta4 * godot_warp_amount;
                return uv + delta * deltaOffset;
            }

            float Border(float2 uv)
            {
                float radius = min(godot_warp_amount, 0.08);
                radius = max(min(min(abs(radius * 2.0), 1.0), 1.0), 0.00001);

                float2 absUv = abs(uv * 2.0 - 1.0) - float2(1.0, 1.0) + radius;
                float dist = length(max(float2(0.0, 0.0), absUv)) / radius;
                float square = smoothstep(0.96, 1.0, dist);
                return saturate(1.0 - square);
            }

            float Vignette(float2 uv)
            {
                uv *= 1.0 - uv;
                float vignette = saturate(uv.x * uv.y * 15.0);
                float power = max(0.0001, godot_vignette_intensity * godot_vignette_opacity);
                return pow(vignette, power);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 resolution = SourceSize.xy;
                bool pixelate = godot_pixelate > 0.5;
                bool roll = godot_roll > 0.5;
                bool discolor = godot_discolor > 0.5;
                bool clipWarp = godot_clip_warp > 0.5;
                bool moire = godot_moire > 0.5;

                float rollSpeed = godot_roll_speed / 100.0;
                float rollSize = godot_roll_size / 8.0;
                float aberration = godot_aberration / 2.0;

                float2 uv = WarpUv(input.uv);
                float2 textUv = uv;
                float2 rollUv = float2(0.0, 0.0);
                float time = roll ? FrameCount : 0.0;

                if (pixelate)
                    textUv = ceil(uv * resolution) / max(resolution, float2(1.0, 1.0));

                float rollLine = 0.0;
                if ((FrameDirection < 0.0) && roll)
                {
                    rollLine = smoothstep(
                        0.3,
                        0.5,
                        sin(uv.y * (rollSize * godot_roll * 10.0) - (time * rollSpeed / 50.0))
                    );

                    rollLine *= rollLine * smoothstep(
                        0.3,
                        0.4,
                        sin(uv.y * (rollSize * godot_roll) * godot_roll_variation -
                            (0.1 * time * rollSpeed / 12.0 * godot_roll_variation))
                    );

                    rollUv = float2(rollLine * godot_distort_intensity * (1.0 - input.uv.x), 0.0);
                }

                float4 text;
                if (roll)
                {
                    text.r = SAMPLE_TEXTURE2D(Source, sampler_Source, textUv + rollUv * 0.8 + float2(aberration, 0.0) * 0.1).r;
                    text.g = SAMPLE_TEXTURE2D(Source, sampler_Source, textUv + rollUv * 1.2 - float2(aberration, 0.0) * 0.1).g;
                    text.b = SAMPLE_TEXTURE2D(Source, sampler_Source, textUv + rollUv).b;
                    text.a = 1.0;
                }
                else
                {
                    text.r = SAMPLE_TEXTURE2D(Source, sampler_Source, textUv + float2(aberration, 0.0) * 0.1).r;
                    text.g = SAMPLE_TEXTURE2D(Source, sampler_Source, textUv - float2(aberration, 0.0) * 0.1).g;
                    text.b = SAMPLE_TEXTURE2D(Source, sampler_Source, textUv).b;
                    text.a = 1.0;
                }

                float r = text.r;
                float g = text.g;
                float b = text.b;

                float2 maskUv = moire ? WarpUv(input.uv) : input.uv;
                if (godot_grille_opacity > 0.0)
                {
                    float gR = smoothstep(0.85, 0.95, abs(sin(maskUv.x * (resolution.x * PI))));
                    float gG = smoothstep(0.85, 0.95, abs(sin(1.05 + maskUv.x * (resolution.x * PI))));
                    float gB = smoothstep(0.85, 0.95, abs(sin(2.1 + maskUv.x * (resolution.x * PI))));

                    r = lerp(r, r * gR, godot_grille_opacity);
                    g = lerp(g, g * gG, godot_grille_opacity);
                    b = lerp(b, b * gB, godot_grille_opacity);
                }

                text.r = saturate(r * godot_brightness);
                text.g = saturate(g * godot_brightness);
                text.b = saturate(b * godot_brightness);

                float scanlines = 0.5;
                if (godot_scanlines_opacity > 0.0)
                {
                    scanlines = smoothstep(
                        godot_scanlines_width,
                        godot_scanlines_width + 0.5,
                        abs(sin(maskUv.y * (resolution.y * PI)))
                    );

                    text.rgb = lerp(text.rgb, text.rgb * scanlines.xxx, godot_scanlines_opacity);
                }

                uv = WarpUv(input.uv);
                if (roll || FrameDirection < 0.0)
                {
                    float bandNoise = smoothstep(
                        0.4,
                        0.5,
                        Noise2D(uv * float2(2.0, 200.0) + float2(10.0, time * godot_noise_speed))
                    );

                    float2 rndUv = ceil(uv * resolution) / max(resolution, float2(1.0, 1.0));
                    float rnd = Random2(rndUv + float2(time * 0.8, 0.0)).x;
                    rollLine *= bandNoise * scanlines * saturate(rnd + 0.8);

                    text.rgb = saturate(lerp(text.rgb, text.rgb + rollLine.xxx, godot_noise_opacity));
                }

                if (godot_static_noise_intensity > 0.0)
                {
                    float2 staticUv = ceil(uv * resolution) / max(resolution, float2(1.0, 1.0));
                    float tFrac = frac(time / 100.0);
                    float staticNoise = saturate(Random2(staticUv + tFrac.xx).x);
                    text.rgb += staticNoise.xxx * godot_static_noise_intensity;
                }

                float border = Border(uv);
                text.rgb *= border;
                text.rgb *= Vignette(uv);

                if (clipWarp)
                    text.a = border;

                if (discolor)
                {
                    const float saturation = 0.5;
                    const float contrast = 1.2;

                    float3 greyscale = (text.r + text.g + text.b).xxx / 3.0;
                    text.rgb = lerp(text.rgb, greyscale, saturation);

                    float midpoint = pow(0.5, 2.2);
                    text.rgb = (text.rgb - midpoint.xxx) * contrast + midpoint.xxx;
                }

                return text;
            }
            ENDHLSL
        }
    }
}
