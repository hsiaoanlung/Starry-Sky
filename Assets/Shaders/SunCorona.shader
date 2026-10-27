// 日冕（體積版）：畫在包住太陽的一顆大球（半徑 = _CoronaSize 倍太陽半徑）上，從任何角度看都正確。
// 每個像素從相機射出一條光線，在球殼內等距取樣 _Steps 次，把每一點的發光量加起來（ray marching）。
// 發光量只跟「3D 位置」有關：亮度隨離表面的高度指數衰減，光芒沿著從太陽中心往外的方向延伸、往外流動。
// 只畫球的背面（Cull Front），所以相機進到日冕裡面也看得到；被太陽擋住的部分由深度測試去掉。Additive 混合。
Shader "StarrySky/SunCorona"
{
    Properties
    {
        [HDR] _InnerColor ("內圈顏色", Color) = (1.2, 0.8, 0.35, 1)
        [HDR] _OuterColor ("外圈顏色", Color) = (0.6, 0.15, 0.03, 1)
        _CoronaSize       ("球殼半徑（以太陽半徑為單位，由 SunVisual 設定）", Float) = 4.0
        _Falloff          ("衰減速度（越大日冕越短）", Float) = 2.2
        _RayFrequency     ("光芒數量（越大光芒越多越細）", Float) = 6.0
        _RaySharpness     ("光芒銳利度（越大光芒越細、縫隙越暗）", Float) = 4.0
        _FlowSpeed        ("往外流動速度", Float) = 0.25
        _Density          ("體積密度（越大整體越亮）", Float) = 0.3
        _RimStrength      ("貼表面亮環強度", Float) = 1.5
        [IntRange] _Steps ("取樣步數（越多越細緻、越耗效能）", Range(8, 64)) = 32
        _Intensity        ("整體亮度", Float) = 1.0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One      // Additive：疊加發光
        ZWrite Off
        Cull Front         // 畫背面：相機在球外、球內都看得到

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "SunNoise.cginc"

            float4 _InnerColor, _OuterColor;
            float _CoronaSize, _Falloff, _RayFrequency, _RaySharpness, _FlowSpeed;
            float _Density, _RimStrength, _Steps, _Intensity;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 objPos : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.objPos = v.vertex.xyz;
                return o;
            }

            // 一層光芒：在「方向 × 頻率」上取樣噪聲。頻率隨 s（≈ log r）慢慢變，噪聲沿徑向被拉長 → 放射狀細絲
            float rayLayer(float3 d, float s, float seed)
            {
                float a = sunFbmOct(d * (_RayFrequency + s) + seed, 4);
                float b = sunFbmOct(d * (_RayFrequency * 2.3 + 1.7 * s) + 11.0 + seed, 4);
                return a * 0.65 + b * 0.5;
            }

            // 空間中一點的發光量。p 以太陽半徑為單位、太陽中心為原點
            float3 coronaEmission(float3 p, float t)
            {
                float r = length(p);
                float3 d = p / max(r, 1e-5);
                float h = max(r - 1.0, 0.0);           // 離太陽表面的高度
                float L = log(max(r, 1.0)) * 3.0;      // 靠近表面細節較密、往外拉長

                // 往外流動：s = L - 相位。相位只在 0~1 之間循環（不然光芒會越來越細），
                // 兩組錯開半圈的相位交叉淡入淡出，看不到跳回 0 的那一下（flow map 的常見技巧）
                float ph0 = frac(t);
                float ph1 = frac(t + 0.5);
                float w0 = 1.0 - abs(2.0 * ph0 - 1.0);
                float rays = rayLayer(d, L - ph0, 0.0) * w0 + rayLayer(d, L - ph1, 37.0) * (1.0 - w0);
                float filaments = pow(saturate(rays), _RaySharpness) * 3.0;

                // 外輪廓依方向擾動，日冕不是正圓
                float wobble = sunFbmOct(d * 2.0 + float3(0, 0, t * 0.3), 3) * 0.8 + 0.6;
                float glow = exp(-h * _Falloff / wobble);

                float fadeEdge = smoothstep(1.0, 0.75, r / _CoronaSize);   // 接近球殼邊緣淡出，看不到球的輪廓

                float3 col = lerp(_OuterColor.rgb, _InnerColor.rgb, saturate(glow * 1.3));
                return col * glow * (0.35 + filaments) * fadeEdge;
            }

            float4 frag(v2f i) : SV_Target
            {
                // 物件空間的球半徑 0.5 = _CoronaSize 倍太陽半徑 → 乘上 scale 換成「太陽半徑」單位
                float scale = 2.0 * _CoronaSize;
                float3 ro = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz * scale;   // 光線起點：相機
                float3 rd = normalize(i.objPos * scale - ro);                                       // 光線方向

                // 光線 ro + rd·t 和半徑 R 的球相交：t² + 2bt + (c - R²) = 0
                float b = dot(ro, rd);
                float c = dot(ro, ro);
                float sq = sqrt(max(b * b - (c - _CoronaSize * _CoronaSize), 0.0));
                float t0 = max(-b - sq, 0.0);          // 進入球殼（相機在裡面時從 0 開始）
                float t1 = -b + sq;                    // 離開球殼
                float discSun = b * b - (c - 1.0);
                if (discSun > 0.0) t1 = clamp(-b - sqrt(discSun), t0, t1);   // 碰到太陽表面就停

                // 等距取樣，起點用像素座標做一點抖動（dither），步數少時不會出現一圈圈的條紋
                float ds = (t1 - t0) / _Steps;
                float jitter = sunHash(float3(i.pos.xy * 0.37, 1.0));
                float t = _Time.y * _FlowSpeed;
                float3 sum = 0;
                [loop] for (int k = 0; k < (int)_Steps; k++)
                    sum += coronaEmission(ro + rd * (t0 + (k + jitter) * ds), t);
                sum *= ds * _Density;

                // 貼表面的亮環太薄（約 1/18 太陽半徑），取樣會漏掉 → 直接用「光線離中心最近的距離」算
                float tc = -b;                                           // 最近點的位置
                float closest = tc > 0.0 ? sqrt(max(c - b * b, 0.0)) : sqrt(c);   // 最近點在相機後面就用相機本身
                sum += _InnerColor.rgb * exp(-max(closest - 1.0, 0.0) * 18.0) * _RimStrength;

                return float4(sum * _Intensity, 1);
            }
            ENDCG
        }
    }
}
