// 行星 Shader 共用：光的方向從太陽來。
// SunVisual 每幀設定全域 _StarrySkySunPos（xyz = 太陽位置，w = 1 有效）；
// 沒有太陽時（單獨預覽 Prefab）改用材質上的 _LightDir。
#ifndef PLANET_LIGHTING_INCLUDED
#define PLANET_LIGHTING_INCLUDED

float4 _StarrySkySunPos;
float4 _LightDir;

float3 StarrySkyLightDir(float3 worldPos)
{
    return _StarrySkySunPos.w > 0.5
        ? normalize(_StarrySkySunPos.xyz - worldPos)
        : normalize(_LightDir.xyz);
}

#endif
