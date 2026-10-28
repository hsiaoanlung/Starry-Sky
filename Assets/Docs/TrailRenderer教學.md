# TrailRenderer 教學

TrailRenderer 是 Unity 內建的元件，會在物體**移動過的路徑**後面留下一條漸漸消失的軌跡，常用在子彈、劍光、賽車尾燈，以及本專案的行星軌道。

本文先介紹 TrailRenderer 的原理與常用屬性，再說明本專案（`SolarSystemBuilder.cs`）怎麼用它。

---

## 1. 運作原理

TrailRenderer 每一幀做三件事：

1. **記錄位置**：物體移動超過 `minVertexDistance` 時，把目前的世界座標加入一個點列表，並記下時間戳記。
2. **刪除過期的點**：存在超過 `time` 秒的點會被移除，所以軌跡長度是「最近幾秒走過的路」。
3. **產生網格**：把這串點連成一條帶狀網格（每個點往左右展開 `width` 寬），預設會轉向面對相機，所以從任何角度看都是一條有寬度的線。

```
  舊 ←─────────────── 新
  ·   ·   ·   ·   ·   ● ← 物體目前位置
  消失       time 秒內的點
```

重點：

- 點記錄的是**世界座標**，物體移走後，軌跡會留在原地，不會跟著物體平移。這正是「畫出走過的路徑」需要的效果。
- 軌跡長度由**時間**決定，不是距離。物體跑得越快，同樣的 `time` 拉出的線越長。

---

## 2. 加入 TrailRenderer

### 2.1 用 Inspector

1. 選取物體 → **Add Component** → 搜尋 **Trail Renderer**。
2. 指定 **Materials** → Element 0，否則會顯示成粉紅色（缺少材質）。
3. 調整 **Time**、**Width**、**Color**。
4. 按 Play，移動物體就會看到軌跡。

### 2.2 用程式碼

```csharp
var trail = gameObject.AddComponent<TrailRenderer>();
trail.time = 2f;                        // 軌跡保留 2 秒
trail.startWidth = 0.2f;                // 靠近物體那端的寬度
trail.endWidth = 0f;                    // 尾端收成一個尖點
trail.material = new Material(Shader.Find("Sprites/Default"));
trail.startColor = Color.white;
trail.endColor = new Color(1, 1, 1, 0); // 尾端透明，看起來像淡出
```

---

## 3. 常用屬性

| 屬性 | 說明 |
| --- | --- |
| `time` | 每個點存活幾秒，決定軌跡長度。 |
| `minVertexDistance` | 物體移動多遠才記一個新點（世界單位）。越小越平滑，但頂點越多。 |
| `startWidth` / `endWidth` | 頭、尾寬度，中間線性內插。 |
| `widthCurve` | 用 AnimationCurve 自訂沿線的寬度變化，會取代上面兩個。 |
| `widthMultiplier` | 寬度整體倍數，方便一次放大縮小。 |
| `startColor` / `endColor` | 頭、尾顏色（含透明度）。 |
| `colorGradient` | 用 Gradient 自訂沿線的顏色，可以有多個色階。 |
| `material` | 軌跡使用的材質。必須設定，否則是粉紅色。 |
| `alignment` | `View`（預設，面向相機）或 `TransformZ`（面向物體的 Z 軸）。 |
| `numCornerVertices` | 轉角處的額外頂點數，讓急轉彎不會出現鋸齒。 |
| `numCapVertices` | 兩端圓角的頂點數。 |
| `textureMode` | 貼圖沿軌跡怎麼展開：`Stretch`、`Tile` 等。純色軌跡不用管。 |
| `emitting` | 設為 `false` 時暫停記錄新點，舊的點照常消失。 |
| `autodestruct` | 物體停止移動且軌跡消失後，自動刪除 GameObject。 |
| `shadowCastingMode` | 是否投射陰影，軌跡通常關掉。 |

### 3.1 漸層顏色範例

```csharp
var g = new Gradient();
g.SetKeys(
    new[] { new GradientColorKey(Color.yellow, 0f), new GradientColorKey(Color.red, 1f) },
    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
trail.colorGradient = g;   // 頭黃尾紅，同時淡出
```

### 3.2 寬度曲線範例

```csharp
trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);   // 頭粗尾細，平滑過渡
trail.widthMultiplier = 0.3f;
```

---

## 4. 常用方法

| 方法 | 用途 |
| --- | --- |
| `Clear()` | 清掉所有點。物體被瞬間移動（傳送、重生）後呼叫，避免拉出一條長直線。 |
| `AddPosition(Vector3)` | 手動加入一個點。 |
| `GetPositions(Vector3[])` | 讀出目前所有點。 |

### 瞬間移動會拉出直線

TrailRenderer 只知道「上一個點」和「目前位置」，中間會直接連線。如果物體從 A 被傳送到 B，就會出現一條 A→B 的直線。解法：

```csharp
trail.emitting = false;
transform.position = newPosition;
trail.Clear();
trail.emitting = true;
```

---

## 5. 本專案的用法

`SolarSystemBuilder.Create()` 在建立每顆天體時加上 TrailRenderer。

> 2026-10-28 起，預設由 `TrailVisual` 接手外觀：固定像素寬度、加法混合的發光細線、`T` 鍵切換顯示模式，見第 8 節與 [軌跡外觀說明.md](軌跡外觀說明.md)。
> 下面是**原本的實心線**寫法，把 Solar System Builder 的 **Trail Visual** 取消勾選時仍會使用。

```csharp
var trail = go.AddComponent<TrailRenderer>();
trail.minVertexDistance = radius * 0.5f;   // 小星（月球）軌跡更細緻，大星省頂點
trail.startWidth = radius * 0.5f;          // 寬度跟著星球大小
trail.endWidth = 0f;                       // 尾端收尖
trail.material = new Material(Shader.Find("Sprites/Default"));
trail.startColor = color;                  // 與星球同色
trail.endColor = new Color(color.r, color.g, color.b, 0f);   // 尾端淡出
```

太陽沒有 `parent`，不會加軌跡。

### 5.1 為什麼不用固定的 time

`time` 的單位是**真實秒數**，但各行星的公轉速度差很多。若所有行星都用同一個 `time`（例如 20 秒），在每秒 30 模擬天的播放速度下，代表軌跡保留 600 天：

| 天體 | 週期 | 20 秒 = 600 天 = 幾圈 |
| --- | --- | --- |
| 水星 | 88 天 | 6.8 圈 → 疊成實心圓 |
| 地球 | 365 天 | 1.6 圈 → 完整圓 |
| 木星 | 4333 天 | 0.14 圈 → 短尾巴 |

內行星的軌跡會疊成一個完整的圓圈，看不出「尾巴」的感覺。

### 5.2 改成按公轉週期的比例

讓每顆星都顯示**同樣比例的一圈**，例如 0.3 圈。先用克卜勒第三定律算週期：

$$T = 2\pi\sqrt{\frac{a^3}{\mu}}, \qquad \mu = G(M_{parent} + m)$$

再換算成真實秒數：

$$\text{trail.time} = \frac{T \times \text{trailOrbitFraction}}{\text{daysPerSecond}}$$

程式碼：

```csharp
double period = 2.0 * System.Math.PI * System.Math.Sqrt(p.a * p.a * p.a / mu);

void UpdateTrailTimes()
{
    double daysPerSecond = System.Math.Max(simulator.daysPerSecond, 1e-6);
    foreach (var t in trails)
        t.trail.time = (float)(t.periodDays * trailOrbitFraction / daysPerSecond);
}
```

`UpdateTrailTimes()` 每幀呼叫一次，所以在執行中調整 Simulator 的 **Days Per Second** 或 Builder 的 **Trail Orbit Fraction**，軌跡長度會立刻跟著變。

預設 `trailOrbitFraction = 0.3`、`daysPerSecond = 30` 時：

| 天體 | 週期（天） | trail.time（秒） |
| --- | --- | --- |
| 月球 | 27.3（繞地球） | 0.27 |
| 水星 | 88 | 0.88 |
| 金星 | 225 | 2.2 |
| 地球 | 365 | 3.7 |
| 火星 | 687 | 6.9 |
| 木星 | 4333 | 43 |
| 土星 | 10759 | 108 |
| 天王星 | 30687 | 307 |
| 海王星 | 60190 | 602 |

外行星的軌跡要跑比較久才會長滿，這是正常的。

### 5.3 調整方式

在 Hierarchy 選 **Simulator** → Inspector 的 **Solar System Builder**：

- **Add Trails**：開關所有軌跡（Play 前設定）。
- **Trail Orbit Fraction**：0.05 ~ 1。越小尾巴越短；1 = 剛好畫滿一圈。

### 5.4 月球的軌跡

月球的軌跡記錄的是它在畫面上的位置（已經被 `orbitScale` 放大過），而且是世界座標，所以會畫出「跟著地球前進、同時繞圈」的波浪線，而不是一個小圓圈。這就是月球相對太陽的真實運動形狀（只是繞地的幅度被放大了）。

---

## 6. 常見問題

### 軌跡是粉紅色

材質沒設定，或 Shader 找不到。本專案用 URP，`Sprites/Default` 在 URP 下仍可運作；也可以改用 URP 專用的：

```csharp
trail.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
```

### 編輯器正常，打包後軌跡消失或變粉紅

`Shader.Find()` 只找得到**有被打包進遊戲**的 Shader。若某個 Shader 沒被任何材質引用，打包時可能被剔除。解法擇一：

- 在 Project 視窗建立一個 Material 資產，指定給 Builder 的欄位，而不是用 `Shader.Find()`。本專案的發光線材質放在 `Resources/Materials/Trail.mat`，用 `Resources.Load` 讀取，就是這個做法。
- 到 **Project Settings → Graphics → Always Included Shaders** 加入該 Shader。

### 遠看時軌跡太細、近看時太粗

`width` 是**世界單位**，相機拉遠後線會變細、拉近會變粗。可以依相機距離動態調整 `widthMultiplier`，本專案的 `TrailVisual` 就是這樣做，見第 8.1 節。

### 軌跡頂點太多、變慢

頂點數 ≈ 軌跡長度 ÷ `minVertexDistance`。軌跡很長（`time` 很大、移動很快）時，把 `minVertexDistance` 調大。

### 想畫完整的軌道線，而不是尾巴

TrailRenderer 只能畫「走過的路」。若要一開始就顯示整圈橢圓，改用 **LineRenderer**：先用 $a$、$e$ 算出橢圓上的點，再一次設定給 `LineRenderer.SetPositions()`。兩者可以同時使用：LineRenderer 畫淡淡的軌道，TrailRenderer 畫亮的尾巴。

---

## 7. TrailRenderer 與 LineRenderer 比較

| | TrailRenderer | LineRenderer |
| --- | --- | --- |
| 點從哪來 | 自動記錄物體走過的位置 | 程式手動指定 |
| 會隨時間消失 | 會 | 不會 |
| 適合 | 移動尾跡、動態路徑 | 固定軌道、雷射、連線 |
| 物體瞬移 | 會拉直線，要 `Clear()` | 不受影響 |

---

## 8. 進階：固定像素寬度的發光線（本專案的 TrailVisual）

### 8.1 讓線在螢幕上永遠一樣粗

透視相機在距離 $d$ 的地方，畫面高度是 $2d	an(	ext{FOV}/2)$ 個世界單位，被分成 `Screen.height` 個像素。所以要 $p$ 像素寬：

```csharp
float pixelAtUnitDistance = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
float depth = Vector3.Dot(transform.position - cam.transform.position, cam.transform.forward);   // 沿視線的深度
trail.startWidth = 1f;                                       // 寬度曲線維持「1 → 尾端比例」
trail.widthMultiplier = p * pixelAtUnitDistance * depth;     // 每幀更新
```

要在相機移動**之後**才算，否則會慢一幀：放在 `LateUpdate`，並用 `[DefaultExecutionOrder(100)]` 讓它比移動相機的腳本晚執行。

### 8.2 用頂點顏色傳資料給 Shader

TrailRenderer 會把 `colorGradient` 內插到每個頂點，Shader 用 `COLOR` 語意讀到。顏色不一定只能是「顏色」：
加法混合用不到 alpha，所以把 alpha 設成「頭 1 → 尾 0」，Shader 就知道每個像素在軌跡上的位置，可以自己決定淡入淡出的曲線。

```csharp
var g = new Gradient();
g.SetKeys(
    new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
trail.colorGradient = g;   // Gradient 每次 new 都會配置記憶體，只在需要時才重設
```

### 8.3 隱藏但不中斷

| 做法 | 還會記錄位置嗎 | 重新顯示時 |
| --- | --- | --- |
| `trail.enabled = false` | 不會 | 從舊位置拉一條直線到現在的位置 |
| `trail.emitting = false` | 不會記新點，舊的照常消失 | 中間有一段空白 |
| `trail.forceRenderingOff = true` | **會** | 軌跡完整，好像從來沒隱藏過 |

`forceRenderingOff` 是所有 Renderer 都有的屬性：元件照常運作，只是這一幀不畫。

### 8.4 自己動手試試

1. Play 中選 Simulator，把 **Trail Visual > Width Pixels** 從 1.5 拉到 6，再拉近、拉遠，觀察線在螢幕上的粗細有沒有變。
2. 把 **Intensity** 拉到 2，切到「全部」模式，看衛星的線疊在一起的地方是不是開始發光（Bloom）。這就是加法混合「越疊越亮」的特性。
3. 把 **Head Fade** 拉到 0.3、**Tail Power** 拉到 4，軌跡會變成只剩中間一小段亮，像流星。
4. 停止 Play，把 Solar System Builder 的 **Trail Visual** 取消勾選後再 Play，跟隨地球，比較原本的實心帶子。
5. 在 `TrailVisual.ApplyColor()` 把 `GradientAlphaKey(0f, 1f)` 改成 `GradientAlphaKey(0.5f, 1f)`，猜猜看尾巴會變怎樣？（提示：Shader 算出的 t 最大只到 0.5）
