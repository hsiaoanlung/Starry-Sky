// 沒有 Prefab 的天體（目前是衛星）用的最簡單 Shader：單一顏色 + 從太陽來的漫反射光（Lambert）。
// 光的方向和行星一樣從太陽來（見 PlanetLighting.cginc），所以衛星的亮面會和母行星朝同一邊。
// 沒有 LightMode 標籤，URP 與 Built-in 都能畫；不需要場景裡的 Directional Light。
Shader "StarrySky/SimpleBody"
{
    Properties
    {
        _Color            ("顏色", Color) = (0.8, 0.8, 0.8, 1)
        _LightDir         ("預覽用光線方向（沒有太陽時）", Vector) = (1, 0.3, 0.6, 0)
        _Ambient          ("背光面最低亮度（越大背光面越亮）", Range(0, 0.2)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "PlanetLighting.cginc"

            float4 _Color;
            float _Ambient;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldN   : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldN = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldN);
                float3 L = StarrySkyLightDir(i.worldPos);
                float lit = saturate(dot(N, L));          // Lambert：表面正對光源最亮，側面越來越暗，背面 0
                return float4(_Color.rgb * (lit + _Ambient), 1);
            }
            ENDCG
        }
    }
}
