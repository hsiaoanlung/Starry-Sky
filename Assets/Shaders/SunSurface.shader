// 太陽表面：流動的電漿（domain-warped fBm）+ 臨邊昏暗。
// 不受光照影響（Unlit），輸出 HDR 顏色，搭配 Bloom 會發光。
// 沒有 LightMode 標籤，URP 與 Built-in 管線都能畫。
Shader "StarrySky/SunSurface"
{
    Properties
    {
        [HDR] _HotColor   ("亮部顏色", Color) = (4.0, 3.2, 1.6, 1)
        [HDR] _CoolColor  ("暗部顏色", Color) = (1.6, 0.45, 0.08, 1)
        [HDR] _LimbColor  ("邊緣顏色", Color) = (1.2, 0.25, 0.03, 1)
        _NoiseScale       ("電漿細節大小", Float) = 3.0
        _WarpStrength     ("扭曲強度", Float) = 1.2
        _FlowSpeed        ("流動速度", Float) = 0.15
        _LimbPower        ("臨邊昏暗程度", Range(0.1, 2)) = 0.5
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
            #include "SunNoise.cginc"

            float4 _HotColor, _CoolColor, _LimbColor;
            float _NoiseScale, _WarpStrength, _FlowSpeed, _LimbPower;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 objPos   : TEXCOORD0;   // 物件空間位置：噪聲黏在球上，跟著球移動
                float3 worldN   : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.objPos = normalize(v.vertex.xyz);
                o.worldN = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float t = _Time.y * _FlowSpeed;
                float3 p = i.objPos * _NoiseScale;

                // Domain warping：用一組噪聲扭曲另一組噪聲的座標，產生翻騰、拉絲的電漿流動
                float3 warp = float3(sunFbm(p + float3(0, 0, t)),
                                     sunFbm(p + float3(5.2, 1.3, -t)),
                                     sunFbm(p + float3(2.8, 7.1, t * 0.7)));
                float n = sunFbm(p + _WarpStrength * warp + float3(t * 0.5, 0, 0));

                // 細的米粒組織疊在大尺度流動上
                float grain = sunNoise(p * 8.0 + warp * 2.0 + t * 2.0);
                n = saturate(n * 0.85 + grain * 0.25);
                n = smoothstep(0.25, 0.8, n);

                float3 col = lerp(_CoolColor.rgb, _HotColor.rgb, n);

                // 臨邊昏暗：表面越斜（邊緣）越暗、越紅
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float mu = saturate(dot(normalize(i.worldN), V));
                col = lerp(_LimbColor.rgb, col, pow(mu, _LimbPower));

                return float4(col, 1);
            }
            ENDCG
        }
    }
}
