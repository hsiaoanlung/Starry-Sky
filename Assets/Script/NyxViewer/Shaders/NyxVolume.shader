// Front-to-back ray marching through a unit cube ([-0.5,0.5]^3 in object space).
// Two 3D textures (snapshot A and B) are blended with _Blend, then mapped through a 1D colormap.
// Works in the Built-in pipeline and URP (unlit, no lighting). Not for HDRP.
Shader "Nyx/Volume"
{
    Properties
    {
        _VolA ("Volume A", 3D) = "" {}
        _VolB ("Volume B", 3D) = "" {}
        _Blend ("Blend A->B", Range(0, 1)) = 0
        _LUT ("Colormap (256x1)", 2D) = "white" {}
        _Low ("Low threshold", Range(0, 1)) = 0.35
        _High ("High threshold", Range(0, 1)) = 1
        _Opacity ("Opacity", Range(0, 5)) = 1
        _Gamma ("Opacity gamma", Range(0.2, 4)) = 1.5
        _Brightness ("Brightness", Range(0, 5)) = 1.5
        _Steps ("Max steps", Range(16, 512)) = 160
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        // Draw back faces so the volume still renders when the camera is inside the cube.
        Cull Front
        ZWrite Off
        ZTest LEqual
        Blend One OneMinusSrcAlpha   // premultiplied alpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            sampler3D _VolA;
            sampler3D _VolB;
            sampler2D _LUT;
            float _Blend, _Low, _High, _Opacity, _Gamma, _Brightness, _Steps;

            struct v2f
            {
                float4 pos    : SV_POSITION;
                float3 objPos : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.objPos = v.vertex.xyz;
                return o;
            }

            // Ray / axis-aligned unit box intersection, returns (tNear, tFar)
            float2 BoxHit (float3 ro, float3 rd)
            {
                float3 inv = 1.0 / rd;
                float3 t0 = (-0.5 - ro) * inv;
                float3 t1 = ( 0.5 - ro) * inv;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                return float2(max(max(tmin.x, tmin.y), tmin.z), min(min(tmax.x, tmax.y), tmax.z));
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 ro = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
                float3 rd = normalize(i.objPos - ro);
                float2 t = BoxHit(ro, rd);
                t.x = max(t.x, 0.0);
                if (t.x >= t.y) discard;

                int steps = (int)_Steps;
                float dt = 1.7320508 / steps;              // cube diagonal / steps
                // per-pixel jitter hides banding
                float jitter = frac(sin(dot(i.pos.xy, float2(12.9898, 78.233))) * 43758.5453);
                float tt = t.x + dt * jitter;
                float range = max(_High - _Low, 1e-4);

                float4 acc = 0;
                [loop]
                for (int s = 0; s < 512; s++)
                {
                    if (s >= steps || tt > t.y || acc.a > 0.98) break;
                    float3 uvw = ro + rd * tt + 0.5;
                    float va = tex3Dlod(_VolA, float4(uvw, 0)).r;
                    float vb = tex3Dlod(_VolB, float4(uvw, 0)).r;
                    float d = saturate((lerp(va, vb, _Blend) - _Low) / range);
                    if (d > 0)
                    {
                        float3 c = tex2Dlod(_LUT, float4(d, 0.5, 0, 0)).rgb * _Brightness;
                        // opacity defined per 1/64 of the box so it does not depend on step count
                        float a = saturate(pow(d, _Gamma) * _Opacity * dt * 64.0);
                        acc.rgb += (1 - acc.a) * a * c;
                        acc.a   += (1 - acc.a) * a;
                    }
                    tt += dt;
                }
                return acc;
            }
            ENDCG
        }
    }
    Fallback Off
}
