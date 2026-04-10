Shader "Hidden/ShaderGlass/BuiltIn/Newpixie/Accumulate"
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
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Name "Accumulate"
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D Source;
            sampler2D PassFeedback1;

            float4 SourceSize;
            float4 OriginalSize;
            float4 OutputSize;
            float FrameCount;
            float acc_modulate;

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
                fixed4 a = tex2D(PassFeedback1, input.uv) * acc_modulate;
                fixed4 b = tex2D(Source, input.uv);
                return max(a, b * 0.96);
            }
            ENDCG
        }
    }
}
