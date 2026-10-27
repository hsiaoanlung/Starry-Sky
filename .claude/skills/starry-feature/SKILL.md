---
name: starry-feature
description: Starry Sky（Unity 太陽系 N-body 專案）新增或修改功能時的標準流程：先讀專案結構、提出分層方案、實作、在 Unity 外驗證、用繁體中文寫教學文件並更新專案結構。使用者說「新增功能」「做一個…」「優化…」「/starry-feature」時使用。
---

# Starry Sky 功能開發流程

使用者在學 Unity / Shader / 物理模擬，**每個功能都要讓他看得懂原理**，不只是把東西做出來。
所有對使用者的說明與文件都用**繁體中文**。

---

## 步驟 0：先讀現況（每次都要）

1. 讀 `Assets/Docs/專案結構.md`：目錄、執行流程、腳本職責、待辦、修改紀錄。
2. 讀和這次功能相關的程式與說明文件（`Assets/Docs/*.md`）。
3. 看 `git status`，知道哪些東西還沒 commit。
4. 注意 `專案結構.md` 第 10 節的待辦與已知問題，新功能可能會碰到（例如 URP 設定）。

## 步驟 1：提出方案，先問再做

使用者問「可以怎麼做」時，**先不要寫程式**：

- 把效果拆成**幾層**（例如：發光 → 表面 → 日冕 → 粒子），每層說明「做法 + 看起來的差別」，由基礎到進階。
- 指出這個專案特有的限制（俯視相機、1 AU = 10 單位、播放速度很快、URP 設定……）。
- 給**一個明確的建議**（先做哪幾層），最後問「要我開始嗎？」。

使用者說「先試試看」就直接做建議的方案。

## 步驟 2：實作慣例

### 程式（C#）
- 放在 `Assets/Script/N-body/`，註解用繁體中文，風格跟現有檔案一致（`<summary>`、`[Header]`、`[Tooltip]`）。
- **只影響畫面的東西不碰物理**：做成獨立元件（像 `SunVisual`），由 `SolarSystemBuilder` 掛上去，並加一個 bool 開關可以關掉。
- **Inspector 參數要能在 Play 中即時調整**：在 `Update` / `LateUpdate` 每幀套用，不要只在 `Start` 讀一次。
- 執行時才加的元件，停止 Play 後數值會還原 → 在文件裡寫清楚，並提醒使用者調好的數值要告訴你，再寫回預設值。

### Shader
- 用 `CGPROGRAM` + `UnityCG.cginc`，**不加 `LightMode` 標籤** → URP 和 Built-in 都能畫。
- 光的方向從太陽來：`#include "PlanetLighting.cginc"`，用 `StarrySkyLightDir(worldPos)`（`SunVisual` 每幀設定全域 `_StarrySkySunPos`；沒有太陽時用材質的 `_LightDir`）。
- Properties 的顯示名稱用中文，數值類參數說明「越大會怎樣」。
- **HDR 亮度要節制**：只讓最亮的一小部分超過 Bloom Threshold（1）。第一版太陽設到 4 結果整顆白掉，是之前的教訓。
- 共用函式放 `.cginc`（例如 `SunNoise.cginc`、`PlanetLighting.cginc`）。

### 程序化貼圖
- 用 Python（numpy + PIL）寫在 `Assets/Docs/<名稱>_texture_gen.py`，噪聲用 `procedural_noise.py`（`fbm`、`fbm_vec`、`ridged`、`sphere_grid`……）。
- 球面貼圖用**等距長方投影 2:1**，在 3D 球面點上取樣噪聲（沒有接縫、兩極不拉扯）。
- 比例類的統計（例如 70% 是海）要用 `cos(緯度)` **面積加權**。
- 固定隨機種子，每次產生的結果一樣。

### Unity 資產（材質、Prefab、貼圖 .meta）
- 可以直接寫 YAML，**自己產生 GUID** 讓檔案互相引用；每個檔案都要有 `.meta`。
- 球體網格：`{fileID: 10207, guid: 0000000000000000e000000000000000, type: 0}`；Quad：`10210`。
- 天體 Prefab 放 `Assets/Resources/Prefabs/`（行星在 `Planets/`），根物件直徑 1、不要 Collider；在 `Planets.csv` 的 `prefab` 欄填路徑（不含 Assets/Resources/ 和副檔名），`SolarSystemBuilder` 會用 `Resources.Load` 自動載入。搬資產時 `.meta` 一起搬（GUID 不變）。
- 貼圖 meta：球面貼圖 `wrapU: 0`（Repeat）、`wrapV: 1`（Clamp）；A 通道存資料（高度）時用高品質壓縮。
- 寫資產用的一次性腳本放 scratchpad，不要放進專案。

## 步驟 3：在 Unity 外驗證

Claude 不能打開 Unity，所以要盡量在外面確認，並**誠實說明哪些沒驗證**：

- **C#**：把新檔案加進 `Assembly-CSharp.csproj`（它被 gitignore，Unity 會重新產生），然後 `dotnet build Assembly-CSharp.csproj -nologo -v q`，要 0 錯誤。
- **貼圖**：縮小拼成一張圖，用 Read 看。
- **外觀**：用 Python 照 Shader 同樣的公式畫一張球的預覽圖（光照、凹凸、大氣），看過覺得好看再接進 Unity。不滿意就先調產生器。
- **Shader 只能在 Unity 編譯**：結果報告要寫「Shader 尚未在 Unity 中確認」。
- 改到 Project Settings 的東西，提醒使用者 **File > Save Project** 才會寫進硬碟，再用 `git status` / `grep` 檢查。

## 步驟 4：文件（每個功能都要）

1. **功能說明** `Assets/Docs/<功能>說明.md`：
   - 開頭一句話說這個功能做什麼、跟原本差在哪（可以用「原本 vs 現在」的表格）。
   - 檔案清單、怎麼看 / 怎麼操作、原理（公式 + 為什麼這樣做）、可調參數表、已知限制與下一步。
2. **出現新觀念時寫教學**（像 `Shader與URP入門教學.md`）：從零開始講，有範例程式片段、內建函式表，最後附「自己動手試試」的小實驗。已有的教學就補充進去，不要重複寫。
3. **更新 `專案結構.md`**：
   - 「最後更新」日期
   - 第 2 節目錄樹（新檔案、新資料夾）
   - 第 4 節執行流程、第 5 節腳本職責（如果有新元件）
   - 第 9 節驗證狀態、第 10 節待辦
   - 最後的「YYYY-MM-DD 修改紀錄」（同一天就加在同一節，用編號）
4. 文件裡的數值要跟程式一致；改了預設值就同步改文件。

## 步驟 5：回報

- 先說結論，再用表格列出做了什麼。
- 明確列出**還沒驗證的部分**和**使用者要做的事**（例如 Save Project、在 Unity 裡看效果）。
- 告訴使用者可以調哪些參數、在哪裡調。
- 最後問下一步，**不要自己 commit**，問使用者要不要 commit。

---

## 參考：已完成的功能（當範例）

| 功能 | 程式 / Shader | 文件 |
| --- | --- | --- |
| 太陽電漿 + 日冕 + Bloom | `SunVisual.cs`、`SunSurface.shader`、`SunCorona.shader` | `太陽視覺效果說明.md` |
| 太陽 Prefab + 體積日冕 | `Sun.prefab`、`SunCorona.shader`（ray marching）、`sun_corona_preview.py` | `太陽Prefab說明.md` |
| 自轉 | `BodySpin.cs`（模擬時間 + 上限） | `自轉說明.md` |
| CSV 指定 Prefab | `PlanetSpec.cs`、`SolarSystemBuilder.LoadPrefab()` | `Prefab載入說明.md` |
| 地球 Prefab | `Earth.shader`、`earth_texture_gen.py` | `地球Prefab說明.md` |
| 其他七顆行星 Prefab | `Planet.shader`、`PlanetRing.shader`、`planet_texture_gen.py` | `行星Prefab說明.md` |
| 觀念教學 | — | `Shader與URP入門教學.md` |
