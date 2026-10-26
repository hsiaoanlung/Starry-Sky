"""
程序化產生地球貼圖（等距長方投影 equirectangular，2:1）。

輸出到 Assets/Textures/Earth/：
  EarthAlbedo.png  RGB = 地表顏色，A = 海洋遮罩（255 = 海，用來做海面反光）
  EarthClouds.png  灰階雲量
  EarthNight.png   夜間城市燈光

做法：每個像素換算成球面上的點 (x, y, z)，在 3D 空間取樣噪聲。
因為是在球面上取樣，左右邊界（經度 0/360）不會有接縫，兩極也不會被拉扯。

用法（在專案根目錄）：
  python Assets/Docs/earth_texture_gen.py            # 預設 2048 x 1024，種子 7
  python Assets/Docs/earth_texture_gen.py 4096 11    # 寬度 4096、種子 11（換種子 = 換一組大陸）
"""
import os
import sys
import numpy as np
from PIL import Image

from procedural_noise import (value_noise, fbm, fbm_vec, ridged, smoothstep,
                              weighted_quantile, mix, sphere_grid)

WIDTH = int(sys.argv[1]) if len(sys.argv) > 1 else 2048
SEED = int(sys.argv[2]) if len(sys.argv) > 2 else 7
HEIGHT = WIDTH // 2
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Textures", "Earth")


# ---------- 主程式 ----------

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    print(f"產生 {WIDTH} x {HEIGHT}，種子 {SEED}")

    # 像素中心 → 經緯度 → 球面點。圖片第 0 列 = 北極（Unity 的 UV v=1 在上方）
    pts, LON, LAT, area = sphere_grid(WIDTH)
    lat_deg = np.degrees(LAT)
    alat = np.abs(lat_deg)

    # --- 高度：domain-warped fBm 做大陸，再加山脊 ---
    print("  高度 ...")
    p = pts * 1.3
    warp = fbm_vec(p * 1.2, 4, SEED)
    h = fbm(p + 0.9 * warp, 8, SEED)
    h += 0.18 * ridged(pts * 3.0 + 0.5 * warp, 6, SEED + 500) * smoothstep(0.45, 0.6, h)
    sea = weighted_quantile(h, area, 0.70)   # 70% 海洋
    land = h > sea
    e = np.where(land, (h - sea) / (h.max() - sea), 0.0)         # 陸地高度 0~1
    d = np.where(land, 0.0, (sea - h) / (sea - h.min()))         # 海洋深度 0~1

    # --- 氣候：溫度（緯度 + 海拔）、濕度（噪聲 + 副熱帶沙漠帶） ---
    print("  氣候 ...")
    temp = 1.0 - (alat / 90.0) ** 1.4 - e * 0.9 + (fbm(pts * 4.0, 3, SEED + 900) - 0.5) * 0.15
    moist = fbm(pts * 2.5 + 0.6 * warp, 5, SEED + 700)
    arid_belt = np.exp(-((alat - 25.0) / 9.0) ** 2)
    wet_belt = np.exp(-(lat_deg / 10.0) ** 2)
    moist = moist - 0.30 * arid_belt + 0.15 * wet_belt
    detail = fbm(pts * 30.0, 4, SEED + 1300)

    # --- 地表顏色 ---
    print("  地表顏色 ...")
    deep = np.array([0.015, 0.05, 0.16])
    shallow = np.array([0.04, 0.22, 0.36])
    desert = np.array([0.74, 0.63, 0.46])
    grass = np.array([0.42, 0.50, 0.24])
    forest = np.array([0.10, 0.27, 0.09])
    jungle = np.array([0.06, 0.22, 0.06])
    tundra = np.array([0.45, 0.44, 0.36])
    rock = np.array([0.42, 0.37, 0.32])
    snow = np.array([0.93, 0.95, 0.98])

    ocean_col = mix(shallow, deep, smoothstep(0.0, 0.08, d))     # 淺海只留在海岸附近
    ground = mix(grass, forest, smoothstep(0.45, 0.6, moist))
    ground = mix(ground, jungle, smoothstep(0.6, 0.7, moist) * smoothstep(0.75, 0.9, temp))
    ground = mix(ground, desert, smoothstep(0.47, 0.35, moist) * smoothstep(0.5, 0.65, temp))
    ground = mix(ground, tundra, smoothstep(0.35, 0.2, temp))
    ground = mix(ground, rock, smoothstep(0.35, 0.6, e))
    ground = mix(ground, snow, smoothstep(0.12, 0.02, temp))
    ground *= (0.85 + 0.3 * detail)[:, None]                   # 細節明暗，不要太平
    ground = mix(ground, ocean_col, smoothstep(0.02, 0.0, e) * 0.35)   # 海岸線稍微柔和

    albedo = np.where(land[:, None], ground, ocean_col)

    # 極冠：陸地 > 68°、海冰 > 74°，邊緣用噪聲打散
    ice_edge = (fbm(pts * 6.0, 4, SEED + 1100) - 0.5) * 10.0
    ice = np.where(land, smoothstep(68.0, 71.0, alat + ice_edge), smoothstep(74.0, 77.0, alat + ice_edge))
    albedo = mix(albedo, snow, ice)
    ocean_mask = (~land) & (ice < 0.5)

    rgba = np.concatenate([np.clip(albedo, 0, 1) * 255, ocean_mask[:, None] * 255.0], axis=1)
    Image.fromarray(rgba.reshape(HEIGHT, WIDTH, 4).astype(np.uint8), "RGBA").save(os.path.join(OUT_DIR, "EarthAlbedo.png"))

    # --- 雲 ---
    print("  雲 ...")
    # 南北方向的頻率放大 → 雲沿東西向拉長（模擬緯向風）
    zonal = pts * np.array([[1.0], [2.5], [1.0]])
    cwarp = fbm_vec(zonal * 2.5, 4, SEED + 2000)
    c = fbm(zonal * 4.0 + 1.4 * cwarp, 7, SEED + 2100)
    band = (0.25 * np.exp(-(lat_deg / 7.0) ** 2)               # 赤道輻合帶
            + 0.20 * np.exp(-((alat - 55.0) / 12.0) ** 2)      # 中緯度風暴帶
            - 0.25 * np.exp(-((alat - 25.0) / 9.0) ** 2))      # 副熱帶晴朗帶
    cv = c + band * 0.5
    thr = weighted_quantile(cv, area, 0.40)                     # 約 60% 天空有雲（含薄雲）
    wisps = fbm(zonal * 16.0 + 2.0 * cwarp, 5, SEED + 2300)
    clouds = smoothstep(thr, thr + 0.22, cv) * (0.35 + 0.9 * wisps)   # 寬的過渡 → 薄雲、雲絲
    clouds = np.clip(clouds, 0, 1)
    Image.fromarray((clouds * 255).reshape(HEIGHT, WIDTH).astype(np.uint8), "L").save(os.path.join(OUT_DIR, "EarthClouds.png"))

    # --- 夜間燈光 ---
    print("  夜間燈光 ...")
    habitable = land & (ice < 0.1)
    pop = smoothstep(0.15, 0.4, temp) * smoothstep(0.3, 0.45, moist + 0.1) * smoothstep(0.5, 0.05, e)
    pop *= 0.5 + 0.8 * smoothstep(0.12, 0.0, e)                # 海岸、低地人口多
    cities = fbm(pts * 18.0, 4, SEED + 3000)
    sparkle = value_noise(pts * 260.0, SEED + 3100)
    lights = np.clip((cities - 0.47) * 4.0, 0, 1) ** 1.5 * pop * (0.2 + 0.8 * (sparkle > 0.6))
    lights = np.where(habitable, np.clip(lights * 2.0, 0, 1), 0.0)
    night = lights[:, None] * np.array([1.0, 0.78, 0.45])
    Image.fromarray((night * 255).reshape(HEIGHT, WIDTH, 3).astype(np.uint8), "RGB").save(os.path.join(OUT_DIR, "EarthNight.png"))

    print(f"完成：陸地 {np.average(land, weights=area):.0%}、雲量 {np.average(clouds > 0.5, weights=area):.0%}")
    print(f"輸出到 {os.path.normpath(OUT_DIR)}")


if __name__ == "__main__":
    main()
