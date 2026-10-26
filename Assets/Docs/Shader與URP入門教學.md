# Shader 與 URP 入門教學

最後更新：2026-10-26

這份文件從零開始介紹太陽效果用到的觀念：渲染管線、材質與 Shader、HDR 與 Bloom、Shader 程式的結構、噪聲，以及日冕用到的 billboard 和混合模式。
太陽效果本身的參數與實作細節請看 [太陽視覺效果說明.md](太陽視覺效果說明.md)。

---

## 0. 原本的作法 vs 現在的作法

| 項目 | 原本 | 現在 |
| --- | --- | --- |
| 太陽材質 | 預設 **Lit** 材質，`material.color = 黃色` | 自訂 **Unlit** Shader `StarrySky/SunSurface` |
| 亮度從哪來 | 被 Directional Light 照亮，背光面會變暗 | 自己發光，不管光源在哪都一樣亮 |
| 顏色範圍 | 0～1（一般顏色） | **HDR**，可以超過 1（例如 4.0） |
| 表面 | 單一顏色，靜止 | 噪聲紋理，隨時間流動 |
| 球外 | 什麼都沒有 | 日冕 Quad（Additive 疊加光芒） |
| 後處理 | 沒有 | URP **Bloom**：亮度 > 1 的地方往外溢光 |
| 渲染管線 | URP Asset 遺失，實際上退回 Built-in | 重新建立 URP Asset |

一句話：原本是「被照亮的黃球」，現在是「自己發光、表面會動、外面有光暈的恆星」。

---

## 1. 渲染管線（Render Pipeline）

**渲染管線**是 Unity「把場景畫到螢幕上」的整套流程：先畫哪些物件、怎麼算光照、要不要做後處理。

| 管線 | 說明 |
| --- | --- |
| **Built-in** | Unity 傳統的管線，不用任何設定就有 |
| **URP**（Universal Render Pipeline） | 新的可程式化管線，效能好、支援 Volume 後處理（Bloom 等）。本專案使用 |
| HDRP | 高畫質管線，用在高階 PC / 主機，本專案不用 |

### 1.1 URP 需要哪些檔案

| 檔案 | 作用 |
| --- | --- |
| **URP Asset**（`Assets/Settings/PC_RPAsset.asset`） | 管線的總設定：HDR、陰影距離、抗鋸齒、渲染比例……。**沒有它，Unity 就不會用 URP** |
| **Renderer**（`PC_RPAsset_Renderer.asset`） | URP Asset 底下的「畫法」：渲染路徑（Forward / Deferred）、Renderer Feature。URP Asset 的 Renderer List 要指向它 |
| Global Settings（`UniversalRenderPipelineGlobalSettings.asset`） | 全專案共用的設定，例如預設 Volume Profile |
| `DefaultVolumeProfile.asset` | 預設的後處理設定（目前是空的） |

### 1.2 在哪裡指定要用哪個管線

| 位置 | 意義 |
| --- | --- |
| Project Settings > **Graphics** > Default Render Pipeline | 預設用哪個 URP Asset |
| Project Settings > **Quality** > 每個品質等級的 Render Pipeline Asset | 該品質等級用哪個。**有指定的話會覆蓋 Graphics 的設定** |

本專案有 `Mobile`、`PC` 兩個品質等級，兩個都要指定，否則切到另一個等級時會退回 Built-in。

> 這次的狀況：URP Asset 被刪掉後，設定指向一個不存在的檔案（Inspector 會顯示 Missing），
> Unity 就退回 Built-in。畫面看起來差不多，但 Bloom 等 URP 後處理全部失效。

### 1.3 設定什麼時候寫進硬碟
在 Project Settings 改的東西會先存在記憶體裡，**要按 File > Save Project（或關閉 Unity）才會寫進 `ProjectSettings/*.asset`**。
git 只看得到硬碟上的檔案，所以改完設定要先 Save Project 再 commit。

---

## 2. 材質（Material）與 Shader

```
Mesh（形狀）  +  Material（材質）  →  畫面
                    │
                    ├─ Shader：「怎麼算每個像素的顏色」的程式
                    └─ 參數：顏色、貼圖、數值（Shader 的 Properties）
```

- **Shader** 是跑在 GPU 上的小程式，決定每個像素的顏色。
- **Material** 是「Shader + 一組參數值」。同一個 Shader 可以做出很多材質（例如紅色、藍色）。
- 程式裡 `new Material(shader)` 會建立新材質；`renderer.material = ...` 指定給物件。

### 2.1 Lit 與 Unlit

| | Lit（受光照） | Unlit（不受光照） |
| --- | --- | --- |
| 顏色 | 依光源方向、法線、陰影計算 | Shader 回傳什麼就是什麼 |
| 用途 | 行星、一般物體 | 發光體、UI、特效 |

太陽自己就是光源，不應該被別的燈「照亮」，所以用 Unlit。行星維持 Lit。

---

## 3. HDR 與 Bloom

### 3.1 HDR（High Dynamic Range）
一般顏色每個通道是 0～1，1 就是最亮的白。**HDR** 允許顏色超過 1，例如 `(1.5, 1.1, 0.5)` 的紅色通道「比最亮還亮 1.5 倍」。
螢幕本身還是只能顯示到 1，超過的部分會被截掉；但這些「超過的亮度」可以交給後處理使用。

- URP Asset 要勾 **HDR**，畫面才會用浮點緩衝區保存超過 1 的值。
- Shader Properties 前面加 `[HDR]`，Inspector 的顏色選取器才會出現 Intensity 滑桿。

### 3.2 後處理（Post-processing）
整個畫面畫完之後，再對這張圖做一次加工，例如 Bloom、色調映射、景深、暈影。

### 3.3 Bloom
Bloom 找出畫面上**亮度超過 Threshold** 的像素，把它模糊、往外擴散，再疊回畫面，
做出「很亮的東西會溢光」的效果，就像相機或眼睛看太陽一樣。

| 參數 | 說明 |
| --- | --- |
| Threshold | 亮度超過多少才會溢光。設 1：只有 HDR 的部分會發光，一般物體不受影響 |
| Intensity | 溢光強度 |
| Scatter | 擴散範圍（0～1） |

這就是為什麼太陽亮部要設到 1 以上：**沒有 HDR，Bloom 抓不到；沒有 Bloom，HDR 只會截成白色**。兩者要一起用。

> **亮度要節制**：第一版太陽亮部設到 4、Bloom Intensity 1.5，結果整顆太陽溢成一團白光，電漿紋理都被蓋掉。
> 現在亮部只稍微超過 1（1.5）、Bloom Threshold 1、Intensity 1，只有最亮的電漿斑塊會溢光。
> 原則：**只讓「最亮的一小部分」超過 Threshold**，Bloom 才是點綴而不是蓋住細節。

### 3.4 Volume 系統
URP 的後處理是用 **Volume** 控制的：

```
GameObject
 └─ Volume 元件（Global = 整個場景都套用）
      └─ Volume Profile（資產）
           ├─ Bloom（Override）
           ├─ Tonemapping
           └─ ...
```

- **Volume Profile** 是一組後處理效果的設定。
- **Override**：Profile 裡的參數要勾選左邊的方塊才會生效；程式裡的 `bloom.threshold.Override(1f)` 就是「勾選並設值」。
- **Camera 要勾 Post Processing**，否則 Volume 對這台相機無效。

`SunVisual.SetupBloom()` 在執行時用程式做了上面這些事，所以不用手動在場景裡加 Volume。
如果想在編輯器裡調 Bloom：Play 時在 Hierarchy 找到 `Sun Bloom Volume`，點它就能即時調整。

---

## 4. Shader 程式的結構

以 `SunCorona.shader` 為例（簡化）：

```hlsl
Shader "StarrySky/SunCorona"                    // ① 名稱：Shader.Find 用它找
{
    Properties                                   // ② 會出現在材質 Inspector 的參數
    {
        [HDR] _InnerColor ("內圈顏色", Color) = (1.2, 0.8, 0.35, 1)
        _Falloff ("衰減速度", Float) = 2.2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" }         // ③ 什麼時候畫
        Blend One One                            // ④ 怎麼跟背景混合
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert                  // ⑤ 頂點函式
            #pragma fragment frag                // ⑥ 片段（像素）函式
            #include "UnityCG.cginc"

            float4 _InnerColor;                  // ⑦ 要宣告同名變數才拿得到 Properties 的值
            float _Falloff;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)                  // 每個頂點跑一次
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);   // 物件座標 → 螢幕
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target       // 每個像素跑一次，回傳顏色
            {
                return _InnerColor;
            }
            ENDCG
        }
    }
}
```

### 4.1 兩個階段

```
Mesh 的每個頂點 ──► vert() ──► GPU 把三角形填滿，頂點資料自動插值 ──► 每個像素 frag() ──► 顏色
```

| 函式 | 執行次數 | 負責 |
| --- | --- | --- |
| `vert` | 每個頂點一次（球約 500 次） | 算出頂點在螢幕上的位置，順便傳資料給 frag |
| `frag` | 每個像素一次（可能幾十萬次） | 算出這個像素的顏色。**效果幾乎都寫在這裡** |

### 4.2 語意（Semantics）
冒號後面的 `POSITION`、`TEXCOORD0`、`SV_POSITION`、`SV_Target` 告訴 GPU 這個變數是什麼：

| 語意 | 意思 |
| --- | --- |
| `POSITION` | Mesh 的頂點座標 |
| `NORMAL` | 頂點法線 |
| `TEXCOORD0` | 第 0 組 UV；在 v2f 裡則是「隨便一個插值通道」，可以放任何資料 |
| `SV_POSITION` | vert 輸出的螢幕位置（必要） |
| `SV_Target` | frag 輸出的顏色 |

### 4.3 常用的內建變數與函式

| 名稱 | 意思 |
| --- | --- |
| `_Time.y` | 遊戲開始到現在的秒數，用來做動畫 |
| `_WorldSpaceCameraPos` | 相機的世界座標 |
| `unity_ObjectToWorld` | 物件 → 世界座標的矩陣 |
| `UnityObjectToClipPos(v)` | 物件座標 → 螢幕裁切座標 |
| `UnityObjectToWorldNormal(n)` | 法線轉成世界座標 |
| `lerp(a, b, t)` | 線性插值：t=0 → a，t=1 → b |
| `saturate(x)` | 夾在 0～1 |
| `smoothstep(a, b, x)` | x 從 a 到 b 時，平滑地從 0 變到 1 |
| `frac(x)` / `floor(x)` | 小數部分 / 整數部分 |
| `dot(a, b)` | 內積；兩個單位向量的內積 = 夾角的 cos |

### 4.4 為什麼這兩支 Shader 在 URP 和 Built-in 都能用
URP 官方的寫法是 `HLSLPROGRAM` + URP 的函式庫，而且要加 `"LightMode"` 標籤。
但 Unlit Shader **不加 LightMode 標籤** 時，URP 會當成 `SRPDefaultUnlit` 照樣畫出來；`UnityCG.cginc` 在 URP 裡也還能用。
所以這種寫法兩邊都能跑，對學習和這次的除錯比較方便。
缺點是不支援 URP 的 SRP Batcher 合批，但太陽只有兩個物件，沒有影響。

---

## 5. 噪聲（Noise）：程式產生的自然紋理

電漿、雲、火焰這類「隨機但連續」的圖案，不用貼圖，而是用**噪聲函式**即時算出來。

### 5.1 Value noise
1. 在整數格點上各給一個「看起來隨機」的值（`sunHash`：同一個輸入永遠得到同一個輸出）。
2. 格點之間用平滑插值連起來。

結果是一片平滑起伏的值（0～1）。3D 版本就是在立方體的 8 個角之間插值。

### 5.2 fBm（分形布朗運動）
單一層噪聲太平滑。把好幾層疊起來：

```
fbm(p) = 0.5·noise(p) + 0.25·noise(2p) + 0.125·noise(4p) + ...
```

每層**頻率 ×2、振幅 ÷2**：大的起伏加上越來越細的細節，就像自然界的山脈、雲朵。這種每一層叫一個 octave。

### 5.3 Domain warping（座標扭曲）
```
warp = (fbm(p), fbm(p + 偏移1), fbm(p + 偏移2))
n    = fbm(p + 強度 × warp)
```
先用噪聲把「取樣位置」推歪，再取一次噪聲。紋理會被拉長、捲曲，看起來像流體——這就是電漿感的來源。
讓 warp 隨 `_Time` 變化，紋理就會像在翻騰。

### 5.4 讓噪聲動起來
最簡單的方法：把時間加進座標，例如 `fbm(p + float3(0, 0, t))`。在 3D 噪聲裡沿某一軸移動，看起來就是平滑的變形。

### 5.5 黏在球上
`SunSurface` 用**物件空間座標**（`v.vertex`）取樣噪聲，而不是世界座標。
所以太陽移動時，紋理跟著球走，不會像「球在一張固定的花紋裡滑動」。

---

## 6. 日冕用到的技巧

### 6.1 Billboard（看板）
一張平面永遠轉向相機，從哪個角度看都是正面。常用在粒子、光暈、遠處的樹。
`SunVisual.LateUpdate()` 每幀做 `corona.rotation = cam.transform.rotation`。
放在 `LateUpdate` 是因為相機在 `CameraFocus.LateUpdate` 才移動完；即使順序剛好相反，相機是平移、轉向不變，所以不會有問題。

### 6.2 極座標
日冕是從中心往外放射的，用「角度 + 半徑」思考比用 x、y 方便：
- 半徑 `r`：決定亮度衰減 `exp(-(r-1) × 衰減)`。
- 角度：決定光芒。用 `(cos θ, sin θ)` 而不是 θ 當噪聲座標，繞一圈回到原點時才不會有接縫。

### 6.3 Render Queue（繪製順序）

| Queue | 數值 | 用途 |
| --- | --- | --- |
| Geometry | 2000 | 不透明物體（行星、太陽表面）先畫 |
| Transparent | 3000 | 半透明、發光特效後畫，才能疊在前面的東西上 |

### 6.4 深度緩衝（Z-buffer）
GPU 為每個像素記錄「目前最近的物體有多遠」。
- **ZTest**（預設開）：比已經畫的東西還遠，就不畫。日冕 Quad 在太陽中心，被太陽球體擋住的部分自動不畫，所以只看得到球外的光芒。
- **ZWrite Off**：日冕畫完不寫入深度，避免透明的部分擋住後面的東西。

### 6.5 混合模式（Blend）

```
最終顏色 = 新顏色 × SrcFactor + 原本顏色 × DstFactor
```

| 寫法 | 名稱 | 效果 |
| --- | --- | --- |
| `Blend SrcAlpha OneMinusSrcAlpha` | Alpha 混合 | 一般半透明（玻璃、煙） |
| `Blend One One` | **Additive（相加）** | 只會變亮，不會擋住東西。光、火、日冕 |

日冕用 Additive：黑色 = 加 0 = 完全透明，所以 frag 只要回傳「要加多少光」，不必處理 alpha。

---

## 7. 自己動手試試

1. Play 後選 **Sun**，展開材質，拖動「扭曲強度」：0 是普通雲，越大越捲曲。
2. 把太陽材質三個顏色的 Intensity 都調到 0（亮度 ≤ 1），表面的溢光就消失（日冕仍是 HDR，所以外圈還會發光）——驗證 HDR 和 Bloom 的關係。
3. 選 **Sun > Corona**，改「光芒數量」和「光芒銳利度」。
4. 選 `Sun Bloom Volume`，把 Bloom 的 Threshold 調低到 0.5，連行星也開始溢光。
5. 在 `SunCorona.shader` 的 `frag` 最後改成 `return float4(filaments, filaments, filaments, 1);`，只看光芒那一層長什麼樣子。
   用這種方式單獨輸出某個中間值，是 Shader 除錯最常用的方法。

> 注意：執行中在 Inspector 改的材質數值，停止 Play 後會還原（材質是執行時用 `new Material` 建立的）。
> 調到滿意的數值後，要記下來改到 Shader 的 Properties 預設值裡。

---

## 8. 延伸閱讀關鍵字

- Unity 手冊：*ShaderLab*、*Writing vertex and fragment shaders*
- URP 手冊：*Volumes*、*Bloom*、*Writing custom shaders in URP*
- 噪聲：*The Book of Shaders*（noise、fBm 章節）、Inigo Quilez 的 *domain warping* 文章
- 進階：*Shader Graph*（URP 的視覺化 Shader 編輯器，不用寫程式）
