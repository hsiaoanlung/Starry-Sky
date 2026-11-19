# Nyx 宇宙模擬檢視器（Nyx.unity）

新的獨立場景 `Assets/Scenes/Nyx.unity`，用來看 **Nyx 宇宙學模擬**的結果：一個邊長 20 Mpc/h（約 2850 萬秒差距）的宇宙方塊，從紅移 z = 50（宇宙年齡約 5 千萬年）演化到後期，可以看到原本幾乎均勻的物質慢慢聚集成**宇宙網**（纖維 + 暈）。

和太陽系場景 `N-body.unity` 完全獨立，兩邊的程式互不影響。

| | N-body.unity（太陽系） | Nyx.unity（宇宙網） |
| --- | --- | --- |
| 尺度 | 太陽系，AU | 宇宙大尺度結構，Mpc（百萬秒差距） |
| 物理 | Unity 裡即時算重力 | 事先在 Nyx（超級電腦等級的 C++ 程式）算好，Unity 只負責**播放** |
| 畫法 | 球體 + Shader | **體積渲染**（ray marching 3D 貼圖）+ 26 萬顆暗物質**粒子** |

更完整的原始說明（資料轉換、格式、技術細節）在 `Assets/Script/NyxViewer/README_zh.md`，物理原理在 `Assets/Script/NyxViewer/Nyx模擬原理.md`。

---

## 1. 檔案

| 檔案 | 說明 |
| --- | --- |
| `Assets/Scenes/Nyx.unity` | 場景（2026-11-19 照 README 第 3.2 節建立） |
| `Assets/StreamingAssets/NyxLyA64/` | 資料：`manifest.json` + 12 個時間點的網格（`volume/`）與粒子（`particles/`），共 91 MB |
| `Assets/Script/NyxViewer/Scripts/` | `NyxPlayer`（讀資料、控制時間）、`NyxVolumeRenderer`、`NyxParticleRenderer`、`NyxOrbitCamera`、`NyxColormaps`、`NyxInput`、`NyxManifest` |
| `Assets/Script/NyxViewer/Shaders/` | `Nyx/Volume`（體積 ray marching）、`Nyx/Particles`（程序化 billboard 粒子） |

> **為什麼資料要放 `StreamingAssets` 而不是 `Resources`？**
> `NyxPlayer` 用 `File.ReadAllBytes(Application.streamingAssetsPath + ...)` 直接讀原始檔。
> `StreamingAssets` 是 Unity 的特殊資料夾：裡面的檔案**原封不動**複製到執行檔旁邊，可以用一般的檔案路徑讀。
> `Resources` 裡的檔案會被 Unity 轉成內部格式打包，只能用 `Resources.Load` 讀，`File.ReadAllBytes` 找不到。
> 所以資料原本放在 `Assets/Resources/NyxLyA64/` 會讀不到，已經連同 `.meta` 搬到 `Assets/StreamingAssets/NyxLyA64/`。
> 順帶的好處：91 MB 不會被打包進太陽系場景的執行檔。

---

## 2. 場景內容

| 物件 | 元件 | 設定 |
| --- | --- | --- |
| **Main Camera** | Camera | 背景純黑（Solid Color）、Near 0.1、Far 500、HDR |
| | Nyx Orbit Camera | Target = NyxBox、Distance 18、自動旋轉 6°/秒 |
| | Universal Additional Camera Data | URP 相機設定（Post Processing 先關閉） |
| **NyxPlayer** | Nyx Player | Dataset Folder = `NyxLyA64`、Speed 0.5（每秒 0.5 個時間點，約 22 秒播完）、Loop、Preload All |
| **NyxBox** | Mesh Filter（Cube）+ Mesh Renderer | Scale **(10, 10, 10)**：1 Unity 單位 ≈ 2.85 Mpc；沒有 Collider |
| | Nyx Volume Renderer | Field = `gas_density`、Low 0.35、Gamma 1.5、Brightness 1.5、Steps 160 |
| | Nyx Particle Renderer | 淡藍色、Point Size 0.003、Intensity 0.12 |

場景裡沒有燈光：體積和粒子都是 **unlit**（自己發光），不需要光源。

---

## 3. 怎麼看

1. Project 視窗打開 `Assets/Scenes/Nyx.unity`。
2. 按 Play。左上角會顯示目前的紅移 z、宇宙年齡、時間點編號。
3. Console 應該出現：`[Nyx] Nyx LyA: 12 snapshots, grid 64x64x64, 262144 particles, box 28.49 Mpc`。

| 按鍵 / 滑鼠 | 功能 |
| --- | --- |
| `Space` | 播放 / 暫停 |
| `←` / `→` | 上一個 / 下一個時間點 |
| `Home` | 回到開頭 |
| `1` / `2` / `3` | 體積換成 氣體密度 / 暗物質密度 / 溫度 |
| `P` | 顯示 / 隱藏粒子 |
| 左鍵或右鍵拖曳 | 旋轉 |
| 滾輪 | 拉近拉遠 |

> **一開始看起來幾乎是空的，這是正確的。** 早期（z = 50～20）宇宙非常均勻，密度都在平均值附近，低於 `Low` 門檻所以不顯示，只看得到均勻分布的藍色粒子。隨著時間推進，纖維和團塊會慢慢「浮現」。

---

## 4. 原理（簡短版）

### 4.1 體積渲染（ray marching）
- 方塊表面每個像素射出一條視線，在方塊裡**沿線取樣 160 次** 3D 貼圖（64³ 的密度）。
- 每個取樣點：密度 → 經過 `Low`/`High` 正規化 → 查色表（inferno）得到顏色，`pow(d, Gamma)` 得到不透明度。
- 由前往後累加（front-to-back compositing），累積不透明度到 98% 就提早停止。
- 時間內插：同時讀時間點 A 和 B 的貼圖，用 `_Blend` 混合，所以動畫是連續的。
- 這和太陽的體積日冕（`SunCorona.shader`）是同一種技巧，見 [Shader與URP入門教學.md](Shader與URP入門教學.md) 6.6 節。

### 4.2 粒子
- 26 萬顆粒子的位置放在 GPU 的 `ComputeBuffer`，用**一次** `Graphics.DrawProcedural` 畫出 26 萬 × 6 個頂點，頂點 shader 用 `SV_VertexID` 算出是第幾顆粒子的哪個角。
- 加法混合（`Blend One One`）：越密的地方疊越多，自然變亮。
- 週期性邊界：粒子從一邊出去會從另一邊回來，內插走最短路徑，不會拉出橫越整個方塊的線。

---

## 5. 可調參數

Play 中可以直接在 Inspector 調（每幀都會套用），停止後會還原；調好的數值告訴 Claude 寫回場景。

| 元件 | 參數 | 越大會怎樣 |
| --- | --- | --- |
| Nyx Volume Renderer | `Low` | **最重要**：只剩下更密的纖維和暈，畫面更乾淨 |
| | `Gamma` | 低密度更透明、高密度更突出 |
| | `Opacity` / `Brightness` | 更不透明 / 更亮 |
| | `Steps` | 更細緻但更耗 GPU（64³ 資料 128～200 就夠） |
| Nyx Particle Renderer | `Intensity` / `Point Size` | 粒子更亮 / 更大 |
| | `Color By Speed` | 勾選後依速度上色（viridis） |
| Nyx Player | `Speed` | 播得更快；暫停時可以拖 `Position` 手動控制時間 |

建議值（README 4.2）：

| Field（按鍵） | Low | Gamma | 會看到 |
| --- | --- | --- | --- |
| `gas_density`（1） | 0.35～0.5 | 1.5 | 氣體構成的宇宙網纖維 |
| `dm_density`（2） | 0.55～0.7 | 2.0 | 暗物質暈和纖維，比氣體尖銳 |
| `temperature`（3） | 0.6～0.75 | 1.2 | 被震波加熱的熱氣體（z < 10 之後） |

> 切換 Field 時 `Low` 不會跟著換，換到 `dm_density` 時記得把 `Low` 調高到 0.6 左右。

---

## 6. 驗證狀態與已知限制

| 項目 | 狀態 |
| --- | --- |
| C# 編譯 | `dotnet build` 0 錯誤 |
| 場景檔 | 手寫 YAML，**尚未在 Unity 中打開確認** |
| Shader（Nyx/Volume、Nyx/Particles） | **尚未在 Unity 中編譯確認**，有錯誤請把 Console 訊息貼給 Claude |
| 外觀 | 尚未看過 |

- **Bloom 還沒加**（README 列為可選）：相機的 Post Processing 先關著。想讓粒子發光，之後可以加 Volume + Bloom（Threshold 0.8、Intensity 1）。
- 新版 Input System 專案也能用（`NyxInput` 已處理）。
- 只能在 Windows / macOS / Linux 執行；WebGL、Android 不能用 `File.ReadAllBytes` 讀 StreamingAssets。
- 這個場景**尚未加入 Build Settings**，要打包時再加。
- Unity 是左手座標、Nyx 是右手座標，畫面是原始資料的鏡像；要和論文圖一致，把 NyxBox 的 Scale Z 設成 −10。
