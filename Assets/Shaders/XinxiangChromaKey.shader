Shader "NexusAnima/XinxiangChromaKey"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _KeyColor ("Key Color", Color) = (0, 0, 0, 1)
        _Threshold ("Threshold", Range(0, 1)) = 0.08
        _Smoothness ("Smoothness", Range(0.001, 0.5)) = 0.12
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "XinxiangChromaKey"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _KeyColor;
            fixed4 _Color;
            float _Threshold;
            float _Smoothness;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.texcoord) * i.color;

                // Luma + chroma distance: remove near-black backdrop.
                float luma = dot(col.rgb, float3(0.299, 0.587, 0.114));
                float keyLuma = dot(_KeyColor.rgb, float3(0.299, 0.587, 0.114));
                float score = max(abs(luma - keyLuma), distance(col.rgb, _KeyColor.rgb));
                float alpha = smoothstep(_Threshold, _Threshold + _Smoothness, score);
                col.a *= alpha;
                return col;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
