// 一般行星（水星、金星、火星、木星、土星、天王星、海王星）共用的 Shader。
// 功能用參數開關：地形凹凸（高度存在貼圖 A）、自轉與雲帶流動、臨邊昏暗、大氣邊緣光、行星環的影子。
// 光從太陽來（見 PlanetLighting.cginc）。沒有 LightMode 標籤，URP 與 Built-in 都能畫。
Shader "StarrySky/Planet"
{
    Properties
    {
        _MainTex          ("表面（RGB）+ 高度（A）", 2D) = "white" {}
        _LightDir         ("預覽用光線方向（沒有太陽時）", Vector) = (1, 0.3, 0.6, 0)
        _Ambient          ("背光面最低亮度", Range(0, 0.2)) = 0.02

        [Header(Terrain)]
        _BumpStrength     ("地形凹凸強度（0 = 關）", Range(0, 20)) = 0

        [Header(Rotation)]
        _RotationSpeed    ("自轉（UV / 秒）", Float) = 0
        _BandShear        ("雲帶相對流動（UV / 秒）", Float) = 0

        [Header(Atmosphere)]
        _LimbDarkening    ("臨邊昏暗（0 = 關）", Range(0, 2)) = 0
        [HDR] _AtmosphereColor ("大氣顏色（黑 = 關）", Color) = (0, 0, 0, 1)
        _AtmospherePower  ("大氣邊緣寬度（越大越窄）", Float) = 3

        [Header(Ring Shadow)]
        _RingTex          ("環（RGB 顏色、A 不透明度）", 2D) = "black" {}
        _RingInner        ("環內緣（行星半徑）", Float) = 1.1
        _RingOuter        ("環外緣（行星半徑）", Float) = 2.4
        _RingShadow       ("環的影子（0 = 關）", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "PlanetLighting.cginc"

            sampler2D _MainTex, _RingTex;
            float4 _MainTex_TexelSize;   // Unity 自動填入：(1/寬, 1/高, 寬, 高)
            float4 _AtmosphereColor;
            float _Ambient, _BumpStrength, _RotationSpeed, _BandShear, _LimbDarkening, _AtmospherePower;
            float _RingInner, _RingOuter, _RingShadow;

            struct appdata
            {
                float4 vertex  : POSITION;
                float3 normal  : NORMAL;
                float4 tangent : TANGENT;   // 沿 UV 的 u 方向（東）
                float2 uv      : TEXCOORD0;
            };
            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float3 worldN   : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                float4 worldT   : TEXCOORD3;   // w = 副切線方向的正負號
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldN = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldT = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w * unity_WorldTransformParams.w);
                return o;
            }

            // 從太陽照向這一點的光，有沒有先穿過行星環？回傳被擋住的比例
            float RingShadow(float3 worldPos, float3 L)
            {
                float3 C = unity_ObjectToWorld._m03_m13_m23;                // 行星中心
                float3 axis = unity_ObjectToWorld._m01_m11_m21;             // 物件 y 軸 = 自轉軸，環在赤道面
                float R = length(axis) * 0.5;                               // 內建球體直徑 1 → 半徑 0.5
                axis = normalize(axis);
                float denom = dot(L, axis);
                if (abs(denom) < 1e-4) return 0;
                float t = dot(C - worldPos, axis) / denom;                  // 沿光線走多遠會碰到赤道面
                if (t <= 0) return 0;
                float r = length(worldPos + L * t - C) / R;
                float u = (r - _RingInner) / (_RingOuter - _RingInner);
                if (u <= 0 || u >= 1) return 0;
                return tex2Dlod(_RingTex, float4(u, 0.5, 0, 0)).a;
            }

            float4 frag(v2f i) : SV_Target
            {
                // 自轉：整張貼圖沿經度捲動；雲帶流動：不同緯度捲動速度不同
                float2 uv = i.uv;
                float shear = sin(uv.y * 37.7) * 0.6 + sin(uv.y * 17.3 + 1.3) * 0.4;
                uv.x += _Time.y * (_RotationSpeed + _BandShear * shear);

                float4 tex = tex2D(_MainTex, uv);
                float3 N = normalize(i.worldN);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 L = StarrySkyLightDir(i.worldPos);

                // 地形凹凸：用相鄰像素的高度差，把法線往「下坡」方向傾斜
                if (_BumpStrength > 0)
                {
                    float2 ts = _MainTex_TexelSize.xy;
                    float dhdu = tex2D(_MainTex, uv + float2(ts.x, 0)).a - tex2D(_MainTex, uv - float2(ts.x, 0)).a;
                    float dhdv = tex2D(_MainTex, uv + float2(0, ts.y)).a - tex2D(_MainTex, uv - float2(0, ts.y)).a;
                    float3 T = normalize(i.worldT.xyz);
                    float3 B = cross(N, T) * i.worldT.w;
                    N = normalize(N - _BumpStrength * (dhdu * T + dhdv * B));
                }

                float NdotL = dot(N, L);
                float lit = saturate(NdotL);
                if (_RingShadow > 0) lit *= 1 - RingShadow(i.worldPos, L) * _RingShadow;

                float3 col = tex.rgb * (lit + _Ambient);

                // 臨邊昏暗：氣態行星邊緣比較暗
                float NdotV = saturate(dot(normalize(i.worldN), V));
                if (_LimbDarkening > 0) col *= pow(max(NdotV, 1e-3), _LimbDarkening);

                // 大氣邊緣光（只在照到光的那一側）
                float fresnel = pow(1 - NdotV, _AtmospherePower);
                col += _AtmosphereColor.rgb * fresnel * saturate(dot(normalize(i.worldN), L) + 0.3);

                return float4(col, 1);
            }
            ENDCG
        }
    }
}
