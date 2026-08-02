Shader "DoodleDiplomacy/Environment/Surface World Aligned"
{
    Properties
    {
        [MainTexture][NoScaleOffset] _BaseMap("Base Color", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        [NoScaleOffset][Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0, 2)) = 0.65
        [NoScaleOffset] _RoughnessMap("Roughness Map", 2D) = "white" {}
        _RoughnessMapStrength("Roughness Map Influence", Range(0, 1)) = 0
        _Metallic("Metallic", Range(0, 1)) = 0.86
        _Smoothness("Smoothness", Range(0, 1)) = 0.34
        _WorldTileSize("World Tile Size (Meters)", Range(0.1, 20)) = 2
        _ProjectionSharpness("Projection Blend Sharpness", Range(1, 16)) = 8
        _WorldOffset("World Offset", Vector) = (0, 0, 0, 0)

        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

        TEXTURE2D(_RoughnessMap);
        SAMPLER(sampler_RoughnessMap);

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _BumpScale;
            half _RoughnessMapStrength;
            half _Metallic;
            half _Smoothness;
            float _WorldTileSize;
            float _ProjectionSharpness;
            float4 _WorldOffset;
        CBUFFER_END

        struct WorldAlignedSample
        {
            half4 albedo;
            half3 normalWS;
            half roughness;
        };

        WorldAlignedSample SampleWorldAligned(float3 positionWS, half3 geometricNormalWS)
        {
            WorldAlignedSample result;

            half3 normalWS = normalize(geometricNormalWS);
            float3 axisSign = step(0.0, normalWS) * 2.0 - 1.0;
            float3 weights = pow(max(abs(normalWS), 0.0001), _ProjectionSharpness);
            weights /= max(weights.x + weights.y + weights.z, 0.0001);

            float tileSize = max(_WorldTileSize, 0.001);
            float3 samplePosition = positionWS + _WorldOffset.xyz;

            // Side projections keep the texture's vertical axis aligned to world Y.
            float2 uvX = float2(-samplePosition.z * axisSign.x, samplePosition.y) / tileSize;
            float2 uvZ = float2(samplePosition.x * axisSign.z, samplePosition.y) / tileSize;
            float2 uvY = float2(samplePosition.x, -samplePosition.z * axisSign.y) / tileSize;

            half4 albedoX = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX);
            half4 albedoY = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY);
            half4 albedoZ = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ);
            result.albedo = albedoX * weights.x + albedoY * weights.y + albedoZ * weights.z;

            half roughnessX = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uvX).r;
            half roughnessY = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uvY).r;
            half roughnessZ = SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, uvZ).r;
            result.roughness = roughnessX * weights.x + roughnessY * weights.y + roughnessZ * weights.z;

            half3 normalXTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), _BumpScale);
            half3 normalYTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), _BumpScale);
            half3 normalZTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), _BumpScale);

            half3 normalXWS = normalize(
                normalXTS.x * half3(0, 0, -axisSign.x) +
                normalXTS.y * half3(0, 1, 0) +
                normalXTS.z * half3(axisSign.x, 0, 0));
            half3 normalYWS = normalize(
                normalYTS.x * half3(1, 0, 0) +
                normalYTS.y * half3(0, 0, -axisSign.y) +
                normalYTS.z * half3(0, axisSign.y, 0));
            half3 normalZWS = normalize(
                normalZTS.x * half3(axisSign.z, 0, 0) +
                normalZTS.y * half3(0, 1, 0) +
                normalZTS.z * half3(0, 0, axisSign.z));

            result.normalWS = normalize(
                normalXWS * weights.x +
                normalYWS * weights.y +
                normalZWS * weights.z);
            return result;
        }

        half ResolveSmoothness(WorldAlignedSample materialSample)
        {
            half mappedSmoothness = saturate(1.0h - materialSample.roughness);
            return lerp(_Smoothness, mappedSmoothness, _RoughnessMapStrength);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                half3 vertexLighting : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                output.vertexLighting = VertexLighting(positionInputs.positionWS, normalInputs.normalWS);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH(normalInputs.normalWS, output.vertexSH);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                WorldAlignedSample materialSample = SampleWorldAligned(input.positionWS, input.normalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = materialSample.normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
                inputData.vertexLighting = input.vertexLighting;
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, materialSample.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = materialSample.albedo.rgb * _BaseColor.rgb;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = ResolveSmoothness(materialSample);
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.emission = half3(0, 0, 0);
                surfaceData.occlusion = 1;
                surfaceData.alpha = 1;
                surfaceData.clearCoatMask = 0;
                surfaceData.clearCoatSmoothness = 0;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SurfaceDepthNormalsVertex
            #pragma fragment SurfaceDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #if defined(LOD_FADE_CROSSFADE)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            struct SurfaceDepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct SurfaceDepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            SurfaceDepthNormalsVaryings SurfaceDepthNormalsVertex(SurfaceDepthNormalsAttributes input)
            {
                SurfaceDepthNormalsVaryings output = (SurfaceDepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            void SurfaceDepthNormalsFragment(
                SurfaceDepthNormalsVaryings input,
                out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            #if defined(LOD_FADE_CROSSFADE)
                LODFadeCrossFade(input.positionCS);
            #endif

                float3 normalWS = SampleWorldAligned(input.positionWS, input.normalWS).normalWS;

            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalize(normalWS));
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                outNormalWS = half4(PackFloat2To888(remappedOctNormalWS), 0.0);
            #else
                outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
            #endif

            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SurfaceMetaVertex
            #pragma fragment SurfaceMetaFragment
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct SurfaceMetaAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct SurfaceMetaVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            #ifdef EDITOR_VISUALIZATION
                float2 VizUV : TEXCOORD2;
                float4 LightCoord : TEXCOORD3;
            #endif
            };

            SurfaceMetaVaryings SurfaceMetaVertex(SurfaceMetaAttributes input)
            {
                SurfaceMetaVaryings output = (SurfaceMetaVaryings)0;
                output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz, input.uv1, input.uv2);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            #ifdef EDITOR_VISUALIZATION
                UnityEditorVizData(
                    input.positionOS.xyz,
                    input.uv0,
                    input.uv1,
                    input.uv2,
                    output.VizUV,
                    output.LightCoord);
            #endif
                return output;
            }

            half4 SurfaceMetaFragment(SurfaceMetaVaryings input) : SV_Target
            {
                WorldAlignedSample materialSample = SampleWorldAligned(input.positionWS, input.normalWS);

                BRDFData brdfData;
                half alpha = 1.0h;
                InitializeBRDFData(
                    materialSample.albedo.rgb * _BaseColor.rgb,
                    _Metallic,
                    half3(0.0h, 0.0h, 0.0h),
                    ResolveSmoothness(materialSample),
                    alpha,
                    brdfData);

                MetaInput metaInput = (MetaInput)0;
                metaInput.Albedo = brdfData.diffuse + brdfData.specular * brdfData.roughness * 0.5h;
                metaInput.Emission = 0.0h;
            #ifdef EDITOR_VISUALIZATION
                metaInput.VizUV = input.VizUV;
                metaInput.LightCoord = input.LightCoord;
            #endif
                return UnityMetaFragment(metaInput);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
