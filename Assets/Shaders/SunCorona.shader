// 日冕：畫在一張永遠面向相機的 Quad 上（由 SunVisual 轉向）。
// 用極座標取樣噪聲做出往外放射、往外流動的絲狀光芒，亮度隨距離指數衰減，Additive 混合。
// Quad 放在太陽中心，內側被太陽球體擋住（深度測試），所以只看得到球外的部分。
Shader "StarrySky/SunCorona"
{
    Properties
    {
        [HDR] _InnerColor ("內圈顏色", Color) = (3.0, 2.2, 1.0, 1)
        [HDR] _OuterColor ("外圈顏色", Color) = (1.2, 0.3, 0.05, 1)
        _CoronaSize       ("Quad 半徑（以太陽半徑為單位）", Float) = 4.0
        _Falloff          ("衰減速度", Float) = 2.2
        _RayFrequency     ("光芒數量", Float) = 6.0
        _RaySharpness     ("光芒銳利度", Float) = 2.5
        _FlowSpeed        ("往外流動速度", Float) = 0.25
        _Intensity        ("整體亮度", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One      // Additive：疊加發光
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "SunNoise.cginc"

            float4 _InnerColor, _OuterColor;
            float _CoronaSize, _Falloff, _RayFrequency, _RaySharpness, _FlowSpeed, _Intensity;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 q = (i.uv - 0.5) * 2.0;     // -1 ~ 1，Quad 邊緣 = 1
                float edge = length(q);
                float r = edge * _CoronaSize;      // 以太陽半徑為單位，太陽表面 r = 1
                float2 dir = q / max(edge, 1e-5);
                float h = max(r - 1.0, 0.0);       // 離太陽表面的高度
                float t = _Time.y * _FlowSpeed;

                // 光芒：角度用 (cos, sin) 取樣（沒有接縫），徑向用 log(r) 讓靠近表面的細節較密，並隨時間往外流
                float radial = log(max(r, 1.0)) * 3.0 - t;
                float rays  = sunFbm(float3(dir * _RayFrequency, radial));
                float rays2 = sunFbm(float3(dir * _RayFrequency * 2.3 + 11.0, radial * 1.7 - t * 0.5));
                float filaments = pow(saturate(rays * 0.65 + rays2 * 0.5), _RaySharpness) * 3.0;

                // 外輪廓也稍微擾動，日冕看起來不是正圓
                float wobble = sunFbm(float3(dir * 2.0, t * 0.3)) * 0.8 + 0.6;
                float glow = exp(-h * _Falloff / wobble);

                float rim = exp(-h * 18.0);        // 緊貼表面的亮環
                float fadeEdge = smoothstep(1.0, 0.75, edge);   // Quad 邊緣淡出，看不到方框

                float3 col = lerp(_OuterColor.rgb, _InnerColor.rgb, saturate(glow * 1.3));
                float3 result = col * (glow * (0.35 + filaments) + rim * 1.5) * fadeEdge * _Intensity;
                return float4(result, 1);
            }
            ENDCG
        }
    }
}
