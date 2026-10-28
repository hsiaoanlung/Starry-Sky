// 軌跡（TrailRenderer）用的發光線 Shader。
// 加法混合（Blend One One）：線只會讓畫面「變亮」，不會蓋掉後面的行星或星空，看起來像淡淡的光痕。
// 沿軌跡的位置由頂點顏色的 alpha 提供：TrailVisual 把 alpha 設成頭端 1 → 尾端 0，這裡換算成 t = 0（頭）→ 1（尾）。
// 沒有 LightMode 標籤，URP 與 Built-in 都能畫。
Shader "StarrySky/Trail"
{
    Properties
    {
        _Intensity        ("亮度（越大越亮；最亮的顏色 × 亮度保持在 1 以下，才不會觸發 Bloom）", Range(0, 2)) = 0.5
        _HeadFade         ("頭端淡入長度（佔軌跡比例，越大星球後面空出越多）", Range(0, 0.3)) = 0.04
        _TailPower        ("尾端淡出曲線（越大尾巴消失得越快；1 = 線性）", Range(0.5, 4)) = 1.5
    }
    SubShader
    {
        // 透明物件：在不透明物件之後畫；不寫深度，線彼此重疊時不會互相擋住
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Intensity;
            float _HeadFade;
            float _TailPower;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float t = 1 - i.color.a;                              // 0 = 頭（星球的位置），1 = 尾
                float head = smoothstep(0, max(_HeadFade, 1e-4), t);  // 星球正後方先空一小段，線不會黏在星球上
                float tail = pow(saturate(1 - t), _TailPower);        // 越舊越暗
                // 加法混合：輸出的顏色直接加到畫面上，所以「透明」= 輸出黑色，不需要 alpha
                return float4(i.color.rgb * head * tail * _Intensity, 0);
            }
            ENDCG
        }
    }
}
