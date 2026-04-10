Shader "Hidden/ShaderGlass/BuiltIn/Preprocess"
{
    Properties
    {
        [HideInInspector]_MainTex ("Texture", 2D) = "white" {}
        [HideInInspector]_ColorConversion ("Color Conversion", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Name "Preprocess"
            ZTest Always
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _UVScaleOffset;
            float _ColorConversion;

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
                float2 uv = input.uv * _UVScaleOffset.xy + _UVScaleOffset.zw;
                fixed4 color = tex2D(_MainTex, uv);
            #if !defined(UNITY_COLORSPACE_GAMMA)
                if (_ColorConversion > 1.5)
                {
                    color.rgb = GammaToLinearSpace(color.rgb);
                }
                else if (_ColorConversion > 0.5)
                {
                    color.rgb = LinearToGammaSpace(color.rgb);
                }
            #endif
                return color;
            }
            ENDCG
        }
    }
}
