// 程序化星空天空盒：三層星星（亮 / 中 / 暗）+ 銀河。全部在 Shader 裡依方向算出來，不用貼圖。
// 星星的大小用「一個像素對應的角度」決定，所以不管視野多寬（廣角）或多窄（望遠鏡）都是清楚的小點。
// 天空盒在無限遠：只跟相機的「方向」有關，跟相機的位置無關（從水星或海王星看，星空都一樣）。
Shader "StarrySky/Starfield"
{
    Properties
    {
        _Exposure        ("整體亮度", Range(0, 3)) = 1.0
        _StarDensity     ("星星數量（越大越多）", Range(0, 2)) = 1.0
        _StarSize        ("星星大小（像素）", Range(0.3, 2)) = 0.6
        _MilkyWay        ("銀河亮度（0 = 關）", Range(0, 3)) = 1.0
        _GalacticTilt    ("銀河面和軌道面的夾角（度）", Range(0, 90)) = 60
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "SunNoise.cginc"

            float _Exposure, _StarDensity, _StarSize, _MilkyWay, _GalacticTilt;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;   // 天空盒網格的頂點位置就是方向
                return o;
            }

            // 方向 → 立方體的哪一面 + 那一面上的座標 uv（-1 ~ 1）
            void cubeFace(float3 d, out float face, out float2 uv)
            {
                float3 a = abs(d);
                if (a.x >= a.y && a.x >= a.z) { face = d.x > 0 ? 0 : 1; uv = d.yz / a.x; }
                else if (a.y >= a.z)          { face = d.y > 0 ? 2 : 3; uv = d.xz / a.y; }
                else                          { face = d.z > 0 ? 4 : 5; uv = d.xy / a.z; }
            }

            // 反過來：立方體的某一面 + uv → 方向（cubeFace 的反函數）
            float3 faceDir(float face, float2 uv)
            {
                if (face < 0.5) return float3( 1, uv.x, uv.y);
                if (face < 1.5) return float3(-1, uv.x, uv.y);
                if (face < 2.5) return float3(uv.x,  1, uv.y);
                if (face < 3.5) return float3(uv.x, -1, uv.y);
                if (face < 4.5) return float3(uv.x, uv.y,  1);
                return float3(uv.x, uv.y, -1);
            }

            // 一層星星：把立方體每一面切成 cells × cells 格，每格最多一顆星，位置、亮度、顏色都由格子編號的 hash 決定
            float3 starLayer(float3 d, float face, float2 uv, float cells, float density,
                             float minB, float maxB, float brightPow, float seed, float sigma)
            {
                float2 id = floor((uv * 0.5 + 0.5) * cells);
                float3 h = float3(id, face * 7.0 + seed);
                if (sunHash(h) > density) return 0;                           // 這一格沒有星星

                // 星星放在格子中央 60% 的範圍，離邊界留空，光點才不會被格線切掉
                float2 jitter = float2(sunHash(h + 17.3), sunHash(h + 41.9));
                float2 starUV = (id + 0.2 + 0.6 * jitter) / cells * 2.0 - 1.0;
                float3 starDir = normalize(faceDir(face, starUV));

                float ang = length(d - starDir);                              // 和星星的夾角（弧度，角度很小時弦長 ≈ 弧長）
                float shape = exp(-ang * ang / (2.0 * sigma * sigma));        // 高斯光點，寬度 = 幾個像素

                float b = lerp(minB, maxB, pow(sunHash(h + 63.1), brightPow)); // 大多數暗、少數很亮
                float t = sunHash(h + 88.7);                                  // 顏色：恆星表面溫度
                float3 col = t < 0.15 ? float3(1.0, 0.7, 0.45)                // 橘紅（低溫，像參宿四）
                           : t < 0.35 ? float3(1.0, 0.92, 0.8)                // 黃白（像太陽）
                           : t < 0.85 ? float3(0.95, 0.97, 1.0)               // 白
                           :            float3(0.7, 0.8, 1.0);                // 藍白（高溫，像參宿七）
                return col * b * shape;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);

                // 一個像素對應的角度：2·tan(視野/2) / 螢幕高度。unity_CameraProjection._m11 = 1 / tan(視野/2)
                float pixelAngle = 2.0 / (unity_CameraProjection._m11 * _ScreenParams.y);
                float sigma = pixelAngle * _StarSize;

                // 銀河：一條繞著天球的帶子。銀河面的法線（銀極）往 x 軸傾斜 _GalacticTilt 度
                float tilt = radians(_GalacticTilt);
                float3 pole = float3(0, cos(tilt), sin(tilt));
                float lat = dot(d, pole);                                    // 離銀河面多遠（sin 銀緯）
                float band = exp(-lat * lat / (2.0 * 0.12 * 0.12));
                float3 galCenter = float3(1, 0, 0);                          // 銀河中心的方向（和 pole 垂直）
                float bulge = exp(-dot(d - galCenter, d - galCenter) / 0.25);

                float clouds = smoothstep(0.35, 0.8, sunFbmOct(d * 6.0, 5));                  // 一團一團的星雲
                float dust = smoothstep(0.45, 0.7, sunFbmOct(d * 12.0 + 7.0, 4))
                           * exp(-lat * lat / (2.0 * 0.04 * 0.04));                           // 中央的暗塵帶
                float3 mwColor = lerp(float3(0.55, 0.6, 0.8), float3(1.0, 0.85, 0.65), bulge); // 外側偏藍、中心偏黃
                float3 milky = mwColor * band * (0.035 * clouds + 0.06 * bulge) * (1.0 - 0.7 * dust) * _MilkyWay;

                float face; float2 uv;
                cubeFace(d, face, uv);
                float3 stars = 0;
                stars += starLayer(d, face, uv,  40, 0.08 * _StarDensity, 0.30, 1.40, 6.0,  0.0, sigma);   // 亮星（約 800 顆）
                stars += starLayer(d, face, uv, 110, 0.20 * _StarDensity, 0.08, 0.45, 3.0, 20.0, sigma);   // 中等
                stars += starLayer(d, face, uv, 200, 0.25 * _StarDensity * (0.4 + 1.6 * band),             // 暗星，銀河裡比較多
                                   0.02, 0.12, 1.0, 40.0, sigma);

                return float4((stars + milky) * _Exposure, 1);
            }
            ENDCG
        }
    }
}
