"""
Starry Sky 背景音樂產生器：用數學合成一段「可以無縫循環」的太空氛圍音樂。

三層聲音（說明見 Assets/Docs/背景音樂說明.md，原理見 聲音合成入門教學.md）：
  1. 低音持續音（drone）：D2 附近幾個頻率差一點點的正弦波，互相干涉產生緩慢的拍頻起伏
  2. 和弦氛圍音（pad）：四個和弦慢慢輪替，每個音由多個泛音加總、三個微微走音的聲部，淡入淡出
  3. 星星閃爍（stars）：隨機時間冒出的高音鐘聲，五聲音階，長殘響
  另外加一層很輕的「太空風聲」（濾過的噪聲）和整體殘響。

無縫循環的做法：所有聲音都放在長度 L 的「環」上——
  * 超過結尾的部分（音的尾巴）折回開頭加上去
  * 一直響著的聲音（drone、LFO）頻率取成 k / L，剛好轉整數圈
  * 殘響用 FFT 做「循環摺積」，結尾的回聲自然流到開頭
所以檔案頭尾接起來沒有斷點。

用法：python Assets/Docs/space_music_gen.py
輸出：Assets/Resources/Audio/SpaceAmbience.wav
      Temp/StarrySkyPreview/space_music_preview.png（波形 + 頻譜圖，檢查用）
固定隨機種子，每次產生的結果都一樣。
"""
import os
import wave

import numpy as np
from PIL import Image, ImageDraw

# ──────────────────────────── 可調參數 ────────────────────────────
SEED = 20261101
SR = 32000                 # 取樣率（Hz）。內容多是低頻，32 kHz 已足夠，檔案比 44.1 kHz 小
CHORD_SECONDS = 24.0       # 每個和弦多長（秒）。越大越慢、越平靜
CROSSFADE = 9.0            # 和弦之間交疊淡入淡出的秒數

# 和弦（MIDI 音號，60 = 中央 C）。D 小調的 i – VI – III – VII，電影配樂常見的「遼闊」進行
CHORDS = [
    [38, 45, 53, 57, 64],  # Dm(add9)：D2 A2 F3 A3 E4
    [34, 41, 50, 57, 60],  # B♭maj9：B♭1 F2 D3 A3 C4
    [41, 48, 57, 60, 64],  # Fmaj7：F2 C3 A3 C4 E4
    [36, 43, 55, 62, 64],  # C(add9)：C2 G2 G3 D4 E4
]

DRONE_MIDI = 38            # 持續音的音高（D2 = 73.4 Hz）
DRONE_BEAT_HZ = 0.12       # 兩個持續音的頻率差 = 每秒起伏幾次。越大起伏越快

PAD_HARMONICS = 8          # 每個音加幾個泛音。越多越亮、越像弦樂
PAD_BRIGHTNESS = 1.7       # 泛音衰減的指數。越大越暗（高泛音越弱）
PAD_DETUNE_CENTS = 7.0     # 三個聲部互相走音幾音分。越大越「寬」、越飄

STAR_INTERVAL = 2.8        # 星星鐘聲平均幾秒出現一次。越小越密
STAR_MIDI = [74, 77, 79, 81, 84, 86, 89, 91, 93, 96]   # D 小調五聲音階 D F G A C（D5 ~ C7）
STAR_DECAY = (1.2, 3.0)    # 鐘聲衰減時間範圍（秒）

REVERB_SECONDS = 7.0       # 殘響長度 RT60（聲音衰減 60 dB 所需時間）。越大空間越大
REVERB_DAMP = 0.35         # 高頻殘響比低頻短多少（0 = 一樣長，越大殘響越暗）

LEVEL_DRONE = 0.22
LEVEL_PAD = 0.22
LEVEL_STARS = 0.10
LEVEL_WIND = 0.035
WET_PAD = 0.55             # 和弦送進殘響的比例
WET_STARS = 0.85           # 星星送進殘響的比例
PEAK = 0.70                # 最後正規化到的峰值（1 = 爆音邊緣；0.7 ≈ -3 dB）

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT_WAV = os.path.join(ROOT, "Assets", "Resources", "Audio", "SpaceAmbience.wav")
OUT_PNG = os.path.join(ROOT, "Temp", "StarrySkyPreview", "space_music_preview.png")

# ──────────────────────────── 基本工具 ────────────────────────────
L_SEC = CHORD_SECONDS * len(CHORDS)    # 整段循環長度（秒）
N = int(round(L_SEC * SR))             # 總取樣數
rng = np.random.default_rng(SEED)


def midi_to_hz(m):
    """MIDI 音號 → 頻率。A4（69）= 440 Hz，每升一個半音 × 2^(1/12)。"""
    return 440.0 * 2.0 ** ((m - 69) / 12.0)


def loop_freq(f):
    """把頻率取成 k / L：在整段循環裡剛好轉整數圈，頭尾的相位才接得起來。"""
    return max(1, round(f * L_SEC)) / L_SEC


def smooth_env(n, attack, release):
    """平滑的包絡線（sin² 淡入、cos² 淡出），沒有尖角就不會有「喀」聲。"""
    env = np.ones(n)
    a, r = int(attack * SR), int(release * SR)
    if a > 0:
        env[:a] = np.sin(np.linspace(0, np.pi / 2, a)) ** 2
    if r > 0:
        env[-r:] *= np.cos(np.linspace(0, np.pi / 2, r)) ** 2
    return env


def pan_gains(p):
    """等功率聲像：p = -1 全左、0 中間、+1 全右。左右功率和固定，移動時音量不會忽大忽小。"""
    ang = (p + 1) * np.pi / 4
    return np.cos(ang), np.sin(ang)


def add_circular(buf, start, sig):
    """把一段聲音加到環形緩衝區：超過結尾的部分折回開頭（無縫循環的關鍵）。sig 形狀 (2, n)。"""
    n = sig.shape[1]                    # 每段都比整段循環短（最長的和弦音約 33 秒）
    first = min(n, N - start)
    buf[:, start:start + first] += sig[:, :first]
    if first < n:
        buf[:, :n - first] += sig[:, first:]


def fft_filter(sig, gain_fn):
    """用 FFT 在頻域乘上增益曲線。FFT 本身就是循環的，濾波後頭尾一樣接得起來。"""
    spec = np.fft.rfft(sig, axis=-1)
    freqs = np.fft.rfftfreq(sig.shape[-1], 1 / SR)
    return np.fft.irfft(spec * gain_fn(freqs), n=sig.shape[-1], axis=-1)


# ──────────────────────────── 1. 低音持續音 ────────────────────────────
def make_drone():
    t = np.arange(N) / SR
    f0 = midi_to_hz(DRONE_MIDI)
    out = np.zeros((2, N))
    # (頻率, 音量, 聲像)：兩個差 DRONE_BEAT_HZ 的音產生拍頻；再加高八度與五度，聲音比較飽滿
    voices = [
        (f0, 1.0, -0.3),
        (f0 + DRONE_BEAT_HZ, 1.0, 0.3),
        (f0 * 2 - DRONE_BEAT_HZ * 0.7, 0.35, 0.0),
        (f0 * 1.5, 0.18, -0.5),   # A2（五度）
        (f0 * 0.5, 0.45, 0.0),    # D1，筆電喇叭聽不太到，耳機才有
    ]
    for f, amp, pan in voices:
        f = loop_freq(f)
        phase = rng.uniform(0, 2 * np.pi)
        s = amp * np.sin(2 * np.pi * f * t + phase)
        gl, gr = pan_gains(pan)
        out[0] += s * gl
        out[1] += s * gr
    # 很慢的音量起伏（一圈 = 整段的 1/2），讓持續音有「呼吸」
    lfo = 0.75 + 0.25 * np.sin(2 * np.pi * loop_freq(2 / L_SEC) * t)
    return out * lfo


# ──────────────────────────── 2. 和弦氛圍音 ────────────────────────────
def pad_note(freq, n, attack, release):
    """一個 pad 音：三個走音聲部 × 多個泛音，泛音亮度慢慢變化。回傳 (2, n)。"""
    t = np.arange(n) / SR
    out = np.zeros((2, n))
    for cents, pan in zip((-PAD_DETUNE_CENTS, 0.0, PAD_DETUNE_CENTS), (-0.45, 0.0, 0.45)):
        f = freq * 2 ** (cents / 1200)
        voice = np.zeros(n)
        # 泛音亮度的 LFO（每個聲部速度不同），模擬濾波器慢慢開合
        bright = 0.5 + 0.5 * np.sin(2 * np.pi * rng.uniform(0.03, 0.08) * t + rng.uniform(0, 2 * np.pi))
        for h in range(1, PAD_HARMONICS + 1):
            fh = f * h
            if fh > 5000:
                break
            amp = h ** -PAD_BRIGHTNESS
            if h > 1:
                amp = amp * (0.35 + 0.65 * bright)
            voice += amp * np.sin(2 * np.pi * fh * t + rng.uniform(0, 2 * np.pi))
        gl, gr = pan_gains(pan)
        out[0] += voice * gl
        out[1] += voice * gr
    return out * smooth_env(n, attack, release)


def make_pad():
    out = np.zeros((2, N))
    n = int((CHORD_SECONDS + CROSSFADE) * SR)
    for i, chord in enumerate(CHORDS):
        start = int((i * CHORD_SECONDS - CROSSFADE / 2) * SR) % N   # 提早一半交疊時間進來
        for j, m in enumerate(chord):
            # 低音弱一點、高音更弱，避免某個音特別突出；每個音淡入時間錯開一點，聽起來像慢慢展開
            amp = (1.0, 0.8, 0.7, 0.55, 0.45)[j]
            attack = CROSSFADE * rng.uniform(0.8, 1.2)
            note = pad_note(midi_to_hz(m), n, attack, CROSSFADE)
            add_circular(out, start, note * amp)
    return out


# ──────────────────────────── 3. 星星閃爍 ────────────────────────────
def star_note(freq, decay):
    """鐘聲：不是整數倍的泛音（1、2.76、5.40，近似金屬棒）讓聲音像「叮」而不是笛子。"""
    n = int(decay * 6 * SR)
    t = np.arange(n) / SR
    s = np.zeros(n)
    for ratio, amp, d in ((1.0, 1.0, 1.0), (2.76, 0.25, 0.45), (5.40, 0.08, 0.2)):
        if freq * ratio < 8000:   # 太高的泛音聽起來刺耳
            s += amp * np.sin(2 * np.pi * freq * ratio * t) * np.exp(-t / (decay * d))
    s *= smooth_env(n, 0.004, 0.05)   # 4 ms 起音：夠快像敲擊，又不會喀一聲
    return s


def make_stars():
    out = np.zeros((2, N))
    t = rng.exponential(STAR_INTERVAL)   # 指數分佈的間隔 = 卜瓦松過程，像雨滴一樣隨機但平均密度固定
    count = 0
    while t < L_SEC:
        m = rng.choice(STAR_MIDI)
        vel = rng.uniform(0.3, 1.0) ** 2              # 平方：小聲的多、大聲的少
        decay = rng.uniform(*STAR_DECAY)
        s = star_note(midi_to_hz(m), decay) * vel
        gl, gr = pan_gains(rng.uniform(-0.8, 0.8))
        add_circular(out, int(t * SR), np.stack([s * gl, s * gr]))
        count += 1
        t += rng.exponential(STAR_INTERVAL)
    return out, count


# ──────────────────────────── 太空風聲 ────────────────────────────
def make_wind():
    """在頻域直接產生噪聲（隨機相位）→ 本身就是循環的。兩個頻帶用慢速 LFO 交替，像濾波器在掃。"""
    t = np.arange(N) / SR
    freqs = np.fft.rfftfreq(N, 1 / SR)
    out = np.zeros((2, N))
    for center, lfo_cycles, ph in ((350.0, 3, 0.0), (900.0, 5, np.pi)):
        band = np.exp(-0.5 * (np.log(np.maximum(freqs, 1) / center) / 0.45) ** 2)   # 對數頻率上的高斯帶通
        lfo = 0.5 + 0.5 * np.sin(2 * np.pi * lfo_cycles / L_SEC * t + ph)
        for ch in range(2):
            spec = band * np.exp(1j * rng.uniform(0, 2 * np.pi, freqs.size))
            noise = np.fft.irfft(spec, n=N)
            out[ch] += noise / np.std(noise) * lfo
    return out


# ──────────────────────────── 殘響 ────────────────────────────
def make_reverb_ir():
    """人工殘響的脈衝響應：指數衰減的噪聲。高頻衰減比較快（真實空間的空氣與牆面會吸收高頻）。"""
    n = int(REVERB_SECONDS * 1.2 * SR)
    t = np.arange(n) / SR
    pre = int(0.03 * SR)                                  # 30 ms 預延遲：先聽到原音，才聽到空間
    ir = np.zeros((2, n))
    tau = REVERB_SECONDS / 6.91                           # RT60 → 指數時間常數（e^-6.91 = 10^-3 = -60 dB）
    for ch in range(2):                                   # 左右各自的噪聲 → 立體感
        noise = rng.standard_normal(n)
        low = fft_filter(noise, lambda f: 1 / (1 + (f / 1500) ** 2))
        high = noise - low
        ir[ch] = low * np.exp(-t / tau) + high * np.exp(-t / (tau * (1 - REVERB_DAMP)))
    ir[:, :pre] = 0
    ir /= np.sqrt(np.sum(ir ** 2) / 2)                    # 能量正規化：殘響不會讓總音量暴增
    return ir


def circular_reverb(sig, ir):
    """FFT 循環摺積：結尾的回聲直接繞回開頭，循環接縫聽不出來。"""
    H = np.fft.rfft(ir, n=N, axis=-1)
    return np.fft.irfft(np.fft.rfft(sig, axis=-1) * H, n=N, axis=-1)


# ──────────────────────────── 混音與輸出 ────────────────────────────
def norm_rms(x):
    return x / (np.sqrt(np.mean(x ** 2)) + 1e-12)


def main():
    print(f"循環長度 {L_SEC:.0f} 秒、{SR} Hz、{N} 取樣")
    drone = make_drone()
    print("  1. 持續音 完成")
    pad = make_pad()
    print("  2. 和弦 完成")
    stars, count = make_stars()
    print(f"  3. 星星 {count} 顆 完成")
    wind = make_wind()
    ir = make_reverb_ir()

    drone, pad, stars, wind = (norm_rms(x) for x in (drone, pad, stars, wind))
    dry = LEVEL_DRONE * drone + LEVEL_PAD * (1 - WET_PAD) * pad + LEVEL_STARS * (1 - WET_STARS) * stars + LEVEL_WIND * wind
    send = LEVEL_PAD * WET_PAD * pad + LEVEL_STARS * WET_STARS * stars + LEVEL_WIND * 0.5 * wind
    mix = dry + circular_reverb(send, ir)
    print("  殘響 完成")

    # 去掉 30 Hz 以下的超低頻（喇叭放不出來，只會吃掉音量空間）；FFT 濾波一樣是循環的
    mix = fft_filter(mix, lambda f: 1 / np.sqrt(1 + (30 / np.maximum(f, 1e-3)) ** 8))
    mix -= mix.mean(axis=1, keepdims=True)
    mix *= PEAK / np.max(np.abs(mix))

    os.makedirs(os.path.dirname(OUT_WAV), exist_ok=True)
    pcm = (np.clip(mix, -1, 1).T * 32767).astype("<i2")   # (n, 2) 交錯成 L R L R …
    with wave.open(OUT_WAV, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    print(f"輸出 {OUT_WAV}（{os.path.getsize(OUT_WAV) / 1e6:.1f} MB）")

    report(mix)
    preview(mix)


def report(mix):
    """檢查數字：響度、峰值、循環接縫。"""
    mono = mix.mean(axis=0)
    rms_db = 20 * np.log10(np.sqrt(np.mean(mix ** 2)))
    print(f"峰值 {20 * np.log10(np.max(np.abs(mix))):.1f} dBFS、平均響度 RMS {rms_db:.1f} dBFS")

    # 接縫檢查：把頭尾接起來，看接縫處相鄰取樣的跳動是不是跟其他地方一樣小。
    # 斷點（喀聲）會讓 |x[i+1] - x[i]| 突然變大，所以比較接縫和整段的最大值。
    diff = np.abs(np.diff(np.concatenate([mix, mix[:, :1]], axis=1), axis=1))
    seam = diff[:, -1].max()
    print(f"接縫跳動 {seam:.5f}（整段相鄰取樣跳動：中位數 {np.median(diff):.5f}、最大 {diff.max():.5f}）")

    # 每 4 秒的響度，看有沒有忽大忽小
    seg = int(4 * SR)
    levels = [20 * np.log10(np.sqrt(np.mean(mono[i:i + seg] ** 2)) + 1e-12) for i in range(0, N - seg + 1, seg)]
    print("每 4 秒響度（dB）：" + " ".join(f"{v:.0f}" for v in levels))


def colormap(v):
    """0~1 → 深藍 → 紫 → 橘 → 淡黃（類似 magma）。"""
    stops = np.array([[0, 0, 4], [40, 11, 84], [140, 41, 129], [229, 90, 62], [252, 200, 100], [252, 253, 191]], float)
    x = np.clip(v, 0, 1) * (len(stops) - 1)
    i = np.minimum(x.astype(int), len(stops) - 2)
    f = (x - i)[..., None]
    return (stops[i] * (1 - f) + stops[i + 1] * f).astype(np.uint8)


def preview(mix):
    """上：波形（每欄最小到最大）；下：頻譜圖（對數頻率 30 Hz ~ 8 kHz）。最後一小段是開頭接回來的部分，用來看接縫。"""
    W, WH, SH = 1200, 140, 360
    mono = mix.mean(axis=0)
    tail = int(6 * SR)
    ext = np.concatenate([mono, mono[:tail]])            # 多畫 6 秒「循環回到開頭」，接縫在紅線

    img = Image.new("RGB", (W, WH + SH + 20), (10, 10, 16))
    d = ImageDraw.Draw(img)
    cols = np.array_split(ext, W)
    peak = np.abs(ext).max()
    for x, c in enumerate(cols):
        y0 = WH / 2 - c.max() / peak * WH / 2 * 0.95
        y1 = WH / 2 - c.min() / peak * WH / 2 * 0.95
        d.line([(x, y0), (x, y1)], fill=(120, 170, 255))

    frame, hop = 4096, 1024
    win = np.hanning(frame)
    starts = np.arange(0, ext.size - frame, hop)
    spec = np.abs(np.array([np.fft.rfft(ext[s:s + frame] * win) for s in starts])).T
    freqs = np.fft.rfftfreq(frame, 1 / SR)
    db = 20 * np.log10(spec + 1e-9)
    db = (db - (db.max() - 80)) / 80                      # 顯示最亮以下 80 dB
    ys = np.geomspace(8000, 30, SH)
    rows = np.searchsorted(freqs, ys)
    xs = np.linspace(0, len(starts) - 1, W).astype(int)
    pic = colormap(db[rows][:, xs])
    img.paste(Image.fromarray(pic), (0, WH + 20))

    seam_x = int(N / ext.size * W)
    d.line([(seam_x, 0), (seam_x, WH + SH + 20)], fill=(255, 60, 60))
    for i in range(1, len(CHORDS)):
        x = int(i * CHORD_SECONDS * SR / ext.size * W)
        d.line([(x, WH + 4), (x, WH + 16)], fill=(200, 200, 200))
    os.makedirs(os.path.dirname(OUT_PNG), exist_ok=True)
    img.save(OUT_PNG)
    print(f"預覽圖 {OUT_PNG}")


if __name__ == "__main__":
    main()
