Shader "DoodleDiplomacy/Environment/Surface Tile World Grid"
{
    Properties
    {
        [MainTexture][NoScaleOffset] _BaseMap("Surface Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Surface Color", Color) = (0.78, 0.8, 0.82, 1)
        [NoScaleOffset][Normal] _BumpMap("Surface Normal", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0, 2)) = 0.45
        _Smoothness("Surface Smoothness", Range(0, 1)) = 0.18
        _GroutSmoothness("Grid Smoothness", Range(0, 1)) = 0.12

        [Header(Grid Size in World Meters)]
        _TileSizeX("Tile Width X", Range(0.1, 5)) = 0.61
        _TileSizeZ("Tile Length Z", Range(0.1, 5)) = 0.61
        _GroutWidth("Grid Line Width", Range(0.001, 0.1)) = 0.012
        _BevelWidth("Tile Edge Bevel", Range(0, 0.05)) = 0.008
        _GridRotation("Grid Rotation", Range(-180, 180)) = 0
        _GridOffset("Grid Offset X/Z", Vector) = (0, 0, 0, 0)

        [Header(Surface Detail)]
        _MicroTextureWorldSize("Surface Texture Size", Range(0.1, 5)) = 0.7
        _TileVariation("Per-Tile Color Variation", Range(0, 0.2)) = 0.07
        _GroutColor("Grid Color", Color) = (0.012, 0.014, 0.016, 1)

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

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _GroutColor;
            half _BumpScale;
            half _Smoothness;
            half _GroutSmoothness;
            half _TileVariation;
            float _TileSizeX;
            float _TileSizeZ;
            float _GroutWidth;
            float _BevelWidth;
            float _GridRotation;
            float4 _GridOffset;
            float _MicroTextureWorldSize;
        CBUFFER_END

        struct TileGridSample
        {
            half3 albedo;
            half3 normalWS;
            half smoothness;
        };

        float2 RotateGrid(float2 position, float sine, float cosine)
        {
            return float2(
                cosine * position.x - sine * position.y,
                sine * position.x + cosine * position.y);
        }

        half HashCell(float2 cell)
        {
            float3 value = frac(float3(cell.xyx) * 0.1031);
            value += dot(value, value.yzx + 33.33);
            return frac((value.x + value.y) * value.z);
        }

        TileGridSample SampleTileGrid(float3 positionWS, half3 geometricNormalWS)
        {
            TileGridSample result;

            float angle = radians(_GridRotation);
            float sine = sin(angle);
            float cosine = cos(angle);
            float2 gridPosition = RotateGrid(positionWS.xz, sine, cosine) + _GridOffset.xy;
            float2 tileSize = max(float2(_TileSizeX, _TileSizeZ), 0.01);

            float2 cellPosition = gridPosition / tileSize;
            float2 localMeters = frac(cellPosition) * tileSize;
            float2 edgeDistance = min(localMeters, tileSize - localMeters);
            float nearestEdge = min(edgeDistance.x, edgeDistance.y);

            float groutHalfWidth = min(_GroutWidth * 0.5, min(tileSize.x, tileSize.y) * 0.45);
            float edgeAA = max(fwidth(nearestEdge), 0.00025);
            half gridMask = 1.0h - smoothstep(
                groutHalfWidth - edgeAA,
                groutHalfWidth + edgeAA,
                nearestEdge);

            float bevelEnd = groutHalfWidth + max(_BevelWidth, 0.0001);
            half bevelMask = 1.0h - smoothstep(groutHalfWidth, bevelEnd, nearestEdge);
            half bevelOnly = saturate(bevelMask - gridMask);

            half normalSignY = geometricNormalWS.y >= 0 ? 1.0h : -1.0h;
            float microScale = max(_MicroTextureWorldSize, 0.01);
            float2 microUV = float2(gridPosition.x, gridPosition.y * -normalSignY) / microScale;

            half3 tileAlbedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, microUV).rgb * _BaseColor.rgb;
            half tileRandom = HashCell(floor(cellPosition));
            half variation = lerp(1.0h - _TileVariation, 1.0h + _TileVariation, tileRandom);
            tileAlbedo *= variation;
            tileAlbedo *= lerp(1.0h, 0.82h, bevelOnly);
            result.albedo = lerp(tileAlbedo, _GroutColor.rgb, gridMask);

            half3 normalTS = UnpackNormalScale(
                SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, microUV),
                _BumpScale);
            half3 tangentWS = half3(cosine, 0, -sine);
            half3 bitangentWS = half3(-normalSignY * sine, 0, -normalSignY * cosine);
            half3 ceilingNormalWS = half3(0, normalSignY, 0);
            half3 mappedNormalWS = normalize(
                normalTS.x * tangentWS +
                normalTS.y * bitangentWS +
                normalTS.z * ceilingNormalWS);
            half horizontalWeight = saturate(abs(geometricNormalWS.y));
            result.normalWS = normalize(lerp(geometricNormalWS, mappedNormalWS, horizontalWeight));
            result.normalWS = normalize(lerp(result.normalWS, geometricNormalWS, gridMask));
            result.smoothness = lerp(_Smoothness, _GroutSmoothness, gridMask);
            return result;
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

                TileGridSample materialSample = SampleTileGrid(input.positionWS, input.normalWS);

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
                surfaceData.albedo = materialSample.albedo;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.metallic = 0;
                surfaceData.smoothness = materialSample.smoothness;
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
            #pragma vertex TileDepthNormalsVertex
            #pragma fragment TileDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #if defined(LOD_FADE_CROSSFADE)
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
            #endif

            struct TileDepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct TileDepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TileDepthNormalsVaryings TileDepthNormalsVertex(TileDepthNormalsAttributes input)
            {
                TileDepthNormalsVaryings output = (TileDepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            void TileDepthNormalsFragment(
                TileDepthNormalsVaryings input,
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

                float3 normalWS = SampleTileGrid(input.positionWS, input.normalWS).normalWS;

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
            #pragma vertex TileMetaVertex
            #pragma fragment TileMetaFragment
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct TileMetaAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct TileMetaVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
            #ifdef EDITOR_VISUALIZATION
                float2 VizUV : TEXCOORD2;
                float4 LightCoord : TEXCOORD3;
            #endif
            };

            TileMetaVaryings TileMetaVertex(TileMetaAttributes input)
            {
                TileMetaVaryings output = (TileMetaVaryings)0;
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

            half4 TileMetaFragment(TileMetaVaryings input) : SV_Target
            {
                TileGridSample materialSample = SampleTileGrid(input.positionWS, input.normalWS);

                BRDFData brdfData;
                half alpha = 1.0h;
                InitializeBRDFData(
                    materialSample.albedo,
                    0.0h,
                    half3(0.0h, 0.0h, 0.0h),
                    materialSample.smoothness,
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
