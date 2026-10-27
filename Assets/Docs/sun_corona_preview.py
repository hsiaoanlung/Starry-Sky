"""
照 Assets/Shaders/SunCorona.shader（體積版日冕）同樣的公式，在 CPU 上畫出俯視 / 斜 40° / 側視三張預覽。
用法：python Assets/Docs/sun_corona_preview.py [密度 0.3] [輸出 png] [光芒銳利度 4]
改 Shader 的公式或預設值時，這裡也要一起改，才能在 Unity 外先看效果。
"""
import sys
import numpy as np
from PIL import Image

def frac(x): return x - np.floor(x)

def sun_hash(p):
    p = frac(p * 0.3183099 + 0.1) * 17.0
    return frac(p[0] * p[1] * p[2] * (p[0] + p[1] + p[2]))

def sun_noise(x):
    i = np.floor(x); f = x - i
    f = f * f * (3 - 2 * f)
    def h(dx, dy, dz): return sun_hash(i + np.array([dx, dy, dz]).reshape(3, *([1] * (x.ndim - 1))))
    l = lambda a, b, t: a + (b - a) * t
    return l(l(l(h(0,0,0), h(1,0,0), f[0]), l(h(0,1,0), h(1,1,0), f[0]), f[1]),
             l(l(h(0,0,1), h(1,0,1), f[0]), l(h(0,1,1), h(1,1,1), f[0]), f[1]), f[2])

def sun_fbm(p, octaves=5):
    s = 0; amp = 0.5; norm = 0
    off = np.array([1.7, 9.2, 3.1]).reshape(3, *([1] * (p.ndim - 1)))
    for _ in range(octaves):
        s = s + amp * sun_noise(p); norm += amp
        p = p * 2.03 + off; amp *= 0.5
    return s / norm

# ---- 參數（和 Shader Properties 預設值一致）----
INNER = np.array([1.2, 0.8, 0.35]); OUTER = np.array([0.6, 0.15, 0.03])
CORONA = 4.0; FALLOFF = 2.2; FREQ = 6.0; SHARP = float(sys.argv[3]) if len(sys.argv) > 3 else 4.0; FLOW = 0.25; INTENSITY = 0.8  # SunVisual.coronaIntensity
DENSITY = float(sys.argv[1]) if len(sys.argv) > 1 else 0.3
STEPS = 32
T = 3.0  # 秒

def emission(p, t):
    r = np.linalg.norm(p, axis=0); d = p / np.maximum(r, 1e-5)
    h = np.maximum(r - 1, 0)
    L = np.log(np.maximum(r, 1)) * 3.0
    ph0 = frac(t); ph1 = frac(t + 0.5)
    w0 = 1 - np.abs(2 * ph0 - 1)
    def layer(ph, seed):
        s = L - ph
        a = sun_fbm(d * (FREQ + s) + seed, 4)
        b = sun_fbm(d * (FREQ * 2.3 + 1.7 * s) + 11 + seed, 4)
        return a * 0.65 + b * 0.5
    rays = layer(ph0, 0.0) * w0 + layer(ph1, 37.0) * (1 - w0)
    fil = np.clip(rays, 0, 1) ** SHARP * 3
    wob = sun_fbm(d * 2 + np.array([0, 0, t * 0.3]).reshape(3, 1), 3) * 0.8 + 0.6
    glow = np.exp(-h * FALLOFF / wob)
    fade = np.clip((1 - r / CORONA) / 0.25, 0, 1); fade = fade * fade * (3 - 2 * fade)
    w = np.clip(glow * 1.3, 0, 1)[None]
    col = OUTER[:, None] * (1 - w) + INNER[:, None] * w
    return col * (glow * (0.35 + fil) * fade)[None]

def render(elev_deg, N=220):
    e = np.radians(elev_deg)
    fwd = -np.array([0, np.sin(e), np.cos(e)])            # 視線方向（從相機看向太陽）
    up = np.array([0, np.cos(e), -np.sin(e)]); right = np.cross(up, fwd)
    xs = np.linspace(-CORONA, CORONA, N)
    X, Y = np.meshgrid(xs, -xs)
    o = (X.ravel()[None] * right[:, None] + Y.ravel()[None] * up[:, None]) - fwd[:, None] * 10
    rd = np.repeat(fwd[:, None], o.shape[1], 1)
    # 射線與殼層 / 太陽球的交點
    bq = np.sum(o * rd, 0); c = np.sum(o * o, 0)
    disc = bq * bq - (c - CORONA ** 2)
    hit = disc > 0
    sq = np.sqrt(np.maximum(disc, 0))
    t0 = np.maximum(-bq - sq, 0); t1 = -bq + sq
    disc_s = bq * bq - (c - 1)
    sun = disc_s > 0
    t1 = np.where(sun, -bq - np.sqrt(np.maximum(disc_s, 0)), t1)
    ds = (t1 - t0) / STEPS
    jitter = sun_hash(np.stack([X.ravel() * 37, Y.ravel() * 53, np.zeros(X.size)]))
    acc = np.zeros((3, o.shape[1]))
    for k in range(STEPS):
        tt = t0 + (k + jitter) * ds
        acc += emission(o + rd * tt, T * FLOW) * ds
    acc *= DENSITY * INTENSITY
    # 解析的貼表面亮環：用射線離中心最近的距離 b
    b = np.sqrt(np.maximum(c - bq * bq, 0))
    rim = np.exp(-np.maximum(b - 1, 0) * 18) * 1.5
    acc += INNER[:, None] * rim * INTENSITY   # Shader 也是最後才乘整體亮度
    acc *= hit
    # 太陽本體（簡化的臨邊昏暗圓盤）蓋在上面：深度測試讓盤面內只看得到太陽
    pts = o + rd * (-bq - np.sqrt(np.maximum(disc_s, 0)))
    mu = np.clip(np.sum(-pts * rd, 0), 0, 1)
    sun_col = np.array([0.6, 0.12, 0.02])[:, None] * (1 - mu ** 0.5) + np.array([1.3, 0.8, 0.3])[:, None] * mu ** 0.5
    img = np.where(sun[None], sun_col, acc)
    return img.reshape(3, N, N).transpose(1, 2, 0)

def tonemap(img):
    over = (img.max(-1) > 1)
    out = np.clip(img, 0, 1)
    return (out ** (1 / 2.2) * 255).astype(np.uint8), over.mean()

tiles = []
for el in (90, 40, 0):
    im = render(el)
    print(f"elev {el}: max {im.max():.2f}, 99% {np.percentile(im.max(-1), 99):.2f}, >1 pixels {np.mean(im.max(-1)>1)*100:.1f}%")
    tiles.append(tonemap(im)[0])
Image.fromarray(np.concatenate(tiles, 1)).save(sys.argv[2] if len(sys.argv) > 2 else "corona_preview.png")
