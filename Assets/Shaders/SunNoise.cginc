// 太陽 Shader 共用的 3D 噪聲函式（value noise + fBm）
#ifndef SUN_NOISE_INCLUDED
#define SUN_NOISE_INCLUDED

float sunHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// 3D value noise，回傳 0~1
float sunNoise(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);   // smoothstep 插值，避免格線
    return lerp(lerp(lerp(sunHash(i + float3(0, 0, 0)), sunHash(i + float3(1, 0, 0)), f.x),
                     lerp(sunHash(i + float3(0, 1, 0)), sunHash(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(sunHash(i + float3(0, 0, 1)), sunHash(i + float3(1, 0, 1)), f.x),
                     lerp(sunHash(i + float3(0, 1, 1)), sunHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

// fBm：疊加 5 層頻率加倍、振幅減半的噪聲，回傳約 0~1
float sunFbm(float3 p)
{
    float sum = 0.0;
    float amp = 0.5;
    for (int k = 0; k < 5; k++)
    {
        sum += amp * sunNoise(p);
        p = p * 2.03 + float3(1.7, 9.2, 3.1);
        amp *= 0.5;
    }
    return sum / 0.96875;
}

// 可指定層數的 fBm（體積日冕每個像素要取樣很多次，用較少層數省效能），回傳約 0~1
float sunFbmOct(float3 p, int octaves)
{
    float sum = 0.0;
    float amp = 0.5;
    float norm = 0.0;
    for (int k = 0; k < octaves; k++)
    {
        sum += amp * sunNoise(p);
        norm += amp;
        p = p * 2.03 + float3(1.7, 9.2, 3.1);
        amp *= 0.5;
    }
    return sum / norm;
}

#endif
