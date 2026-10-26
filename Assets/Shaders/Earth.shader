// 地球：白天地表 + 海面反光 + 會飄的雲 + 夜晚城市燈光 + 大氣邊緣光。
// 光照自己算：光的方向 = 從地球指向太陽（SunVisual 每幀設定全域 _StarrySkySunPos）。
// 場景裡沒有太陽時（例如單獨預覽 Prefab），改用材質上的 _LightDir。
// 沒有 LightMode 標籤，URP 與 Built-in 管線都能畫。
Shader "StarrySky/Earth"
{
    Properties
    {
        _MainTex          ("地表（RGB）+ 海洋遮罩（A）", 2D) = "white" {}
        _CloudTex         ("雲（灰階）", 2D) = "black" {}
        _NightTex         ("夜間燈光", 2D) = "black" {}
        _LightDir         ("預覽用光線方向（沒有太陽時）", Vector) = (1, 0.3, 0.6, 0)
        _CloudSpeed       ("雲飄動速度", Float) = 0.004
        _CloudShadow      ("雲的陰影", Range(0, 1)) = 0.35
        [HDR] _NightColor ("夜間燈光亮度", Color) = (1.2, 1.2, 1.2, 1)
        _SpecPower        ("海面反光集中度", Float) = 60
        _SpecStrength     ("海面反光強度", Range(0, 2)) = 0.8
        [HDR] _AtmosphereColor ("大氣顏色", Color) = (0.3, 0.55, 1.0, 1)
        _AtmospherePower  ("大氣邊緣寬度（越大越窄）", Float) = 3
        _Ambient          ("背光面最低亮度", Range(0, 0.2)) = 0.02
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

            sampler2D _MainTex, _CloudTex, _NightTex;
            float4 _LightDir, _NightColor, _AtmosphereColor;
            float _CloudSpeed, _CloudShadow, _SpecPower, _SpecStrength, _AtmospherePower, _Ambient;

            float4 _StarrySkySunPos;   // 全域：xyz = 太陽世界座標，w = 1 表示有效（由 SunVisual 設定）

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f
            {
                float4 pos      : SV_POSITION;
                float2 uv       : TEXCOORD0;
                float3 worldN   : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldN = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldN);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 L = _StarrySkySunPos.w > 0.5
                    ? normalize(_StarrySkySunPos.xyz - i.worldPos)
                    : normalize(_LightDir.xyz);

                float NdotL = dot(N, L);
                float day = saturate(NdotL);
                float dayMask = smoothstep(-0.15, 0.2, NdotL);   // 晨昏線的柔和過渡

                float4 ground = tex2D(_MainTex, i.uv);
                float clouds = tex2D(_CloudTex, i.uv + float2(_Time.y * _CloudSpeed, 0)).r;
                // 雲的陰影：往光的反方向偏一點再取樣一次（簡化版）
                float cloudShadow = tex2D(_CloudTex, i.uv + float2(_Time.y * _CloudSpeed + 0.002, 0.002)).r;

                // 白天地表（Lambert）+ 海面反光（Blinn-Phong，只在海洋遮罩 A = 1 的地方）
                float3 H = normalize(L + V);
                float spec = pow(saturate(dot(N, H)), _SpecPower) * ground.a * day * (1 - clouds) * _SpecStrength;
                float3 surface = ground.rgb * (day + _Ambient) * (1 - cloudShadow * _CloudShadow)
                               + spec * float3(1.0, 0.95, 0.85);

                // 雲：照到光的一面是白的，晨昏線附近稍微延伸
                float3 cloudLit = saturate(NdotL * 1.1 + 0.05) + _Ambient;
                float3 col = lerp(surface, cloudLit, clouds);

                // 夜晚城市燈光：只在背光面，被雲擋住會變暗
                col += tex2D(_NightTex, i.uv).rgb * _NightColor.rgb * (1 - dayMask) * (1 - clouds * 0.8);

                // 大氣：邊緣（法線和視線接近垂直）發藍光，只在照到光的那一側
                float fresnel = pow(1 - saturate(dot(N, V)), _AtmospherePower);
                col += _AtmosphereColor.rgb * fresnel * saturate(NdotL + 0.3);

                return float4(col, 1);
            }
            ENDCG
        }
    }
}
