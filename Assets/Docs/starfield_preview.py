"""
照 Assets/Shaders/Starfield.shader 同樣的公式在 CPU 上畫星空預覽：
  1. 全天空的等距長方投影（2:1）
  2. 兩張 60° 視野的透視圖（遊戲裡的預設視野，1080p 的像素大小）：看向銀河中心 / 看向軌道面
用法：python Assets/Docs/starfield_preview.py [輸出資料夾，預設 Temp/StarrySkyPreview]
改 Shader 的公式或預設值時，這裡也要一起改。
"""
import os
import sys
import numpy as np
from PIL import Image

EXPOSURE, DENSITY, STAR_SIZE, MILKY, TILT = 1.0, 1.0, 0.6, 1.0, 60.0


def frac(x): return x - np.floor(x)


def sun_hash(p):
    p = frac(p * 0.3183099 + 0.1) * 17.0
    return frac(p[0] * p[1] * p[2] * (p[0] + p[1] + p[2]))


def sun_noise(x):
    i = np.floor(x); f = x - i
    f = f * f * (3 - 2 * f)
    def h(dx, dy, dz): return sun_hash(i + np.array([dx, dy, dz]).reshape(3, 1))
    l = lambda a, b, t: a + (b - a) * t
    return l(l(l(h(0,0,0), h(1,0,0), f[0]), l(h(0,1,0), h(1,1,0), f[0]), f[1]),
             l(l(h(0,0,1), h(1,0,1), f[0]), l(h(0,1,1), h(1,1,1), f[0]), f[1]), f[2])


def sun_fbm(p, octaves):
    s = 0; amp = 0.5; norm = 0
    off = np.array([1.7, 9.2, 3.1]).reshape(3, 1)
    for _ in range(octaves):
        s = s + amp * sun_noise(p); norm += amp
        p = p * 2.03 + off; amp *= 0.5
    return s / norm


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def cube_face(d):
    a = np.abs(d)
    face = np.zeros(d.shape[1]); uv = np.zeros((2, d.shape[1]))
    mx = (a[0] >= a[1]) & (a[0] >= a[2])
    my = ~mx & (a[1] >= a[2])
    mz = ~mx & ~my
    face[mx] = np.where(d[0, mx] > 0, 0, 1); uv[:, mx] = d[1:3, mx] / a[0, mx]
    face[my] = np.where(d[1, my] > 0, 2, 3); uv[:, my] = d[[0, 2]][:, my] / a[1, my]
    face[mz] = np.where(d[2, mz] > 0, 4, 5); uv[:, mz] = d[0:2, mz] / a[2, mz]
    return face, uv


def face_dir(face, uv):
    one = np.ones_like(uv[0])
    out = np.zeros((3, uv.shape[1]))
    for f, (s, ax) in enumerate([(1, 0), (-1, 0), (1, 1), (-1, 1), (1, 2), (-1, 2)]):
        m = face == f
        other = [k for k in range(3) if k != ax]
        out[ax, m] = s * one[m]
        out[other[0], m] = uv[0, m]
        out[other[1], m] = uv[1, m]
    return out


def star_layer(d, face, uv, cells, density, minb, maxb, bpow, seed, sigma):
    idx = np.floor((uv * 0.5 + 0.5) * cells)
    h = np.stack([idx[0], idx[1], face * 7.0 + seed])
    has = sun_hash(h) <= density
    jit = np.stack([sun_hash(h + 17.3), sun_hash(h + 41.9)])
    suv = (idx + 0.2 + 0.6 * jit) / cells * 2 - 1
    sd = face_dir(face, suv); sd /= np.linalg.norm(sd, axis=0)
    ang = np.linalg.norm(d - sd, axis=0)
    shape = np.exp(-ang * ang / (2 * sigma * sigma))
    b = minb + (maxb - minb) * sun_hash(h + 63.1) ** bpow
    t = sun_hash(h + 88.7)
    col = np.where(t < 0.15, [[1.0], [0.7], [0.45]],
          np.where(t < 0.35, [[1.0], [0.92], [0.8]],
          np.where(t < 0.85, [[0.95], [0.97], [1.0]], [[0.7], [0.8], [1.0]])))
    return col * (b * shape * has)


def shade(d, pixel_angle):
    sigma = pixel_angle * STAR_SIZE
    tilt = np.radians(TILT)
    pole = np.array([0, np.cos(tilt), np.sin(tilt)]).reshape(3, 1)
    lat = np.sum(d * pole, 0)
    band = np.exp(-lat * lat / (2 * 0.12 ** 2))
    gc = np.array([1.0, 0, 0]).reshape(3, 1)
    bulge = np.exp(-np.sum((d - gc) ** 2, 0) / 0.25)
    clouds = smoothstep(0.35, 0.8, sun_fbm(d * 6.0, 5))
    dust = smoothstep(0.45, 0.7, sun_fbm(d * 12.0 + 7.0, 4)) * np.exp(-lat * lat / (2 * 0.04 ** 2))
    mwc = np.array([[0.55], [0.6], [0.8]]) * (1 - bulge) + np.array([[1.0], [0.85], [0.65]]) * bulge
    milky = mwc * band * (0.035 * clouds + 0.06 * bulge) * (1 - 0.7 * dust) * MILKY
    face, uv = cube_face(d)
    stars = star_layer(d, face, uv, 40, 0.08 * DENSITY, 0.30, 1.40, 6.0, 0.0, sigma)
    stars += star_layer(d, face, uv, 110, 0.20 * DENSITY, 0.08, 0.45, 3.0, 20.0, sigma)
    stars += star_layer(d, face, uv, 200, 0.25 * DENSITY * (0.4 + 1.6 * band), 0.02, 0.12, 1.0, 40.0, sigma)
    return (stars + milky) * EXPOSURE, stars, milky


def to_img(c, w, h):
    return (np.clip(c, 0, 1) ** (1 / 2.2) * 255).astype(np.uint8).T.reshape(h, w, 3)


def perspective(forward, w, h, fov=60):
    f = forward / np.linalg.norm(forward)
    right = np.cross([0, 1, 0], f); right /= np.linalg.norm(right)
    up = np.cross(f, right)
    t = np.tan(np.radians(fov) / 2)
    xs = np.linspace(-t * w / h, t * w / h, w); ys = np.linspace(t, -t, h)
    X, Y = np.meshgrid(xs, ys)
    d = f[:, None] + X.ravel() * right[:, None] + Y.ravel() * up[:, None]
    return d / np.linalg.norm(d, axis=0)


out = sys.argv[1] if len(sys.argv) > 1 else "Temp/StarrySkyPreview"
os.makedirs(out, exist_ok=True)

W, H = 2048, 1024
lon = (np.arange(W) + 0.5) / W * 2 * np.pi - np.pi
lat = np.pi / 2 - (np.arange(H) + 0.5) / H * np.pi
LON, LAT = np.meshgrid(lon, lat)
d = np.stack([np.cos(LAT) * np.sin(LON), np.sin(LAT), np.cos(LAT) * np.cos(LON)]).reshape(3, -1)
col, stars, milky = shade(d, 2 * np.pi / W)
Image.fromarray(to_img(col, W, H)).save(os.path.join(out, "starfield_equirect.png"))
print(f"全天空：最亮 {col.max():.2f}，超過 1 的像素 {np.mean(col.max(0) > 1) * 100:.3f}%，"
      f"銀河最亮 {milky.max():.3f}，平均亮度 {col.mean():.4f}")

# 遊戲裡的樣子：60° 視野、1920×1080 的像素大小（只畫中間 960×540 一塊，像素和遊戲一樣大）
pix = 2 * np.tan(np.radians(30)) / 1080
for name, fwd in [("center", np.array([1.0, 0.05, 0.0])), ("plane", np.array([0.0, 0.0, 1.0]))]:
    w, h = 960, 540
    fov = np.degrees(2 * np.arctan(np.tan(np.radians(30)) * h / 1080))
    dd = perspective(fwd, w, h, fov)
    c, _, _ = shade(dd, pix)
    Image.fromarray(to_img(c, w, h)).save(os.path.join(out, f"starfield_{name}.png"))
    print(f"{name}：最亮 {c.max():.2f}，平均 {c.mean():.4f}")
