# 太陽 Prefab 與體積日冕說明

最後更新：2026-10-27

太陽改成一個做好的 Prefab（`Assets/Resources/Prefabs/Sun.prefab`），日冕也從「一張轉向相機的平面」改成「包住太陽的一顆球，用 ray marching 算出 3D 的光」，從側面、斜角、甚至相機進到日冕裡面看都正確。

| 項目 | 原本 | 現在 |
| --- | --- | --- |
| 太陽怎麼來的 | `SolarSystemBuilder` 用 `CreatePrimitive` 產生球，`SunVisual` 在 Play 時換材質、建立日冕 | 複製 `Sun.prefab`，材質和日冕都已經在 Prefab 裡 |
| 不按 Play 能看嗎 | 不能（東西都是執行時產生的） | 能：雙擊 Prefab 就看得到表面和日冕 |
| 材質 | 執行時 `new Material(Shader.Find(...))` | `Materials/Sun.mat`、`Materials/SunCorona.mat`（Inspector 調好的值會留下來） |
| 日冕的形狀 | 一張 **Quad**，每幀轉向相機（billboard） | 一顆半徑 4 倍太陽的**球**，不用轉向 |
| 日冕的算法 | 2D：用螢幕上離中心的距離算亮度 | 3D：沿著視線取樣 32 點，把每一點的發光加起來 |
| 從側面看 | 只有俯視正確；側面、斜看時平面的感覺會露出來 | 任何角度都一樣自然 |
| 相機進到日冕裡 | Quad 被穿過，光暈突然消失 | 還是看得到四周的日冕 |
| 打包成執行檔 | `Shader.Find` 可能找不到 Shader | Shader 被材質引用，一定會打包進去 |

三個角度的預覽圖可以用 `python Assets/Docs/sun_corona_preview.py` 產生（俯視 / 斜 40° / 側視，見第 5 節）。

---

## 1. 檔案

| 檔案 | 內容 |
| --- | --- |
| `Assets/Resources/Prefabs/Sun.prefab` | 根物件 `Sun`：內建球體 + `Sun.mat` + `SunVisual`；子物件 `Corona`：內建球體（localScale 4）+ `SunCorona.mat`。都不投影、沒有 Collider |
| `Assets/Materials/Sun.mat` | `StarrySky/SunSurface`（電漿表面，和原本一樣） |
| `Assets/Materials/SunCorona.mat` | `StarrySky/SunCorona`（**改寫成體積版**） |
| `Assets/Shaders/SunCorona.shader` | 體積日冕：射線與球相交 → ray marching → 解析的表面亮環 |
| `Assets/Shaders/SunNoise.cginc` | 新增 `sunFbmOct(p, 層數)`：可以指定層數的 fBm |
| `Assets/Script/N-body/SunVisual.cs` | 新增 `surface`、`corona` 欄位；有填就直接用，沒填才自己建立 |
| `Assets/Script/N-body/SolarSystemBuilder.cs` | 依 CSV 的 prefab 欄載入（見 [Prefab載入說明.md](Prefab載入說明.md)） |
| `Assets/Docs/sun_corona_preview.py` | 照 Shader 同樣的公式在 CPU 上畫預覽圖 |

---

## 2. 怎麼接上

`Planets.csv` 太陽那一列的 prefab 欄填 `Prefabs/Sun`，按 Play 時自動載入，不用在 Inspector 拖任何東西
（原本的 Sun Prefab 欄位已移除，細節見 [Prefab載入說明.md](Prefab載入說明.md)）。

| 情況 | 結果 |
| --- | --- |
| prefab 欄有填、讀得到 | 太陽用 Prefab |
| 空白 / 讀不到 / Use Prefabs 關閉，且 Sun Visual 勾選 | 用程式產生球，`SunVisual` 自己建立材質與日冕（日冕也是新的體積版） |
| 同上，但 Sun Visual 不勾 | 純色球 |

`SolarSystemBuilder` 複製 Prefab 後，會把根物件的 `localScale` 設成 CSV 的 `displayRadius × 2`。
`Corona` 是子物件，會跟著等比例放大，所以日冕永遠是 4 倍太陽半徑。

### 怎麼看
- **不按 Play**：Project 視窗雙擊 `Assets/Resources/Prefabs/Sun.prefab` 進入 Prefab 模式，用滑鼠轉視角就能從各種角度看日冕。
  Prefab 模式下沒有 Bloom，看起來會比 Play 中暗一點。
- **Play 中**：Hierarchy 選 `Sun`（調 `SunVisual`）或 `Sun > Corona`（調材質）。

---

## 3. 原理：體積日冕

### 3.1 為什麼平面不夠
舊版用一張正對相機的 Quad，在上面畫「離中心越遠越暗」的光。俯視時沒問題，
但真的日冕是**包住太陽的一團發光氣體**。從側面看時，視線會穿過厚厚的一層氣體，邊緣應該特別亮，
平面做不出這種「厚度」的感覺；相機靠近時還會直接穿過那張平面。

### 3.2 Ray marching：沿著視線把光加起來
日冕是透明、自己發光的氣體，每一小段都會發一點光，光不會被擋住。所以一個像素看到的亮度就是：

```
亮度 = ∫ 發光量(視線上每一點) ds  ≈  Σ 發光量(pₖ) × Δs      （取樣 _Steps 次）
```

步驟（全部在 frag 裡）：

1. **換單位**：把相機位置和像素位置換到「以太陽半徑為單位、太陽中心為原點」的座標。
   Corona 球的物件空間半徑是 0.5，代表 `_CoronaSize` 倍太陽半徑，所以乘上 `2 × _CoronaSize`。
2. **光線和球相交**：光線 `p = ro + rd·t`，代入 `|p|² = R²` 得到 `t² + 2bt + (c − R²) = 0`，
   其中 `b = ro·rd`、`c = ro·ro`。兩個解 `t0`（進入）、`t1`（離開）就是要取樣的那一段。
   相機在球裡面時 `t0 < 0`，改從 0（相機本身）開始。
3. **被太陽擋住**：同樣的公式對半徑 1 的太陽算一次，光線碰到太陽表面就停。
4. **等距取樣**：在 `t0 ~ t1` 之間取 32 點，每點算 `coronaEmission(p)`，加總後乘上 `Δs × 密度`。

### 3.3 每一點的發光量
和舊版的公式一樣，只是「離中心的距離」從螢幕上的 2D 距離變成真正的 3D 距離 `r = |p|`：

| 成分 | 公式 | 效果 |
| --- | --- | --- |
| 光暈 `glow` | `exp(−(r−1) × 衰減 / wobble)` | 越外面越暗；`wobble` 依方向改變，日冕不是正圓 |
| 光芒 `filaments` | `pow(fbm(方向 × (頻率 + s)), 銳利度) × 3` | 放射狀細絲 |
| 邊緣淡出 | `smoothstep(1, 0.75, r / R)` | 看不到球殼的輪廓 |

**光芒為什麼是放射狀的？** 噪聲是在「方向 `d` × 頻率」取樣的，同一個方向上不管多遠，取樣點幾乎一樣，
所以沿著半徑方向的變化很慢 → 被拉成長條。頻率再加上 `s ≈ 3 log r`，讓細絲往外慢慢改變形狀。

**怎麼讓光芒往外流？** 讓 `s = 3 log r − 相位`，相位隨時間增加時，同樣的花紋會出現在更大的 r → 往外移動。
但相位不能一直變大（頻率會越來越偏離，光芒越來越粗或越細），所以用兩組相位：

```
ph0 = frac(t)          ph1 = frac(t + 0.5)          （各自在 0~1 之間循環）
w0  = 1 − |2·ph0 − 1|  （ph0 在 0 和 1 時權重 0，剛好是它跳回去的時候）
光芒 = 花紋(ph0) × w0 + 花紋(ph1) × (1 − w0)
```

一組要「跳回 0」的時候權重剛好是 0，由另一組接手，看不出跳動。這是做水流、flow map 的常見技巧。

### 3.4 貼表面的亮環另外算
表面那一圈亮邊只有約 1/18 個太陽半徑那麼薄，32 個取樣點的間距（約 0.25）會直接跳過它，看起來會一閃一閃。
所以改用公式直接算：光線離太陽中心最近的距離 `closest = √(c − b²)`，
亮環 = `exp(−(closest − 1) × 18) × 亮環強度`，和舊版的 2D 亮環一模一樣。

### 3.5 抖動（dither）
每個像素的第一個取樣點往前偏移一個 0~1 之間的隨機量（用像素座標算 hash）。
不抖動時，同一圈的像素取樣在同樣的位置，會出現一圈圈的條紋（banding）；抖動後條紋變成細微的雜訊，眼睛幾乎看不出來。

### 3.6 只畫背面（Cull Front）
| 設定 | 相機在球外 | 相機在球內 |
| --- | --- | --- |
| 畫正面（預設 `Cull Back`） | 看得到 | **看不到**（正面都朝外） |
| 畫背面 `Cull Front` | 看得到 | 看得到 |

畫背面還有一個好處：太陽圓盤範圍內，背面在太陽後方，被深度測試去掉，不會在太陽表面上又疊一層光。

更完整的觀念說明見 [Shader與URP入門教學.md](Shader與URP入門教學.md) 第 6.6 節。

---

## 4. 可調參數

### SunVisual（`Sun` 物件）
| 參數 | 預設 | 說明 |
| --- | --- | --- |
| Surface / Corona | Prefab 已指定 | 留空 = 執行時自己建立 |
| Corona Size | 4 | 日冕球殼半徑（倍太陽半徑）。同時設定 `Corona` 的 localScale 和材質的 `_CoronaSize` |
| Corona Intensity | 0.8 | 整體亮度 |
| Add Bloom / Threshold / Intensity / Scatter | 開 / 1 / 1 / 0.6 | 同 [太陽視覺效果說明.md](太陽視覺效果說明.md) 第 4 節 |

### SunCorona.mat
| 參數 | 預設 | 越大會怎樣 |
| --- | --- | --- |
| 內圈 / 外圈顏色 | (1.2, 0.8, 0.35) / (0.6, 0.15, 0.03) | HDR，靠近表面用內圈色 |
| 衰減速度 | 2.2 | 日冕越短 |
| 光芒數量 | 6 | 光芒越多越細 |
| 光芒銳利度 | 4 | 光芒越細、縫隙越暗（體積版會沿視線平均，所以比舊版的 2.5 高） |
| 往外流動速度 | 0.25 | 流得越快（真實時間，和模擬播放速度無關） |
| 體積密度 | 0.3 | 整體越亮 |
| 貼表面亮環強度 | 1.5 | 表面那圈亮邊越亮 |
| 取樣步數 | 32 | 越細緻、越耗效能；16 通常也夠 |

在 Prefab 模式直接改 `SunCorona.mat` 的數值會**存進材質檔**；Play 中改的是複製出來的材質，停止 Play 會還原（SunVisual 的欄位也一樣）。
Play 中調好的數值請告訴我，再寫回預設值。

亮度檢查（預覽腳本的結果）：最亮的地方是表面亮環，HDR 約 1.7；超過 1（會被 Bloom 抓到）的像素約佔日冕畫面的 5%，不會整顆白掉。

---

## 5. 在 Unity 外預覽

```
python Assets/Docs/sun_corona_preview.py [密度 0.3] [輸出 png] [光芒銳利度 4]
```

用 numpy 照 Shader 同樣的公式（同樣的 hash、噪聲、光線相交、取樣）畫出俯視、斜 40°、側視三張圖，
並印出最大亮度和超過 1 的像素比例。改 Shader 的公式時，這支腳本也要一起改。輸出圖建議放在 `Temp/`（不會進 git）。

---

## 6. 已知限制與下一步

- **效能**：每個像素 32 步 × 約 19 次噪聲。相機貼近太陽、日冕佔滿整個畫面時最吃 GPU；卡的話把取樣步數降到 16。
- **擋在日冕前面的東西**：Shader 沒有讀深度貼圖，只靠「日冕背面」做深度測試。
  行星擋在前面時，那幾個像素的日冕整條都不畫，連行星前面那一小段光也一起不見。
  目前太陽 displayRadius 0.6 → 日冕外緣 2.4 單位，水星近日點約 3.1 單位，所以實際上沒有天體會穿過日冕。
  若把 Corona Size 或太陽調大，要注意這點。
- **正交相機**：光線方向用「相機位置 → 像素」算，只適用透視相機（本專案的 Main Camera 是透視）。
- 舊版 Quad 日冕已被取代；想比較的話，Git 歷史裡還有（commit `a4dae81` 的 `Assets/Shaders/SunCorona.shader`）。
- 下一步：日珥（Particle System 或沿磁力線的弧形）、其他行星也接上 Prefab（`SolarSystemBuilder` 可以用同樣的 `Create(…, prefab)`）。
