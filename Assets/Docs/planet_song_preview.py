"""
行星之歌預覽：用和 PlanetSong.cs 同樣的公式，算出每顆行星的音高，並合成一段試聽。

音高公式（克卜勒《世界的和諧》的想法：行星繞太陽的角速度 → 音高）：
    f = 參考音高 × (n / n_ref)^γ × (ω / n)
      n     = 平均角速度（平均運動），n = sqrt(μ / a³)
      n_ref = 地球的平均角速度 2π / 365.25 天
      γ     = 行星之間差距的壓縮（1 = 真實比例）
      ω     = 瞬時角速度 |r × v| / r²：近日點快 → 音高高，遠日點慢 → 音高低（這部分不壓縮，是真實比例）

用法：python Assets/Docs/planet_song_preview.py
輸出：印出音高表；Temp/StarrySkyPreview/planet_song_preview.wav（32 秒試聽，每 4 秒換一顆行星當主角）
"""
import csv
import math
import os
import wave

import numpy as np

# 和 PlanetSong.cs 的預設值一致
REFERENCE_PITCH = 587.33     # 地球的音高（D5）
REFERENCE_PERIOD = 365.25    # 地球的公轉週期（天）
SPREAD = 0.4                 # γ
BACKGROUND_LEVEL = 0.15      # 不是主角的行星音量
BRIGHTNESS = 0.35            # 泛音量

DEMO_DAYS_PER_SECOND = 20    # 試聽用的播放速度（比 Unity 預設的 1 天/秒快，32 秒內才聽得出音高起伏）
DEMO_SECONDS = 32
SR = 32000
G = 2.959122082855911e-4     # AU³ / (太陽質量 · 天²)，和 NBodySimulator.G 一樣

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CSV = os.path.join(ROOT, "Assets", "Resources", "Planets.csv")
OUT_WAV = os.path.join(ROOT, "Temp", "StarrySkyPreview", "planet_song_preview.wav")

NAMES_ZH = {"Mercury": "水星", "Venus": "金星", "Earth": "地球", "Mars": "火星",
            "Jupiter": "木星", "Saturn": "土星", "Uranus": "天王星", "Neptune": "海王星"}
NOTE = ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"]


def note_name(f):
    m = 69 + 12 * math.log2(f / 440)
    r = round(m)
    cents = (m - r) * 100
    return f"{NOTE[r % 12]}{r // 12 - 1}{cents:+.0f}"


def load_planets():
    out = []
    with open(CSV, encoding="utf-8") as fh:
        for row in csv.DictReader(fh):
            if row["parent"] == "Sun":
                out.append((row["name"], float(row["mass"]), float(row["a"]), float(row["e"])))
    return out


def pitch(n, omega):
    n_ref = 2 * math.pi / REFERENCE_PERIOD
    return REFERENCE_PITCH * (n / n_ref) ** SPREAD * (omega / n)


def main():
    planets = load_planets()
    print(f"{'行星':<6}{'週期(天)':>10}{'平均':>16}{'近日點':>16}{'遠日點':>16}{'起伏(音分)':>12}")
    rows = []
    for name, m, a, e in planets:
        mu = G * (1 + m)
        n = math.sqrt(mu / a ** 3)
        period = 2 * math.pi / n
        # 角動量守恆：ω = h / r²，近日點 r = a(1-e)、遠日點 r = a(1+e)
        k = 1 / (1 - e * e) ** 1.5
        w_peri, w_aph = n * (1 + e) ** 2 * k, n * (1 - e) ** 2 * k
        f_mean, f_peri, f_aph = pitch(n, n), pitch(n, w_peri), pitch(n, w_aph)
        cents = 1200 * math.log2(f_peri / f_aph)
        rows.append((name, mu, a, e, n))
        print(f"{NAMES_ZH[name]:<6}{period:>10.0f}"
              f"{f_mean:>8.0f} Hz {note_name(f_mean):<6}{f_peri:>8.0f} Hz {note_name(f_peri):<6}"
              f"{f_aph:>8.0f} Hz {note_name(f_aph):<6}{cents:>10.0f}")

    # 試聽：每顆行星沿克卜勒軌道跑（解克卜勒方程式求真近點角），每 4 秒換一顆當主角
    N = DEMO_SECONDS * SR
    t = np.arange(N) / SR
    days = t * DEMO_DAYS_PER_SECOND
    mix = np.zeros(N)
    gains = []
    for i, (name, mu, a, e, n) in enumerate(rows):
        M = n * days + i * 0.7                       # 平近點角（各行星錯開起點）
        E = M.copy()
        for _ in range(8):                           # 牛頓法解 E - e sin E = M
            E -= (E - e * np.sin(E) - M) / (1 - e * np.cos(E))
        r = a * (1 - e * np.cos(E))
        omega = n * a * a * math.sqrt(1 - e * e) / r ** 2   # h / r²
        f = pitch(n, omega)
        phase = 2 * np.pi * np.cumsum(f) / SR
        voice = (np.sin(phase) + BRIGHTNESS * 0.5 * np.sin(2 * phase) + BRIGHTNESS * 0.25 * np.sin(3 * phase)) / (1 + 0.75 * BRIGHTNESS)
        # 主角音量 1，其他 BACKGROUND_LEVEL，用 0.5 秒平滑切換
        focus = ((t // 4).astype(int) == i).astype(float)
        k = int(0.5 * SR)
        c = np.concatenate([[0], np.cumsum(focus)])   # 移動平均：用累加和相減，比 np.convolve 快很多
        lo = np.clip(np.arange(N) - k // 2, 0, N)
        hi = np.clip(np.arange(N) + k // 2, 0, N)
        focus = (c[hi] - c[lo]) / k
        g = BACKGROUND_LEVEL + (1 - BACKGROUND_LEVEL) * focus
        gains.append(g)
        mix += voice * g
    mix /= np.maximum(1, np.sum(gains, axis=0))      # 和 PlanetSong.cs 一樣：總增益超過 1 時壓回來
    env = np.minimum(1, np.minimum(t / 1.0, (DEMO_SECONDS - t) / 1.0))
    mix *= env * 0.5
    print(f"試聽峰值 {20 * np.log10(np.abs(mix).max()):.1f} dBFS（每 4 秒換主角：水星 → 海王星，播放速度 {DEMO_DAYS_PER_SECOND} 天/秒）")

    os.makedirs(os.path.dirname(OUT_WAV), exist_ok=True)
    pcm = (np.clip(mix, -1, 1) * 32767).astype("<i2")
    with wave.open(OUT_WAV, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"輸出 {OUT_WAV}")


if __name__ == "__main__":
    main()
