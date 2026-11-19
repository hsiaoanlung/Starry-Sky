# Nyx → Unity：資料轉換與初始設定

把 Nyx 模擬輸出的 plotfile 轉成 Unity 讀得到的格式，並在 Unity 裡用**體積渲染**顯示氣體、溫度和暗物質密度，用**粒子**顯示暗物質。12 個時間點之間會平滑內插，播放成宇宙結構形成的動畫。

```
Nyx plotfile (AMReX)  ──nyx_to_unity.py──▶  .bytes + manifest.json  ──NyxViewer──▶  Unity 場景
  build/Exec/LyA/run64/plt*                  build/unity_export/NyxLyA64/          Assets/StreamingAssets/NyxLyA64/
```

---

## 1. 資料夾內容

```
unity/
├── README_zh.md                  ← 本文件
├── tools/
│   └── nyx_to_unity.py           ← 轉換腳本（Python）
└── NyxViewer/                    ← 整個資料夾複製到 Unity 的 Assets/ 底下
    ├── Scripts/
    │   ├── NyxManifest.cs        ← manifest.json 的資料結構
    │   ├── NyxPlayer.cs          ← 載入資料、控制播放時間、畫面左上角的資訊
    │   ├── NyxVolumeRenderer.cs  ← 體積渲染元件（掛在 Cube 上）
    │   ├── NyxParticleRenderer.cs← 暗物質粒子元件（掛在同一個 Cube 上）
    │   ├── NyxOrbitCamera.cs     ← 環繞相機
    │   ├── NyxColormaps.cs       ← 色表（inferno、viridis、coolwarm）
    │   └── NyxInput.cs           ← 同時支援新版 Input System 和舊版 Input Manager
    └── Shaders/
        ├── NyxVolume.shader      ← Ray marching 體積渲染
        └── NyxParticles.shader   ← 程序化 billboard 粒子
```

---

## 2. 轉換腳本 `nyx_to_unity.py`

### 2.1 執行方式

```bash
cd D:/git_repository/Nyx
python unity/tools/nyx_to_unity.py build/Exec/LyA/run64 build/unity_export/NyxLyA64
```

| 參數 | 說明 |
|---|---|
| `run_dir` | 放 `pltNNNNN` 資料夾的地方 |
| `out_dir` | 輸出資料夾，資料夾名稱就是 Unity 裡的 `datasetFolder` |
| `--particle-stride K` | 每 K 顆粒子取 1 顆，用來減少粒子數量（例如 `4` 會剩 65,536 顆） |
| `--no-particles` | 不輸出粒子，只輸出網格 |

需要的套件：`numpy`、`yt`（4.x 版）。這台電腦已經裝好了。12 個時間點轉換約 6 秒，輸出 91 MB。

### 2.2 輸出格式

```
NyxLyA64/
├── manifest.json
├── volume/
│   ├── snap00_gas_density.bytes      # 每個 512 KB
│   ├── snap00_dm_density.bytes
│   ├── snap00_temperature.bytes
│   └── ...（12 個時間點 × 3 個物理量 = 36 個檔案）
└── particles/
    ├── snap00_pos.bytes              # 每個 3 MB
    ├── snap00_vel.bytes
    └── ...（12 × 2 = 24 個檔案）
```

**網格檔（volume）**
- 格式是 **float16**（little-endian），共 64×64×64 個值，**x 變化最快，然後 y，最後 z**。這正好是 Unity `Texture3D.SetPixelData` 需要的排列順序，可以直接載入。
- 數值**已經正規化到 [0, 1]**。換回物理量的公式是：`物理量 = min + v × (max − min)`，`min` 和 `max` 寫在 manifest 裡。
- 所有時間點用**同一組 min 和 max** 正規化，所以同一個顏色在每個時間點代表同樣的物理量。

| 物理量（field） | 儲存的值 | 範圍（這次的資料） |
|---|---|---|
| `gas_density` | log₁₀(ρ_gas / 平均值)，也就是氣體過密度 | −1.04 到 2.48 |
| `dm_density` | log₁₀(ρ_dm / 平均值)；空的格子會設成平均值的 10⁻³ | −3.00 到 2.62 |
| `temperature` | log₁₀(T / K) | 0.00 到 6.88 |

**粒子檔（particles）**
- `snapXX_pos.bytes`：每顆粒子 3 個 float32 (x, y, z)，**以盒子大小正規化到 [0, 1)**。
- `snapXX_vel.bytes`：每顆粒子 3 個 float32 (vx, vy, vz)，單位是 km/s（本動速度）。
- **已經按粒子 ID 排序**，所以第 i 顆粒子在每個時間點都是同一顆，可以直接在時間點之間內插位置。

**manifest.json**（節錄）
```json
{
  "box_size_mpc": 28.49,
  "box_size_mpc_h": 20.0,
  "grid": [64, 64, 64],
  "num_particles": 262144,
  "volume_fields": [ { "name": "gas_density", "min": -1.041, "max": 2.477, ... }, ... ],
  "snapshots": [
    { "index": 0, "plotfile": "plt00115", "z": 50.0, "a": 0.0196, "age_gyr": 0.049,
      "volume_files": ["volume/snap00_gas_density.bytes", ...],
      "particle_positions": "particles/snap00_pos.bytes", ... },
    ...
  ]
}
```

宇宙年齡 `age_gyr` 是用 manifest 裡的宇宙學參數算出來的（假設平坦的 ΛCDM 宇宙）。

---

## 3. Unity 初始設定

### 3.1 環境

| 項目 | 說明 |
|---|---|
| Unity 版本 | 這台電腦是 **Unity 6（6000.5.4f1）**。C# 腳本已經用這個版本的 DLL 編譯過，新版和舊版 Input 都是 0 錯誤。最低需求大約是 2021.3。 |
| 渲染管線 | **URP** 或 **Built-in** 都可以。**HDRP 不支援**，因為 shader 是 unlit 的 CG 寫法，粒子也沒有處理 HDRP 的相機相對座標。 |
| Input | 新版 Input System、舊版 Input Manager，或兩者都開，全部支援。`D:\Unity\Starry Sky` 這個專案是「只用新版 Input System」，也可以直接用。 |
| 平台 | Windows、macOS、Linux 的 Editor 和 Standalone 都可以。WebGL 和 Android 不行，因為資料是用 `File.ReadAllBytes` 直接從 StreamingAssets 讀的，這兩個平台不能這樣讀。 |
| GPU | 粒子需要支援 compute（DX11 以上、Vulkan 或 Metal）。你的 RX 6500 XT 沒問題。 |

> ⚠️ **Shader 還沒有在 Unity 裡實際測試過**，C# 腳本則已經確認能編譯。第一次打開時如果 Console 出現 shader 錯誤，把錯誤訊息貼給我。

### 3.2 步驟

**① 複製檔案**

| 從 | 複製到 |
|---|---|
| `D:\git_repository\Nyx\unity\NyxViewer\` | `<你的專案>\Assets\NyxViewer\` |
| `D:\git_repository\Nyx\build\unity_export\NyxLyA64\` | `<你的專案>\Assets\StreamingAssets\NyxLyA64\` |

`StreamingAssets` 這個資料夾名稱必須一字不差（Unity 的特殊資料夾），沒有的話自己建立。

**② 建立播放控制器**

1. Hierarchy 按右鍵 → **Create Empty**，命名為 `NyxPlayer`。
2. Add Component → **Nyx Player**。
3. 確認 `Dataset Folder` 是 `NyxLyA64`。

**③ 建立模擬盒子**

1. Hierarchy 按右鍵 → **3D Object → Cube**，命名為 `NyxBox`。
2. Transform：Position 設成 `(0, 0, 0)`，**Scale 設成 `(10, 10, 10)`**。也就是 1 個 Unity 單位約等於 2.85 Mpc。
3. Add Component → **Nyx Volume Renderer**。Shader、Player、色表會自動帶入。
4. Add Component → **Nyx Particle Renderer**。
5. （可選）移除 Box Collider。程式在執行時也會自動停用它。

> Cube 本地座標的 [−0.5, 0.5]³ 就是整個模擬盒子。要放大、縮小、旋轉，直接改 Cube 的 Transform 就好，體積和粒子會一起變。

**④ 設定相機**

1. 選 `Main Camera`，Add Component → **Nyx Orbit Camera**，把 `Target` 設成 `NyxBox`，`Distance` 設成 `18`。
2. 背景改成黑色：
   - **URP**：Camera → Environment → Background Type 設成 **Solid Color**，顏色設成黑色。
   - **Built-in**：Camera → Clear Flags 設成 **Solid Color**，顏色設成黑色。
3. （建議，URP）在 Volume 加上 **Bloom**（Threshold 約 0.8、Intensity 約 1），粒子會有發光效果。記得勾選相機的 **Post Processing**。

**⑤ 按 Play**

左上角會顯示目前的紅移、宇宙年齡和時間點編號。

### 3.3 操作方式

| 按鍵或滑鼠 | 功能 |
|---|---|
| `Space` | 播放 / 暫停 |
| `←` / `→` | 上一個 / 下一個時間點（同時會暫停播放） |
| `Home` | 回到第一個時間點 |
| `1` / `2` / `3` | 體積切換成 gas_density / dm_density / temperature |
| `P` | 顯示或隱藏粒子 |
| 滑鼠左鍵或右鍵拖曳 | 旋轉視角 |
| 滾輪 | 拉近或拉遠 |

不拖曳的時候，相機會自動慢慢繞著盒子轉，速度由 `Auto Rotate` 控制。

---

## 4. 參數調整

### 4.1 Nyx Player

| 參數 | 預設 | 說明 |
|---|---|---|
| `Speed` | 0.5 | 每秒前進幾個時間點。12 個時間點用 0.5 的速度約 22 秒播完 |
| `Loop` | ✔ | 播完之後從頭開始 |
| `Position` | 0 | 目前的時間位置（0 到 11，可以是小數）。暫停時可以拖這個值來手動控制時間 |
| `Preload All` | ✔ | 一開始就把全部資料讀進記憶體（約 100 MB），播放時才不會卡頓 |

> 時間軸是以「時間點編號」為單位均勻前進，不是真實的宇宙時間。早期的時間點之間隔了好幾億年，後期的時間點則比較接近。

### 4.2 Nyx Volume Renderer（體積）

| 參數 | 說明 |
|---|---|
| `Field` | `gas_density`、`dm_density` 或 `temperature` |
| `Low` / `High` | 只顯示正規化值在 [Low, High] 之間的部分。**Low 是最重要的參數**：調高會只剩下密度最高的纖維和暈 |
| `Opacity` | 整體不透明度 |
| `Gamma` | 不透明度曲線。調大會讓低密度的地方更透明，高密度的地方更突出 |
| `Brightness` | 亮度 |
| `Steps` | Ray marching 的取樣次數。64³ 的資料用 128 到 200 就夠了，太卡的話調低 |
| `Colormap` | 色表，可以在 Inspector 直接編輯漸層 |

各個物理量的建議值：

| Field | Low | Gamma | 建議色表 | 會看到什麼 |
|---|---|---|---|---|
| `gas_density` | 0.35 到 0.5 | 1.5 | inferno（預設） | 氣體構成的宇宙網纖維 |
| `dm_density` | 0.55 到 0.7 | 2.0 | inferno | 暗物質暈和纖維，比氣體更尖銳 |
| `temperature` | 0.6 到 0.75 | 1.2 | 可以換成 coolwarm | z < 10 之後，被震波加熱的熱氣體（10⁵ 到 10⁷ K） |

> 早期（z = 50 到 20）的密度幾乎是均勻的，正規化值都在 0.3 左右，會低於 Low 而看不到東西。這是正確的物理結果。隨著時間推進，結構會慢慢「浮現」出來。

### 4.3 Nyx Particle Renderer（粒子）

| 參數 | 預設 | 說明 |
|---|---|---|
| `Show` | ✔ | 顯示或隱藏 |
| `Color` | 淡藍色 | 粒子顏色 |
| `Point Size` | 0.003 | 粒子半徑，以盒子大小的比例表示 |
| `Intensity` | 0.12 | 亮度。粒子是相加混合，重疊越多越亮，所以密集的地方會自然變亮 |
| `Color By Speed` | ✘ | 勾選後改成依速度上色（viridis 色表） |
| `Speed Max` | 0（自動） | 速度色表最高點對應的速度（km/s）。0 代表自動取最大速度的 60% |

---

## 5. 技術細節

### 座標對應
- Nyx 的盒子座標 (x, y, z) ∈ [0, 28.49) Mpc 會轉成 [0, 1)，再對應到 Cube 本地座標 [−0.5, 0.5)。對應方式是 **x → X、y → Y、z → Z**。
- Unity 是左手座標系，Nyx 是右手座標系，所以畫面上的結構會是原本的**鏡像**。這不影響觀察；如果需要和論文的圖完全一致，把 Cube 的 Scale Z 設成 −10 就好。

### 時間內插
- **體積**：shader 同時讀取時間點 A 和 B 的 Texture3D，用 `_Blend` 做線性混合。
- **粒子**：每顆粒子的位置在 A 和 B 之間線性內插，並考慮**週期性邊界**，也就是走最短路徑。如果粒子從盒子的一邊穿出去、從另一邊回來，不會出現橫越整個盒子的拉線。
- 早期兩個時間點之間隔很久，粒子實際上走的是曲線，內插只是直線近似。要更平滑的話，可以讓 Nyx 輸出更多時間點（在 `inputs` 的 `nyx.plot_z_values` 加更多紅移），再重新轉換。

### 在自己的程式裡讀取物理量
```csharp
var player = FindFirstObjectByType<Nyx.NyxPlayer>();
var info = player.Manifest.volume_fields[player.FieldIndex("temperature")];
float v = 0.8f;                                   // 從 texture 讀到的正規化值
float log10T = info.min + v * (info.max - info.min);
float T = Mathf.Pow(10f, log10T);                 // 單位 K
```

### 效能
| 項目 | 數量 | 記憶體 |
|---|---|---|
| Texture3D | 36 張 64³ RHalf | 18 MB |
| ComputeBuffer | 24 個，每個 262,144 × float3 | 75 MB |
| 每幀繪製 | 1 個體積 + 1 次 DrawProcedural（約 157 萬個頂點） | — |

---

## 6. 之後想換資料的時候

1. 用 Nyx 跑新的模擬，例如 128³ 或輸出更多時間點。
2. 執行 `python unity/tools/nyx_to_unity.py <新的 run 資料夾> build/unity_export/<新名稱>`。
3. 把輸出複製到 `Assets/StreamingAssets/<新名稱>/`，再把 `NyxPlayer` 的 `Dataset Folder` 改成新名稱。

> 128³ 的資料：每張 Texture3D 4 MB，粒子 210 萬顆（每個時間點 24 MB）。如果卡頓，可以用 `--particle-stride 4` 減少粒子數。

---

## 7. 可以繼續發展的方向

- **UI**：用 UI Toolkit 或 uGUI 做時間軸拖曳條、欄位選單、色表選擇。
- **等值面**：用 Marching Cubes 把高密度區域轉成 Mesh，可以加光影。
- **暈追蹤**：在 Python 端找出密度峰值（暈）的位置，在 Unity 裡標記出來。
- **VR**：把盒子放大到房間的尺寸，就能「走進」宇宙網裡面。
- **方案 B 或 C**：之後可以讓 Unity 即時連線 Nyx，或在 Unity 用 compute shader 自己跑簡化版的 PM 模擬。
