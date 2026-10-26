"""
程序化產生行星貼圖（地球以外的七顆行星）。地球見 earth_texture_gen.py。

輸出到 Assets/Textures/<行星>/：
  <行星>Albedo.png  RGB = 表面顏色，A = 高度（岩石行星做凹凸用；氣態行星固定 128）
  SaturnRings.png    土星環的徑向剖面：RGB = 顏色，A = 不透明度（橫軸 = 半徑 1.1 ~ 2.4 倍土星半徑）

用法（在專案根目錄）：
  python Assets/Docs/planet_texture_gen.py                    # 全部，2048 x 1024
  python Assets/Docs/planet_texture_gen.py Mars Jupiter       # 只產生指定的行星
  python Assets/Docs/planet_texture_gen.py --width 4096 Mars  # 指定寬度
"""
import os
import sys
import numpy as np
from PIL import Image

from procedural_noise import (value_noise, fbm, fbm_vec, ridged, smoothstep,
                              weighted_quantile, mix, sphere_grid)

OUT_ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Textures")

# 土星環貼圖的半徑範圍（以土星半徑為單位），必須和 Planet / PlanetRing Shader 的 _RingInner / _RingOuter 一致
RING_INNER, RING_OUTER = 1.1, 2.4


# ---------- 共用 ----------

def save_albedo(name, width, rgb, height=None):
    """rgb: (N,3) 0~1；height: (N,) 0~1 或 None（存成 128）。"""
    h = width // 2
    a = np.full(rgb.shape[0], 0.5) if height is None else height
    rgba = np.concatenate([np.clip(rgb, 0, 1), np.clip(a, 0, 1)[:, None]], axis=1)
    out = os.path.join(OUT_ROOT, name)
    os.makedirs(out, exist_ok=True)
    Image.fromarray((rgba * 255 + 0.5).reshape(h, width, 4).astype(np.uint8), "RGBA").save(
        os.path.join(out, f"{name}Albedo.png"))


def normalize01(x):
    return (x - x.min()) / (x.max() - x.min())


def random_unit_vectors(rng, n, lat_weight=None):
    """球面上均勻分布的隨機點；lat_weight(lat_rad) 可以讓某些緯度比較多（用拒絕取樣）。"""
    out = []
    while len(out) < n:
        v = rng.normal(size=3)
        v /= np.linalg.norm(v)
        if lat_weight is None or rng.random() < lat_weight(np.arcsin(v[1])):
            out.append(v)
    return np.array(out)


def add_craters(pts, lat, width, height_map, albedo_mod, rng, count, rmin, rmax,
                depth=1.0, fresh_fraction=0.08, lat_weight=None):
    """
    加隕石坑。半徑（弧度）符合 N(>r) ∝ r^-2 的冪次分布：小坑很多、大坑很少。
    坑的剖面：碗狀凹陷 + 隆起的坑緣 + 大坑的中央峰。少數「新鮮」的坑比較亮，還有放射紋。
    只處理坑所在的那幾列像素，所以幾千個坑也很快。
    """
    h = width // 2
    centers = random_unit_vectors(rng, count, lat_weight)
    u = rng.random(count)
    radii = rmin * (1.0 - u * (1.0 - (rmin / rmax) ** 2)) ** -0.5
    row_lat = (0.5 - (np.arange(h) + 0.5) / h) * np.pi
    for c, r in zip(centers, radii):
        clat = np.arcsin(c[1])
        reach = r * (3.0 if r > rmin * 4 else 2.0)
        rows = np.nonzero(np.abs(row_lat - clat) < reach)[0]
        if rows.size == 0:
            continue
        sl = slice(rows[0] * width, (rows[-1] + 1) * width)
        p = pts[:, sl]
        ang = np.arccos(np.clip(c @ p, -1.0, 1.0))
        x = ang / r
        near = x < 3.0
        if not near.any():
            continue
        xs = x[near]
        d = depth * (r / rmax) ** 0.6                             # 大坑比較深，但不是線性
        prof = np.where(xs < 1.0, (xs * xs - 1.0) * d, 0.0)        # 碗
        prof += 0.3 * d * np.exp(-((xs - 1.0) / 0.2) ** 2)         # 坑緣
        if r > rmin * 6:
            prof += 0.35 * d * np.exp(-(xs / 0.18) ** 2)           # 中央峰
        idx = np.arange(sl.start, sl.stop)[near]
        height_map[idx] += prof
        albedo_mod[idx] += np.where(xs < 1.0, -0.03, 0.0)          # 坑底稍暗
        if rng.random() < fresh_fraction:
            # 新鮮的坑：周圍亮、加放射紋（用方位角的噪聲）
            t1 = np.cross(c, [0.0, 1.0, 0.0])
            if np.linalg.norm(t1) < 1e-6:
                t1 = np.array([1.0, 0.0, 0.0])
            t1 /= np.linalg.norm(t1)
            t2 = np.cross(c, t1)
            az = np.arctan2(t2 @ p[:, near], t1 @ p[:, near])
            ray_pts = np.stack([np.cos(az) * 6.0, np.sin(az) * 6.0, np.full_like(az, rng.random() * 100)])
            rays = smoothstep(0.55, 0.8, value_noise(ray_pts, int(rng.integers(1 << 20))))
            albedo_mod[idx] += 0.18 * np.exp(-(xs / 1.2) ** 2) + 0.12 * rays * np.exp(-xs / 1.5) * (xs > 0.8)


def band_color(lat_deg, stops):
    """依緯度在 (緯度, 顏色) 的節點之間插值。"""
    la = np.array([s[0] for s in stops], float)
    cols = np.array([s[1] for s in stops], float)
    return np.stack([np.interp(lat_deg, la, cols[:, k]) for k in range(3)], axis=1)


def oval(lon, lat, lon0_deg, lat0_deg, a_deg, b_deg):
    """以 (lon0, lat0) 為中心的橢圓局部座標（a = 東西半軸、b = 南北半軸，單位：度）。回傳 (x, y, r)。"""
    dlon = (lon - np.radians(lon0_deg) + np.pi) % (2 * np.pi) - np.pi
    x = dlon * np.cos(lat) / np.radians(a_deg)
    y = (lat - np.radians(lat0_deg)) / np.radians(b_deg)
    return x, y, np.sqrt(x * x + y * y)


def zonal(pts, k):
    """南北方向頻率放大 k 倍：圖案沿東西向拉長，像被緯向風吹過。"""
    return pts * np.array([[1.0], [k], [1.0]])


def gas_bands(pts, lat, stops, turbulence_deg, stretch, seed, streak=0.15):
    """氣態行星的雲帶：用噪聲擾動緯度，再查顏色表；最後加東西向的細條紋。"""
    lat_deg = np.degrees(lat)
    z = zonal(pts, stretch)
    warp = fbm_vec(z * 1.5, 4, seed)
    t = fbm(z * 2.0 + 1.5 * warp, 6, seed + 10) - 0.5
    t2 = fbm(z * 6.0 + 2.0 * warp, 4, seed + 20) - 0.5
    lat_t = lat_deg + turbulence_deg * (2.0 * t + 0.8 * t2)
    col = band_color(lat_t, stops)
    s = fbm(zonal(pts, stretch * 4) * 4.0 + warp, 5, seed + 30)
    col *= (1.0 - streak + 2 * streak * s)[:, None]
    return col, lat_t, warp


# ---------- 各行星 ----------

def mercury(width, rng):
    pts, lon, lat, area = sphere_grid(width)
    hmap = 0.15 * fbm(pts * 3.0, 6, 11)
    amod = np.zeros_like(hmap)
    add_craters(pts, lat, width, hmap, amod, rng, 12, 0.08, 0.30, depth=0.6)      # 盆地
    add_craters(pts, lat, width, hmap, amod, rng, 400, 0.015, 0.08, depth=0.5)
    add_craters(pts, lat, width, hmap, amod, rng, 5000, 0.004, 0.015, depth=0.25)
    base = 0.47 + 0.12 * (fbm(pts * 2.0, 5, 12) - 0.5) + 0.06 * (fbm(pts * 12.0, 4, 13) - 0.5)
    light = base + amod
    rgb = light[:, None] * np.array([1.0, 0.96, 0.90])
    save_albedo("Mercury", width, rgb, normalize01(hmap))


def venus(width, rng):
    pts, lon, lat, area = sphere_grid(width)
    lat_deg = np.degrees(lat)
    # 濃厚雲層：東西向拉長的條紋，加上隨經度變化的 Y 形彎曲
    bend = 14.0 * np.cos(lon - 0.5) * np.cos(lat) ** 2
    z = zonal(pts, 5.0)
    warp = fbm_vec(z * 1.2, 4, 21)
    t = fbm(z * 2.5 + 1.8 * warp, 6, 22)
    bands = 0.5 + 0.5 * np.sin(np.radians(lat_deg + bend) * 7.0 + 3.0 * (t - 0.5))
    v = 0.6 * t + 0.4 * bands
    rgb = mix(np.array([0.80, 0.68, 0.46]), np.array([0.96, 0.90, 0.74]), v)
    rgb = mix(rgb, np.array([0.86, 0.80, 0.66]), smoothstep(55, 80, np.abs(lat_deg)) * 0.6)   # 極區較灰
    save_albedo("Venus", width, rgb)


def mars(width, rng):
    pts, lon, lat, area = sphere_grid(width)
    lat_deg, lon_deg = np.degrees(lat), np.degrees(lon)
    n = fbm(pts * 2.0, 6, 31)
    # 南北二分：北半球低地、南半球高地
    hmap = 0.25 * smoothstep(20, -10, lat_deg + 15 * (fbm(pts * 1.5, 4, 32) - 0.5)) + 0.25 * n
    # 塔爾西斯高原、奧林帕斯山、水手號峽谷、希臘盆地
    _, _, r = oval(lon, lat, -110, 0, 35, 30);   hmap += 0.30 * np.exp(-r * r)
    _, _, r = oval(lon, lat, -134, 18, 6, 6);    hmap += 0.60 * np.exp(-(r * 1.2) ** 2)
    for lo, la in [(-112, -1), (-104, 9), (-102, 1)]:                          # 三座盾狀火山
        _, _, r = oval(lon, lat, lo, la, 3, 3);  hmap += 0.35 * np.exp(-r * r)
    x, y, _ = oval(lon, lat, -70, -8, 35, 1.6)
    hmap -= 0.35 * np.exp(-y * y) * smoothstep(1.1, 0.8, np.abs(x)) * (0.7 + 0.6 * fbm(pts * 20, 3, 33))
    _, _, r = oval(lon, lat, 70, -42, 22, 18);   hmap -= 0.35 * smoothstep(1.2, 0.3, r)
    amod = np.zeros_like(hmap)
    south = lambda la: 0.25 + 0.75 * (la < np.radians(10))
    add_craters(pts, lat, width, hmap, amod, rng, 120, 0.02, 0.10, depth=0.35, fresh_fraction=0.0, lat_weight=south)
    add_craters(pts, lat, width, hmap, amod, rng, 1500, 0.004, 0.02, depth=0.15, fresh_fraction=0.0, lat_weight=south)

    rust = np.array([0.74, 0.43, 0.25])
    dark = np.array([0.38, 0.23, 0.15])
    dust = np.array([0.86, 0.62, 0.42])
    albedo_n = fbm(pts * 2.5 + 0.5 * fbm_vec(pts * 2.0, 3, 34), 6, 35)
    rgb = mix(rust, dark, smoothstep(0.52, 0.66, albedo_n))
    rgb = mix(rgb, dust, smoothstep(0.45, 0.32, albedo_n) * 0.7)
    rgb *= (0.92 + 0.16 * fbm(pts * 25, 4, 36) + amod * 2)[:, None]
    # 極冠（北大、南小），邊緣有螺旋狀的噪聲
    edge = (fbm(pts * 8.0, 4, 37) - 0.5) * 8
    cap = np.where(lat > 0, smoothstep(77, 80, lat_deg + edge), smoothstep(81, 84, -lat_deg + edge))
    rgb = mix(rgb, np.array([0.95, 0.93, 0.90]), cap)
    save_albedo("Mars", width, rgb, normalize01(hmap))


JUPITER_STOPS = [
    (-90, (0.52, 0.48, 0.45)), (-62, (0.55, 0.50, 0.46)), (-52, (0.70, 0.62, 0.53)),
    (-46, (0.60, 0.50, 0.41)), (-40, (0.80, 0.72, 0.60)), (-35, (0.64, 0.50, 0.39)),
    (-29, (0.88, 0.80, 0.67)), (-21, (0.92, 0.86, 0.74)), (-17, (0.66, 0.46, 0.32)),
    (-10, (0.72, 0.52, 0.36)), (-6, (0.93, 0.87, 0.75)), (0, (0.90, 0.79, 0.60)),
    (6, (0.93, 0.87, 0.75)), (9, (0.62, 0.42, 0.29)), (17, (0.70, 0.50, 0.34)),
    (20, (0.90, 0.84, 0.72)), (26, (0.86, 0.78, 0.65)), (29, (0.64, 0.50, 0.39)),
    (34, (0.84, 0.76, 0.63)), (40, (0.68, 0.58, 0.48)), (46, (0.80, 0.72, 0.62)),
    (52, (0.64, 0.56, 0.49)), (62, (0.55, 0.50, 0.46)), (90, (0.52, 0.48, 0.45)),
]


def jupiter(width, rng):
    pts, lon, lat, area = sphere_grid(width)
    col, lat_t, warp = gas_bands(pts, lat, JUPITER_STOPS, 4.5, 6.0, 41, streak=0.2)
    # 帶與帶交界的渦流：用更強的座標扭曲取樣，讓明暗不規則
    eddy = fbm(zonal(pts, 3.0) * 6.0 + 3.0 * warp, 5, 43)
    col *= (0.88 + 0.24 * eddy)[:, None]
    # 大紅斑：橢圓內部用旋轉後的座標取樣噪聲，看起來像在打轉
    x, y, r = oval(lon, lat, 40, -22, 12, 6)
    ang = np.arctan2(y, x) + 2.5 * np.clip(1 - r, 0, 1)
    swirl = np.stack([np.cos(ang) * r * 3, np.sin(ang) * r * 3, np.full_like(r, 7.0)])
    inner = fbm(swirl, 4, 42)
    grs = mix(np.array([0.70, 0.33, 0.20]), np.array([0.85, 0.52, 0.36]), inner)
    col = mix(col, np.array([0.95, 0.90, 0.80]), np.exp(-((r - 1.15) / 0.15) ** 2) * 0.6)   # 外圈淺色環
    col = mix(col, grs, smoothstep(1.0, 0.75, r))
    # 南溫帶的白色小橢圓
    for lo in (120, 160, 205, 250):
        _, _, r = oval(lon, lat, lo, -33 + rng.normal() * 0.8, 3.0, 2.0)
        col = mix(col, np.array([0.96, 0.94, 0.90]), smoothstep(1.0, 0.5, r))
    save_albedo("Jupiter", width, col)


SATURN_STOPS = [
    (-90, (0.70, 0.66, 0.55)), (-65, (0.76, 0.70, 0.55)), (-50, (0.84, 0.76, 0.56)),
    (-40, (0.80, 0.70, 0.50)), (-30, (0.88, 0.80, 0.60)), (-20, (0.82, 0.72, 0.52)),
    (-10, (0.92, 0.85, 0.66)), (0, (0.94, 0.88, 0.70)), (10, (0.92, 0.85, 0.66)),
    (20, (0.82, 0.72, 0.52)), (30, (0.88, 0.80, 0.60)), (40, (0.80, 0.70, 0.50)),
    (50, (0.82, 0.76, 0.60)), (65, (0.66, 0.68, 0.66)), (90, (0.58, 0.62, 0.66)),
]


def saturn(width, rng):
    pts, lon, lat, area = sphere_grid(width)
    col, _, _ = gas_bands(pts, lat, SATURN_STOPS, 1.2, 8.0, 51, streak=0.06)
    save_albedo("Saturn", width, col)
    saturn_rings(rng)


def saturn_rings(rng, samples=2048):
    """土星環的徑向剖面（寬 2048、高 4）。各環的位置（土星半徑）：D、C、B、卡西尼縫、A（含恩克縫）、F。"""
    r = RING_INNER + (np.arange(samples) + 0.5) / samples * (RING_OUTER - RING_INNER)
    a = np.zeros(samples)
    col = np.zeros((samples, 3))
    def region(r0, r1, alpha, c, soft=0.004):
        w = smoothstep(r0 - soft, r0 + soft, r) * smoothstep(r1 + soft, r1 - soft, r)
        a[:] = a + w * alpha
        col[:] = col + w[:, None] * np.array(c)
    region(1.11, 1.236, 0.04, (0.55, 0.50, 0.46))                                    # D 環
    region(1.239, 1.527, 0.10 + 0.12 * smoothstep(1.24, 1.52, r), (0.58, 0.53, 0.48))  # C 環
    region(1.527, 1.95, 0.80 + 0.18 * smoothstep(1.53, 1.75, r), (0.88, 0.80, 0.66))   # B 環
    region(1.95, 2.025, 0.06, (0.55, 0.50, 0.46))                                     # 卡西尼縫
    region(2.025, 2.27, 0.62, (0.82, 0.76, 0.66))                                     # A 環
    region(2.32, 2.33, 0.35, (0.80, 0.76, 0.70), soft=0.002)                          # F 環
    a = np.where((r > 2.214) & (r < 2.222), 0.0, a)                                   # 恩克縫
    # 細環紋：多個頻率的 1D 噪聲
    p = np.stack([r * 300.0, np.zeros_like(r), np.zeros_like(r)])
    q = np.stack([r * 1200.0, np.full_like(r, 3.0), np.zeros_like(r)])
    a *= 0.75 + 0.35 * value_noise(p, 61) + 0.15 * (value_noise(q, 62) - 0.5)
    col *= (0.9 + 0.2 * value_noise(p * 0.5, 63))[:, None]
    rgba = np.concatenate([np.clip(col, 0, 1), np.clip(a, 0, 1)[:, None]], axis=1)
    img = np.repeat(rgba[None, :, :], 4, axis=0)
    out = os.path.join(OUT_ROOT, "Saturn")
    os.makedirs(out, exist_ok=True)
    Image.fromarray((img * 255 + 0.5).astype(np.uint8), "RGBA").save(os.path.join(out, "SaturnRings.png"))


def uranus(width, rng):
    pts, lon, lat, area = sphere_grid(width)
    lat_deg = np.degrees(lat)
    stops = [(-90, (0.62, 0.82, 0.86)), (-30, (0.60, 0.80, 0.85)), (0, (0.58, 0.79, 0.85)),
             (30, (0.61, 0.81, 0.86)), (55, (0.68, 0.86, 0.89)), (90, (0.74, 0.90, 0.92))]
    col, _, _ = gas_bands(pts, lat, stops, 2.0, 8.0, 71, streak=0.025)
    for lo, la in [(30, 35), (150, 28)]:                                   # 幾朵小亮雲
        _, _, r = oval(lon, lat, lo, la, 3, 1.5)
        col = mix(col, np.array([0.85, 0.95, 0.97]), smoothstep(1.0, 0.3, r) * 0.7)
    save_albedo("Uranus", width, col)


def neptune(width, rng):
    pts, lon, lat, area = sphere_grid(width)
    lat_deg = np.degrees(lat)
    stops = [(-90, (0.24, 0.38, 0.80)), (-60, (0.22, 0.36, 0.78)), (-45, (0.30, 0.47, 0.90)),
             (-25, (0.20, 0.33, 0.75)), (-10, (0.27, 0.44, 0.88)), (0, (0.25, 0.42, 0.86)),
             (15, (0.27, 0.44, 0.88)), (30, (0.21, 0.35, 0.77)), (50, (0.29, 0.46, 0.90)),
             (90, (0.24, 0.38, 0.80))]
    col, _, warp = gas_bands(pts, lat, stops, 2.5, 6.0, 81, streak=0.08)
    # 大黑斑 + 旁邊的白色伴雲
    _, _, r = oval(lon, lat, -60, -20, 10, 5)
    col = mix(col, np.array([0.10, 0.18, 0.48]), smoothstep(1.0, 0.6, r))
    _, _, r = oval(lon, lat, -58, -27, 7, 1.2)
    col = mix(col, np.array([0.92, 0.95, 1.0]), smoothstep(1.0, 0.2, r) * 0.9)
    # 高空的白色條狀雲（甲烷冰）
    streaks = fbm(zonal(pts, 7.0) * 4.0 + 1.5 * warp, 5, 82)
    belt = np.exp(-((np.abs(lat_deg) - 35) / 10) ** 2) + 0.5 * np.exp(-((lat_deg + 60) / 6) ** 2)
    col = mix(col, np.array([0.88, 0.92, 0.98]), smoothstep(0.6, 0.8, streaks) * belt * 0.85)
    save_albedo("Neptune", width, col)


PLANETS = {"Mercury": mercury, "Venus": venus, "Mars": mars, "Jupiter": jupiter,
           "Saturn": saturn, "Uranus": uranus, "Neptune": neptune}


def main():
    args = sys.argv[1:]
    width = 2048
    if "--width" in args:
        i = args.index("--width")
        width = int(args[i + 1])
        del args[i:i + 2]
    names = args or list(PLANETS)
    for name in names:
        print(f"產生 {name} ({width} x {width // 2}) ...")
        rng = np.random.default_rng(sum(map(ord, name)))   # 固定種子：每次產生的結果一樣
        PLANETS[name](width, rng)
    print(f"輸出到 {os.path.normpath(OUT_ROOT)}")


if __name__ == "__main__":
    main()
