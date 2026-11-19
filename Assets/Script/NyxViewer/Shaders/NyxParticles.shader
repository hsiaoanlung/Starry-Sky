// Procedural additive billboards for Nyx dark-matter particles.
// 6 vertices per particle (two triangles), positions read from StructuredBuffers and
// interpolated between snapshot A and B with periodic wrapping.
// Works in the Built-in pipeline and URP. Requires compute-capable GPU (DX11+/Vulkan/Metal).
Shader "Nyx/Particles"
{
    Properties
    {
        _Color ("Color", Color) = (0.55, 0.7, 1, 1)
        _PointSize ("Point radius (world units)", Float) = 0.03
        _Intensity ("Intensity", Float) = 0.12
        _LUT ("Speed colormap (256x1)", 2D) = "white" {}
        _ColorBySpeed ("Color by speed (0/1)", Float) = 0
        _SpeedMax ("Speed max (km/s)", Float) = 600
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One      // additive glow
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            StructuredBuffer<float3> _PosA;
            StructuredBuffer<float3> _PosB;
            StructuredBuffer<float3> _VelA;
            StructuredBuffer<float3> _VelB;
            float4x4 _BoxToWorld;
            float _Blend, _PointSize, _Intensity, _ColorBySpeed, _SpeedMax;
            float4 _Color;
            sampler2D _LUT;

            static const float2 kCorners[6] =
            {
                float2(-1, -1), float2( 1, -1), float2( 1,  1),
                float2(-1, -1), float2( 1,  1), float2(-1,  1)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                float3 col : TEXCOORD1;
            };

            v2f vert (uint vid : SV_VertexID)
            {
                uint pid = vid / 6;
                float2 corner = kCorners[vid % 6];

                // periodic box: take the shortest path between A and B
                float3 a = _PosA[pid];
                float3 d = _PosB[pid] - a;
                d -= round(d);
                float3 p = frac(a + d * _Blend);

                float4 wp = mul(_BoxToWorld, float4(p - 0.5, 1));
                float4 vp = mul(UNITY_MATRIX_V, wp);
                vp.xy += corner * _PointSize;          // camera-facing billboard

                v2f o;
                o.pos = mul(UNITY_MATRIX_P, vp);
                o.uv = corner;

                float3 col = _Color.rgb;
                if (_ColorBySpeed > 0.5)
                {
                    float speed = length(lerp(_VelA[pid], _VelB[pid], _Blend));
                    col = tex2Dlod(_LUT, float4(saturate(speed / _SpeedMax), 0.5, 0, 0)).rgb;
                }
                o.col = col * _Intensity;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                if (r2 > 1) discard;
                float f = 1 - r2;
                return float4(i.col * f * f, 0);
            }
            ENDCG
        }
    }
    Fallback Off
}
