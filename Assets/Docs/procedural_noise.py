"""
程序化貼圖共用的噪聲與工具函式（earth_texture_gen.py、planet_texture_gen.py 共用）。

所有噪聲都在 3D 空間取樣：把貼圖像素換算成球面上的點再取樣，貼圖左右接得起來、兩極不會拉扯。
"""
import numpy as np


# ---------- 噪聲 ----------

def hash3(ix, iy, iz, seed):
    """整數格點 → 0~1 的偽隨機值（同樣輸入永遠同樣輸出）。int64 溢位會自動 wrap，是刻意的。"""
    h = ix * np.int64(374761393) + iy * np.int64(668265263) + iz * np.int64(1440662683) + np.int64(seed * 144665)
    h = (h ^ (h >> 13)) * np.int64(1274126177)
    h = h ^ (h >> 16)
    return (h & 0xFFFFFF).astype(np.float64) / 0xFFFFFF


def value_noise(p, seed):
    """3D value noise，p 的形狀 (3, N)，回傳 (N,) 的 0~1。"""
    i = np.floor(p)
    f = p - i
    i = i.astype(np.int64)
    u = f * f * (3.0 - 2.0 * f)
    x, y, z = i
    c = {}
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                c[dx, dy, dz] = hash3(x + dx, y + dy, z + dz, seed)
    ux, uy, uz = u
    x00 = c[0, 0, 0] + (c[1, 0, 0] - c[0, 0, 0]) * ux
    x10 = c[0, 1, 0] + (c[1, 1, 0] - c[0, 1, 0]) * ux
    x01 = c[0, 0, 1] + (c[1, 0, 1] - c[0, 0, 1]) * ux
    x11 = c[0, 1, 1] + (c[1, 1, 1] - c[0, 1, 1]) * ux
    y0 = x00 + (x10 - x00) * uy
    y1 = x01 + (x11 - x01) * uy
    return y0 + (y1 - y0) * uz


# 每個 octave 轉一個角度，避免噪聲沿座標軸排列的痕跡
_ROT = np.array([[0.00, 0.80, 0.60],
                 [-0.80, 0.36, -0.48],
                 [-0.60, -0.48, 0.64]])


def fbm(p, octaves, seed, lacunarity=2.0, gain=0.5):
    """fBm：每層頻率 ×lacunarity、振幅 ×gain，正規化到約 0~1。"""
    total = np.zeros(p.shape[1])
    amp, norm = 0.5, 0.0
    q = p.copy()
    for k in range(octaves):
        total += amp * value_noise(q, seed + k * 31)
        norm += amp
        q = (_ROT @ q) * lacunarity
        amp *= gain
    return total / norm


def fbm_vec(p, octaves, seed):
    """三個不同種子的 fBm，當作 domain warping 的位移向量（中心化到 -0.5~0.5）。"""
    return np.stack([fbm(p, octaves, seed + s) - 0.5 for s in (101, 202, 303)])


def ridged(p, octaves, seed):
    """Ridged noise：1 - |2n-1|，產生山脊狀的尖峰。"""
    total = np.zeros(p.shape[1])
    amp, norm = 0.5, 0.0
    q = p.copy()
    for k in range(octaves):
        n = 1.0 - np.abs(2.0 * value_noise(q, seed + k * 17) - 1.0)
        total += amp * n * n
        norm += amp
        q = (_ROT @ q) * 2.0
        amp *= 0.5
    return total / norm


# ---------- 小工具 ----------

def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def weighted_quantile(values, weights, q):
    """面積加權的分位數（高緯度像素代表的面積小，要乘 cos(緯度)）。"""
    order = np.argsort(values)
    cw = np.cumsum(weights[order])
    return values[order][np.searchsorted(cw, q * cw[-1])]


def mix(a, b, t):
    """顏色插值：a、b 為 (3,) 或 (N,3)，t 為 (N,)。"""
    return a + (b - a) * t[:, None]


def sphere_grid(width):
    """等距長方投影的像素 → 球面點。回傳 (pts (3,N), 經度 rad, 緯度 rad, 面積權重 cos(緯度))。
    圖片第 0 列 = 北極（Unity 的 UV v=1 在上方）；+y 是北極。"""
    height = width // 2
    u = (np.arange(width) + 0.5) / width
    v = (np.arange(height) + 0.5) / height
    lon = (u * 2.0 - 1.0) * np.pi
    lat = (0.5 - v) * np.pi
    LON, LAT = np.meshgrid(lon, lat)
    LON, LAT = LON.ravel(), LAT.ravel()
    pts = np.stack([np.cos(LAT) * np.cos(LON), np.sin(LAT), np.cos(LAT) * np.sin(LON)])
    return pts, LON, LAT, np.cos(LAT)
