"""
程序化產生 20 顆衛星的貼圖（月球、火衛、伽利略衛星、土衛、天衛、海衛一）。行星見 planet_texture_gen.py。

輸出到 Assets/Textures/Moons/<名稱>Albedo.png：RGB = 表面顏色，A = 高度（StarrySky/Planet 的凹凸用）。
經度慣例（貼圖座標，lon 0 在貼圖正中央）：
  lon 0    = 朝向行星的那一面（潮汐鎖定，永遠朝著行星）
  lon +90  = 「前導面」（公轉前進的方向）；lon -90 = 「後隨面」
很多衛星的兩個半球長得不一樣（例如土衛八前導面全黑），就是靠這個慣例擺位置。
存檔時左右翻轉（u → 1 - u）：Unity 內建球體的 u 從北極往下看是「順時針」增加，和一般地圖（往東 = 逆時針）相反，
翻過來貼上去才不會鏡像（2026-11-01 在 Unity 中由 TidalLock 的檢查發現）。所以存好的 PNG 裡 lon +90 在 1/4 處。

用法（在專案根目錄）：
  python Assets/Docs/moon_texture_gen.py               # 全部（月球 2048 x 1024，其他 1024 x 512）
  python Assets/Docs/moon_texture_gen.py Io Europa     # 只產生指定的衛星
  python Assets/Docs/moon_texture_gen.py --preview     # 只畫預覽圖（讀已產生的貼圖）
預覽圖：Temp/StarrySkyPreview/moons_preview.png（照 Planet.shader 的光照公式畫成球）
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

from procedural_noise import value_noise, fbm, fbm_vec, ridged, smoothstep, mix, sphere_grid
from planet_texture_gen import add_craters, oval, normalize01, zonal

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(HERE, "..", "Textures", "Moons")
PREVIEW = os.path.join(HERE, "..", "..", "Temp", "StarrySkyPreview", "moons_preview.png")
SMALL, LARGE = 1024, 2048     # 衛星在畫面上很小，1024 就夠；月球最常被近看，用 2048


# ---------- 共用的地形元素 ----------

def unit(lon_deg, lat_deg):
    lo, la = np.radians(lon_deg), np.radians(lat_deg)
    return np.array([np.cos(la) * np.cos(lo), np.sin(la), np.cos(la) * np.sin(lo)])


def angle_to(pts, c):
    """每個像素到球面上某一點 c 的角距離（弧度）。"""
    return np.arccos(np.clip(c @ pts, -1.0, 1.0))


def crater_at(pts, h, a, lon, lat, r, depth, albedo=0.0, peak=True):
    """在指定位置放一個大坑（有名字的那種）：碗 + 坑緣 + 中央峰；albedo 改變坑內亮度。"""
    x = angle_to(pts, unit(lon, lat)) / np.radians(r)
    h += np.where(x < 1, (x * x - 1) * depth, 0) + 0.3 * depth * np.exp(-((x - 1) / 0.15) ** 2)
    if peak:
        h += 0.4 * depth * np.exp(-(x / 0.12) ** 2)
    a += albedo * smoothstep(1.1, 0.8, x)


def rays(pts, lon, lat, r, length, seed):
    """新鮮撞擊坑的放射紋：沿方位角取噪聲 → 一條條往外的亮線，越遠越淡。回傳 0~1。"""
    c = unit(lon, lat)
    t1 = np.cross(c, [0.0, 1.0, 0.0]); t1 /= np.linalg.norm(t1)
    t2 = np.cross(c, t1)
    az = np.arctan2(t2 @ pts, t1 @ pts)
    d = angle_to(pts, c)
    q = np.stack([np.cos(az) * 9, np.sin(az) * 9, np.zeros_like(az) + seed])
    streak = smoothstep(0.5, 0.85, value_noise(q, seed))
    halo = np.exp(-(d / np.radians(r * 2.5)) ** 2)
    return np.clip(halo + streak * np.exp(-d / np.radians(length)) * (d > np.radians(r)), 0, 1)


def arc(pts, normal, start, length_deg, width_deg, wiggle=0.0, seed=0):
    """大圓上的一段弧（峽谷、裂縫、條紋用）。回傳 0~1 的遮罩。"""
    n = normal / np.linalg.norm(normal)
    s = start - n * (start @ n); s /= np.linalg.norm(s)
    t = np.cross(n, s)
    dist = np.abs(np.arcsin(np.clip(n @ pts, -1, 1)))
    if wiggle > 0:
        dist = np.abs(np.arcsin(np.clip(n @ pts, -1, 1)) + np.radians(wiggle) * (fbm(pts * 6, 3, seed) - 0.5))
    phi = np.degrees(np.arctan2(t @ pts, s @ pts)) % 360
    along = smoothstep(0, 8, phi) * smoothstep(length_deg, length_deg - 8, phi)
    return smoothstep(np.radians(width_deg), 0, dist) * along


def random_arcs(pts, rng, count, length, width, wiggle=0.0, region=None):
    """隨機的一堆弧；region = (中心 lon, lat, 半徑度) 時只在那附近產生。"""
    m = np.zeros(pts.shape[1])
    for k in range(count):
        if region is None:
            start = rng.normal(size=3)
        else:
            start = unit(region[0], region[1]) + np.radians(region[2]) * rng.normal(size=3)
        n = np.cross(start, rng.normal(size=3))
        m = np.maximum(m, arc(pts, n, start, rng.uniform(*length), rng.uniform(*width), wiggle, k * 7))
    return m


def cratered(pts, lat, width, rng, h, a, density=1.0, depth=1.0, fresh=0.08, big=True):
    """標準的撞擊坑三層：大盆地、中坑、小坑。density 控制數量，depth 控制深淺。"""
    s = (width / SMALL) ** 2   # 解析度越高，看得到越多小坑
    if big:
        add_craters(pts, lat, width, h, a, rng, int(8 * density), 0.12, 0.35, depth=0.5 * depth, fresh_fraction=0)
    add_craters(pts, lat, width, h, a, rng, int(150 * density), 0.03, 0.12, depth=0.45 * depth, fresh_fraction=fresh)
    add_craters(pts, lat, width, h, a, rng, int(900 * density * s), 0.008, 0.03, depth=0.25 * depth, fresh_fraction=fresh)


def grey(v, tint):
    return v[:, None] * np.array(tint)


def save(name, width, rgb, height):
    os.makedirs(OUT_DIR, exist_ok=True)
    rgba = np.concatenate([np.clip(rgb, 0, 1), np.clip(height, 0, 1)[:, None]], axis=1)
    img = (rgba * 255 + 0.5).reshape(width // 2, width, 4).astype(np.uint8)
    img = img[:, ::-1]   # 左右翻轉，配合 Unity 球體的 UV 方向（見檔案開頭）
    Image.fromarray(np.ascontiguousarray(img), "RGBA").save(os.path.join(OUT_DIR, f"{name}Albedo.png"))


# ---------- 地球 ----------

def moon(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h = 0.12 * fbm(pts * 3, 6, 1)
    a = np.zeros_like(h)
    cratered(pts, lat, width, rng, h, a, density=1.4)
    # 月海：古老的大盆地被玄武岩熔岩填平，幾乎都在朝地球的那一面（lon 0 附近）
    maria = [(-55, 15, 32, 34), (-17, 33, 17, 15), (17, 27, 10, 9), (30, 8, 12, 9), (59, 17, 7, 6),
             (51, -8, 8, 10), (-17, -21, 10, 8), (-39, -24, 6, 6), (0, 56, 38, 4), (35, -15, 9, 5)]
    edge = (fbm(pts * 5, 4, 2) - 0.5) * 0.6
    mare = np.zeros_like(h)
    for lo, la, aa, bb in maria:
        _, _, r = oval(lon, lat, lo, la, aa, bb)
        mare = np.maximum(mare, smoothstep(1.05, 0.8, r + edge))
    h = h * (1 - 0.8 * mare) + 0.05 * mare * fbm(pts * 8, 3, 3)       # 熔岩把坑填平
    add_craters(pts, lat, width, h, a, rng, int(1500 * (width / SMALL) ** 2), 0.004, 0.015, depth=0.15, fresh_fraction=0.05)
    # 有名的新鮮坑：第谷、哥白尼、克卜勒（明亮的放射紋）
    for lo, la, r, ln, s in [(-11, -43, 2.5, 25, 0.35), (-20, 10, 2.5, 12, 0.22), (-38, 8, 1.2, 8, 0.15)]:
        crater_at(pts, h, a, lo, la, r, 0.25, 0.1)
        a += s * rays(pts, lo, la, r, ln, int(abs(lo * 10 + la)))
    highland = 0.60 + 0.10 * (fbm(pts * 4, 5, 4) - 0.5)
    mare_v = 0.30 + 0.05 * (fbm(pts * 6, 4, 5) - 0.5)
    v = mix(highland[:, None], mare_v[:, None], mare)[:, 0] + a
    rgb = grey(v, (1.0, 0.98, 0.95)) * mix(np.array([1.0, 1.0, 1.0]), np.array([0.95, 0.97, 1.02]), mare)
    save("Moon", width, rgb, normalize01(h))


# ---------- 火星 ----------

def phobos(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h = 0.2 * fbm(pts * 2, 5, 11)
    a = np.zeros_like(h)
    cratered(pts, lat, width, rng, h, a, density=1.3, fresh=0.0)
    crater_at(pts, h, a, 45, 1, 24, 0.9, 0.04)                    # 斯蒂克尼坑：直徑將近火衛一的一半
    # 平行的溝槽（可能是 Stickney 撞擊或火星潮汐造成）
    d = pts[0] * 0.2 + pts[2] * 0.98
    grooves = smoothstep(0.75, 0.95, np.sin(pts[1] * 45 + 4 * fbm(pts * 3, 3, 12)) * 0.5 + 0.5) * (np.abs(d) < 0.8)
    h -= 0.12 * grooves
    v = 0.24 + 0.04 * (fbm(pts * 6, 4, 13) - 0.5) + a * 0.5   # 真實反照率約 0.07，太暗看不到，提高到看得清楚
    save("Phobos", width, grey(v, (1.0, 0.92, 0.82)), normalize01(h))


def deimos(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h = 0.15 * fbm(pts * 2, 5, 21)
    a = np.zeros_like(h)
    cratered(pts, lat, width, rng, h, a, density=0.5, depth=0.5, fresh=0.0)   # 表面被塵土蓋住，坑比較淺
    v = 0.27 + 0.04 * (fbm(pts * 4, 4, 22) - 0.5) + a * 0.5   # 同火衛一，提亮
    save("Deimos", width, grey(v, (1.0, 0.93, 0.84)), normalize01(h))


# ---------- 木星（伽利略衛星） ----------

def io(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    w = fbm_vec(pts * 2, 3, 31)
    n1 = fbm(pts * 3 + w, 5, 32)
    n2 = fbm(pts * 5 + 2 * w, 4, 33)
    yellow, white = np.array([0.92, 0.84, 0.42]), np.array([0.94, 0.93, 0.84])
    orange, polar = np.array([0.82, 0.50, 0.22]), np.array([0.50, 0.38, 0.28])
    rgb = mix(yellow, white, smoothstep(0.55, 0.7, n1))
    rgb = mix(rgb, orange, smoothstep(0.55, 0.72, n2) * 0.8)
    rgb = mix(rgb, polar, smoothstep(45, 70, np.abs(np.degrees(lat)) + 10 * (n1 - 0.5)))
    h = 0.3 * n1
    # 火山口（paterae）：黑色的熔岩湖，有些周圍有紅色或白色的噴發環
    for k in range(70):
        c = rng.normal(size=3); c /= np.linalg.norm(c)
        d = angle_to(pts, c) / np.radians(rng.uniform(0.6, 2.0))
        rgb = mix(rgb, np.array([0.08, 0.06, 0.05]), smoothstep(1.0, 0.6, d))
        if rng.random() < 0.35:
            ring = rng.choice([0, 1])
            col = [np.array([0.75, 0.30, 0.15]), np.array([0.96, 0.94, 0.85])][ring]
            rgb = mix(rgb, col, np.exp(-((d - 4) / 1.5) ** 2) * 0.5)
    # 貝利火山（Pele）：巨大的紅色噴發環
    d = angle_to(pts, unit(-105, -19)) / np.radians(18)
    rgb = mix(rgb, np.array([0.72, 0.28, 0.14]), np.exp(-((d - 1) / 0.12) ** 2) * 0.7 * (0.6 + 0.8 * fbm(pts * 10, 3, 34)))
    rgb = mix(rgb, np.array([0.08, 0.06, 0.05]), smoothstep(0.12, 0.06, d))
    save("Io", width, rgb, normalize01(h))


def europa(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    ice, brown = np.array([0.92, 0.88, 0.80]), np.array([0.62, 0.42, 0.30])
    n = fbm(pts * 3, 5, 41)
    rgb = mix(ice, np.array([0.80, 0.72, 0.62]), smoothstep(0.5, 0.7, n) * 0.6)
    # 紅褐色的裂紋（lineae）：冰殼被潮汐拉扯裂開，下面的鹽類物質滲出來
    lines = random_arcs(pts, rng, 70, (40, 160), (0.25, 0.8), wiggle=1.0)
    fine = smoothstep(0.82, 0.95, ridged(pts * 12, 4, 42))
    rgb = mix(rgb, brown, np.clip(lines * 0.85 + fine * 0.25, 0, 1))
    # 混沌地形：後隨面比較多，冰塊破碎、顏色斑駁
    chaos = smoothstep(0.55, 0.7, fbm(pts * 4, 5, 43)) * smoothstep(-0.2, 0.6, -pts[2])
    rgb = mix(rgb, brown * 0.9, chaos * 0.6 * fbm(pts * 15, 3, 44))
    h = 0.3 * n + 0.15 * lines + 0.1 * fine
    a = np.zeros_like(h)
    crater_at(pts, h, a, 89, -25, 1.0, 0.2, 0.0)                 # 皮威爾坑（Pwyll）：很少數的撞擊坑之一
    rgb = mix(rgb, np.array([0.97, 0.96, 0.93]), rays(pts, 89, -25, 1.0, 25, 45) * 0.6)
    save("Europa", width, rgb, normalize01(h))


def ganymede(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h = 0.15 * fbm(pts * 3, 5, 51)
    a = np.zeros_like(h)
    # 暗色古老地形 vs 亮色溝槽地形（冰殼曾被拉開、重新結凍）
    w = fbm_vec(pts * 2, 3, 52)
    bright = smoothstep(0.45, 0.55, fbm(pts * 2 + w, 5, 53))
    _, _, r = oval(lon, lat, -145, 35, 50, 40)                     # 伽利略區：最大的暗區
    bright *= smoothstep(0.7, 1.1, r)
    grooves = 0.5 + 0.5 * np.sin((pts[0] * 30 + pts[1] * 20) + 6 * w[0])
    h += 0.1 * bright * grooves
    cratered(pts, lat, width, rng, h, a, density=0.8, fresh=0.2)
    dark, light = np.array([0.38, 0.34, 0.30]), np.array([0.70, 0.68, 0.65])
    rgb = mix(dark, light, bright) * (0.92 + 0.08 * grooves * bright + a)[:, None]
    rgb = mix(rgb, np.array([0.86, 0.86, 0.88]), smoothstep(40, 65, np.abs(np.degrees(lat))) * 0.5)   # 極區的霜
    save("Ganymede", width, rgb, normalize01(h))


def callisto(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h = 0.1 * fbm(pts * 3, 5, 61)
    a = np.zeros_like(h)
    cratered(pts, lat, width, rng, h, a, density=2.2, depth=0.8, fresh=0.35)   # 太陽系坑最多的天體之一
    # 瓦爾哈拉多環盆地：亮的中心 + 一圈圈的環
    d = angle_to(pts, unit(56, 15))
    a += 0.25 * np.exp(-(d / 0.15) ** 2) + 0.08 * (0.5 + 0.5 * np.sin(d * 60)) * smoothstep(0.9, 0.2, d)
    v = 0.25 + 0.05 * (fbm(pts * 5, 4, 62) - 0.5) + a
    save("Callisto", width, grey(v, (1.0, 0.93, 0.85)), normalize01(h))


# ---------- 土星 ----------

def icy_base(pts, lat, width, rng, level, tint, seed, density=1.0, fresh=0.1):
    h = 0.1 * fbm(pts * 3, 5, seed)
    a = np.zeros_like(h)
    cratered(pts, lat, width, rng, h, a, density=density, fresh=fresh)
    v = level + 0.05 * (fbm(pts * 5, 4, seed + 1) - 0.5)
    return h, a, v, np.array(tint)


def mimas(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h, a, v, t = icy_base(pts, lat, width, rng, 0.78, (1.0, 1.0, 1.0), 71, density=1.6)
    crater_at(pts, h, a, 90, 0, 19, 1.2, -0.05)                   # 赫歇爾坑：直徑是土衛一的 1/3（像死星）
    save("Mimas", width, grey(v + a, t), normalize01(h))


def enceladus(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h = 0.08 * fbm(pts * 3, 5, 81)
    a = np.zeros_like(h)
    north = lambda la: float(la > np.radians(20))                   # 北方有坑，南方被重新覆蓋、很平滑
    add_craters(pts, lat, width, h, a, rng, 120, 0.02, 0.08, depth=0.3, fresh_fraction=0.0, lat_weight=north)
    v = 0.96 + 0.03 * (fbm(pts * 5, 4, 82) - 0.5)
    rgb = grey(v + a, (1.0, 1.0, 1.0))
    # 南極「虎紋」：四條平行的裂縫，噴出水氣
    x, z = pts[0], pts[2]
    stripe = np.zeros_like(h)
    for k in range(4):
        off = (k - 1.5) * 0.09
        d = np.abs(x * 0.8 + z * 0.6 - off + 0.03 * (fbm(pts * 8, 3, 83 + k) - 0.5))
        stripe = np.maximum(stripe, smoothstep(0.012, 0.0, d) * smoothstep(-0.78, -0.92, pts[1]))
    rgb = mix(rgb, np.array([0.55, 0.75, 0.88]), stripe)
    h -= 0.2 * stripe
    lines = random_arcs(pts, rng, 25, (20, 60), (0.2, 0.5), region=(0, -40, 50))
    h -= 0.08 * lines
    save("Enceladus", width, rgb, normalize01(h))


def tethys(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h, a, v, t = icy_base(pts, lat, width, rng, 0.86, (1.0, 0.99, 0.96), 91, density=1.3)
    crater_at(pts, h, a, 130, 30, 27, 0.6, 0.0)                   # 奧德修斯坑
    # 伊薩卡峽谷：繞了 3/4 圈的大裂谷，剛好在奧德修斯坑的「赤道」上
    chasma = arc(pts, unit(130, 30), unit(130, 30 - 90) + 0.01, 270, 3.5, wiggle=3, seed=92)
    h -= 0.5 * chasma
    save("Tethys", width, grey(v + a - 0.05 * chasma, t), normalize01(h))


def dione(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h, a, v, t = icy_base(pts, lat, width, rng, 0.80, (1.0, 0.99, 0.96), 101, density=1.2)
    # 後隨面的亮色細紋（wispy terrain）：其實是冰的懸崖
    wisps = random_arcs(pts, rng, 40, (15, 70), (0.2, 0.6), wiggle=1, region=(-90, 0, 40))
    v = v - 0.10 * smoothstep(0.3, -0.8, pts[2])                   # 後隨面整體比較暗
    h -= 0.15 * wisps
    save("Dione", width, grey(v + a + 0.25 * wisps, t), normalize01(h))


def rhea(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h, a, v, t = icy_base(pts, lat, width, rng, 0.76, (1.0, 0.98, 0.95), 111, density=1.8)
    wisps = random_arcs(pts, rng, 20, (15, 50), (0.2, 0.5), wiggle=1, region=(-90, 0, 35))
    crater_at(pts, h, a, 112, 25, 2.5, 0.3, 0.1)                  # 因克托米坑：新鮮的亮紋
    a += 0.3 * rays(pts, 112, 25, 2.5, 20, 112)
    save("Rhea", width, grey(v + a + 0.2 * wisps, t), normalize01(h))


def titan(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    # 濃厚的橘色霧霾（有機物），看不到地面；只有很淡的緯度帶，北極有比較暗的「頭罩」
    lat_deg = np.degrees(lat)
    n = fbm(zonal(pts, 4) * 2, 5, 121)
    v = 0.92 + 0.06 * (n - 0.5) + 0.03 * np.sin(np.radians(lat_deg) * 6)
    rgb = grey(v, (0.86, 0.58, 0.24))
    rgb = mix(rgb, np.array([0.62, 0.42, 0.20]), smoothstep(55, 80, lat_deg) * 0.8)
    save("Titan", width, rgb, np.full(v.shape, 0.5))


def iapetus(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h, a, v, t = icy_base(pts, lat, width, rng, 0.72, (1.0, 0.98, 0.94), 131, density=1.3)
    # 卡西尼區：前導面像被塗了一層黑炭（撿到土衛九掉下來的暗色塵埃），兩極仍是亮的
    d = angle_to(pts, unit(90, 0))
    dark = smoothstep(1.35, 1.15, d + 0.25 * (fbm(pts * 6, 5, 132) - 0.5) + 0.6 * np.abs(pts[1]) ** 2)
    rgb = mix(grey(v + a, t), np.array([0.10, 0.07, 0.05]) * (1 + a)[:, None], dark)
    h += 0.25 * np.exp(-(lat / np.radians(1.5)) ** 2) * smoothstep(1.6, 0.8, d)   # 赤道山脊（像核桃）
    save("Iapetus", width, rgb, normalize01(h))


# ---------- 天王星 ----------

def miranda(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h, a, v, t = icy_base(pts, lat, width, rng, 0.62, (1.0, 1.0, 1.02), 141, density=0.8)
    # 冠狀地形（coronae）：像賽道一樣一圈一圈的溝槽，被拼貼在地表上
    for lo, la, rr, ang in [(30, -30, 30, 20), (-60, -20, 28, 0), (170, -40, 25, 45)]:
        x, y, r = oval(lon, lat, lo, la, rr, rr * 0.8)
        rect = smoothstep(1.0, 0.85, np.maximum(np.abs(x), np.abs(y)))
        rings = 0.5 + 0.5 * np.sin(np.maximum(np.abs(x), np.abs(y)) * 25)
        h += 0.15 * rect * rings - 0.1 * rect
        v = v + rect * (0.12 * rings - 0.06)
    save("Miranda", width, grey(v + a, t), normalize01(h))


def ariel(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h, a, v, t = icy_base(pts, lat, width, rng, 0.66, (1.0, 0.99, 0.98), 151, density=0.9)
    canyons = random_arcs(pts, rng, 18, (30, 100), (1.0, 2.2), wiggle=2)   # 地殼拉開形成的峽谷，底部平滑
    h -= 0.4 * canyons
    save("Ariel", width, grey(v + a + 0.06 * canyons, t), normalize01(h))


def umbriel(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h, a, v, t = icy_base(pts, lat, width, rng, 0.30, (1.0, 1.0, 1.03), 161, density=1.6, fresh=0.0)
    d = angle_to(pts, unit(-87, -8))                                 # 溫達坑：暗色表面上唯一的亮環
    v = v + 0.35 * np.exp(-((d - 0.07) / 0.02) ** 2)
    save("Umbriel", width, grey(v + 0.5 * a, t), normalize01(h))


def titania(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h, a, v, t = icy_base(pts, lat, width, rng, 0.50, (1.0, 0.95, 0.90), 171, density=1.1)
    canyons = random_arcs(pts, rng, 8, (40, 110), (0.8, 1.5), wiggle=2)
    h -= 0.35 * canyons
    save("Titania", width, grey(v + a - 0.04 * canyons, t), normalize01(h))


def oberon(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    h, a, v, t = icy_base(pts, lat, width, rng, 0.45, (1.0, 0.90, 0.82), 181, density=1.5, fresh=0.15)
    a = np.where(a < 0, a * 4, a)                                    # 坑底有暗色物質，比一般坑更暗
    crater_at(pts, h, a, 20, -10, 6, 0.3, 0.0)
    h += 0.4 * np.exp(-(angle_to(pts, unit(-30, -60)) / 0.06) ** 2)   # 高 11 公里的山
    save("Oberon", width, grey(v + a, t), normalize01(h))


# ---------- 海王星 ----------

def triton(width, rng):
    pts, lon, lat, _ = sphere_grid(width)
    lat_deg = np.degrees(lat)
    # 北半球：「哈密瓜地形」（一個個小凹坑）；南半球：粉紅色的氮冰極冠，上面有深色的噴泉條紋
    cells = ridged(pts * 14, 3, 191)
    h = 0.08 * cells + 0.1 * fbm(pts * 3, 4, 192)
    rgb = mix(np.array([0.74, 0.72, 0.68]), np.array([0.84, 0.81, 0.76]), cells)
    cap = smoothstep(-10, -20, lat_deg + 12 * (fbm(pts * 4, 4, 193) - 0.5))
    rgb = mix(rgb, np.array([0.92, 0.82, 0.78]), cap)
    for k in range(45):
        c = unit(rng.uniform(-180, 180), rng.uniform(-70, -20))
        x, y, r = oval(lon, lat, np.degrees(np.arctan2(c[2], c[0])), np.degrees(np.arcsin(c[1])), 4.5, 1.2)
        rgb = mix(rgb, np.array([0.40, 0.33, 0.32]), smoothstep(1.0, 0.3, r) * 0.7 * cap)
    a = np.zeros_like(h)
    add_craters(pts, lat, width, h, a, rng, 20, 0.01, 0.03, depth=0.2, fresh_fraction=0)   # 很年輕的表面，坑很少
    save("Triton", width, rgb, normalize01(h))


MOONS = {
    "Moon": moon, "Phobos": phobos, "Deimos": deimos,
    "Io": io, "Europa": europa, "Ganymede": ganymede, "Callisto": callisto,
    "Mimas": mimas, "Enceladus": enceladus, "Tethys": tethys, "Dione": dione, "Rhea": rhea,
    "Titan": titan, "Iapetus": iapetus,
    "Miranda": miranda, "Ariel": ariel, "Umbriel": umbriel, "Titania": titania, "Oberon": oberon,
    "Triton": triton,
}

# Planet.shader 的材質參數（凹凸強度；泰坦有大氣）。2048 寬的貼圖相鄰像素的高度差只有 1024 的一半，所以凹凸要加倍
MATERIAL = {name: {"bump": 4} for name in MOONS}
MATERIAL["Moon"]["bump"] = 8
MATERIAL["Io"]["bump"] = 2
MATERIAL["Titan"] = {"bump": 0, "limb": 0.4, "atmo": (0.9, 0.5, 0.15), "atmo_power": 2.5}


# ---------- 預覽：照 Planet.shader 的公式畫成球 ----------

def render_sphere(name, size=150):
    img = np.asarray(Image.open(os.path.join(OUT_DIR, f"{name}Albedo.png"))).astype(np.float64) / 255
    img = img[:, ::-1]   # 存檔時翻轉過，翻回一般地圖的方向再畫
    th, tw = img.shape[:2]
    albedo = img[..., :3] ** 2.2                 # sRGB → 線性（Unity 線性色彩空間）
    hgt = img[..., 3]
    m = MATERIAL[name]
    dhdu = np.roll(hgt, -1, 1) - np.roll(hgt, 1, 1)
    dhdv = np.roll(hgt, 1, 0) - np.roll(hgt, -1, 0)   # 第 0 列是北極，v 往上
    ys, xs = np.mgrid[0:size, 0:size]
    x = (xs + 0.5) / size * 2 - 1
    y = 1 - (ys + 0.5) / size * 2
    r2 = x * x + y * y
    inside = r2 < 1
    z = np.sqrt(np.clip(1 - r2, 0, 1))
    # 看向 lon = 0（朝行星的那面）：畫面 (x, y, z) → 物件 (z, y, x)
    p = np.stack([z, y, x])
    lon = np.arctan2(p[2], p[0])
    lat = np.arcsin(np.clip(p[1], -1, 1))
    u = ((lon / np.pi + 1) / 2 * tw).astype(int) % tw
    v = ((0.5 - lat / np.pi) * th).astype(int).clip(0, th - 1)
    T = np.stack([-np.sin(lon), np.zeros_like(lon), np.cos(lon)])
    B = np.stack([-np.sin(lat) * np.cos(lon), np.cos(lat), -np.sin(lat) * np.sin(lon)])
    N = p - m.get("bump", 0) * (dhdu[v, u] * T + dhdv[v, u] * B)
    N /= np.linalg.norm(N, axis=0) + 1e-9
    L = np.array([0.55, 0.35, -0.76]); L /= np.linalg.norm(L)       # 光從左上前方來
    lit = np.clip(np.einsum("i,ijk->jk", L, N), 0, 1)
    col = albedo[v, u] * (lit + 0.02)[..., None]
    if "limb" in m:
        col *= (np.maximum(z, 1e-3) ** m["limb"])[..., None]
    if "atmo" in m:
        fres = (1 - z) ** m["atmo_power"]
        col += np.array(m["atmo"]) * (fres * np.clip(np.einsum("i,ijk->jk", L, p) + 0.3, 0, 1))[..., None]
    col = np.clip(col, 0, 1) ** (1 / 2.2)
    out = np.zeros((size, size, 3))
    out[inside] = col[inside]
    return (out * 255).astype(np.uint8)


def preview():
    names = list(MOONS)
    cols, size, pad = 5, 150, 22
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * (size * 2 + 10), rows * (size + pad)), (8, 8, 12))
    d = ImageDraw.Draw(sheet)
    for i, n in enumerate(names):
        cx, cy = (i % cols) * (size * 2 + 10), (i // cols) * (size + pad)
        sheet.paste(Image.fromarray(render_sphere(n, size)), (cx, cy))
        tex = Image.open(os.path.join(OUT_DIR, f"{n}Albedo.png")).convert("RGB").resize((size, size // 2))
        sheet.paste(tex, (cx + size, cy + size // 4))
        d.text((cx + 4, cy + size + 4), n, fill=(220, 220, 220))
    os.makedirs(os.path.dirname(PREVIEW), exist_ok=True)
    sheet.save(PREVIEW)
    print(f"預覽圖 {os.path.normpath(PREVIEW)}")


def main():
    args = sys.argv[1:]
    if "--preview" in args:
        preview()
        return
    for name in args or list(MOONS):
        width = LARGE if name == "Moon" else SMALL
        print(f"產生 {name} ({width} x {width // 2}) ...")
        rng = np.random.default_rng(sum(map(ord, name)) + 1000)   # 固定種子：每次產生的結果一樣
        MOONS[name](width, rng)
    preview()
    print(f"輸出到 {os.path.normpath(OUT_DIR)}")


if __name__ == "__main__":
    main()
