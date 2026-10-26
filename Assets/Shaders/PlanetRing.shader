// 行星環：畫在一張 Quad 上（Prefab 裡轉成躺在行星赤道面）。
// Quad 的 UV 換算成「離行星中心幾倍行星半徑」，再去查環的徑向剖面貼圖（RGB 顏色、A 不透明度）。
// 光從太陽來；行星擋住太陽的部分會有影子。Alpha 混合，兩面都畫。
Shader "StarrySky/PlanetRing"
{
    Properties
    {
        _RingTex     ("環（RGB 顏色、A 不透明度）", 2D) = "black" {}
        _RingInner   ("環內緣（行星半徑）", Float) = 1.1
        _RingOuter   ("環外緣（行星半徑）= Quad 半徑", Float) = 2.4
        _LightDir    ("預覽用光線方向（沒有太陽時）", Vector) = (1, 0.3, 0.6, 0)
        _Brightness  ("亮度", Float) = 1.0
        _BackLight   ("背光面亮度", Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "PlanetLighting.cginc"

            sampler2D _RingTex;
            float _RingInner, _RingOuter, _Brightness, _BackLight;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 worldPos : TEXCOORD1; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float r = length(i.uv - 0.5) * 2.0 * _RingOuter;    // 離中心幾倍行星半徑
                float u = (r - _RingInner) / (_RingOuter - _RingInner);
                clip(u * (1 - u));                                   // 環的範圍外不畫
                float4 ring = tex2D(_RingTex, float2(u, 0.5));

                float3 C = unity_ObjectToWorld._m03_m13_m23;         // Quad 中心 = 行星中心
                float side = length(unity_ObjectToWorld._m00_m10_m20);   // Quad 邊長（世界單位）= 環外緣直徑
                float R = side / (2.0 * _RingOuter);                 // 行星半徑（世界單位）
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1)));
                float3 L = StarrySkyLightDir(i.worldPos);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);

                // 照到光的那一面比較亮；從背面看是透過環的散射光，比較暗
                float sameSide = (dot(n, L) * dot(n, V) > 0) ? 1.0 : _BackLight;
                float lit = lerp(0.3, 1.0, saturate(abs(dot(n, L)) * 3.0)) * sameSide;

                // 行星的影子：從這一點往太陽的射線有沒有打到行星球體
                float3 oc = i.worldPos - C;
                float b = dot(oc, L);
                float c = dot(oc, oc) - R * R;
                if (b < 0 && b * b - c > 0) lit *= 0.05;

                return float4(ring.rgb * lit * _Brightness, ring.a);
            }
            ENDCG
        }
    }
}
