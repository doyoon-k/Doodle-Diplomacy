Shader "Hidden/ShaderGlass/BuiltIn/Newpixie/BlurHoriz"
{
    Properties
    {
        [HideInInspector]_MainTex ("Texture", 2D) = "white" {}
        blur_x ("Horizontal Blur", Range(0.0, 5.0)) = 1.0
        [HideInInspector]SourceSize ("SourceSize", Vector) = (1, 1, 1, 1)
        [HideInInspector]OriginalSize ("OriginalSize", Vector) = (1, 1, 1, 1)
        [HideInInspector]OutputSize ("OutputSize", Vector) = (1, 1, 1, 1)
        [HideInInspector]FrameCount ("FrameCount", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Name "BlurHoriz"
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D Source;

            float4 SourceSize;
            float4 OriginalSize;
            float4 OutputSize;
            float FrameCount;
            float blur_x;

            struct Attributes
            {
                float4 vertex : POSITION;
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
                output.positionCS = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float2 blur = float2(blur_x, 0.0) * OutputSize.zw;
                float2 uv = input.uv;
                fixed4 sum = tex2D(Source, uv) * 0.2270270288;
                sum += tex2D(Source, float2(uv.x - (4.0 * blur.x), uv.y - (4.0 * blur.y))) * 0.0162162166;
                sum += tex2D(Source, float2(uv.x - (3.0 * blur.x), uv.y - (3.0 * blur.y))) * 0.0540540554;
                sum += tex2D(Source, float2(uv.x - (2.0 * blur.x), uv.y - (2.0 * blur.y))) * 0.1216216236;
                sum += tex2D(Source, float2(uv.x - (1.0 * blur.x), uv.y - (1.0 * blur.y))) * 0.1945945919;
                sum += tex2D(Source, float2(uv.x + (1.0 * blur.x), uv.y + (1.0 * blur.y))) * 0.1945945919;
                sum += tex2D(Source, float2(uv.x + (2.0 * blur.x), uv.y + (2.0 * blur.y))) * 0.1216216236;
                sum += tex2D(Source, float2(uv.x + (3.0 * blur.x), uv.y + (3.0 * blur.y))) * 0.0540540554;
                sum += tex2D(Source, float2(uv.x + (4.0 * blur.x), uv.y + (4.0 * blur.y))) * 0.0162162166;
                return sum;
            }
            ENDCG
        }
    }
}
