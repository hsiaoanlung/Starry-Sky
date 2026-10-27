# Prefab 載入說明（CSV 的 prefab 欄）

最後更新：2026-10-27

`Planets.csv` 新增 `prefab` 欄，填 Prefab 的路徑，按下 Play 時 `SolarSystemBuilder` 就會用那個 Prefab 當天體的外觀，放到 N-body 算出的位置上。
太陽和八大行星現在都會自動換成做好的 Prefab；衛星的 `prefab` 欄空白，仍是純色球。

| 項目 | 原本 | 現在 |
| --- | --- | --- |
| Prefab 放哪 | `Assets/Prefabs/` | `Assets/Resources/Prefabs/`（太陽），`Assets/Resources/Prefabs/Planets/`（八大行星） |
| 行星外觀 | 純色球（Prefab 只能拖進場景試看） | 自動載入 Prefab：貼圖、雲、土星環、自轉軸傾角都有 |
| 太陽 | 要把 Sun.prefab 手動拖到 `SolarSystemBuilder` 的 Sun Prefab 欄位 | CSV 填 `Prefabs/Sun`，不用拖。**Sun Prefab 欄位已移除** |
| 要換外觀時 | 改程式或 Inspector | 改 CSV 一格 |

---

## 1. 檔案

| 檔案 | 改了什麼 |
| --- | --- |
| `Assets/Resources/Prefabs/Sun.prefab` | 從 `Assets/Prefabs/` 搬過來 |
| `Assets/Resources/Prefabs/Planets/*.prefab` | 水星～海王星 8 個，從 `Assets/Prefabs/` 搬過來 |
| `Assets/Resources/Planets.csv` | 新增第 10 欄 `prefab` |
| `PlanetSpec.cs` | 讀第 10 欄到 `prefab`；沒有這一欄也能讀（舊的 CSV 照樣能用） |
| `SolarSystemBuilder.cs` | 新增 `LoadPrefab()`、`usePrefabs` 開關；移除 `sunPrefab` 欄位 |

搬檔時連 `.meta` 一起搬，GUID 沒變，所以 Prefab 對材質、材質對貼圖的引用都不會斷。

---

## 2. CSV 怎麼填

```
name,parent,mass,a,e,displayRadius,color,orbitScale,retrograde,prefab
Sun,,1,0,0,0.6,#FFD933,1,0,Prefabs/Sun
Earth,Sun,3.00E-06,1,0.0167,0.15,#4D80FF,1,0,Prefabs/Planets/Earth
Moon,Earth,3.69E-08,0.00257,0.0549,0.05,#CCCCCC,25,0,
```

路徑規則：

- 從 **Resources 資料夾**開始算，不寫 `Assets/Resources/`。
- **不寫副檔名** `.prefab`。
- 斜線用 `/`。大小寫要和檔名一致（Windows 編輯器裡不分大小寫，但打包到其他平台可能會分）。
- 空白 = 純色球（用 `color` 欄的顏色）。

---

## 3. 原理：為什麼一定要放在 Resources

Unity 打包時**只會放進有被引用的資產**。場景裡用到的、被其他資產引用的才會被打包，其他的會被丟掉。
CSV 裡的路徑只是一段文字，Unity 不知道它指向哪個 Prefab，所以沒辦法自動打包。

`Resources` 是特殊資料夾名稱（任何位置、任何層都可以叫 Resources），規則是：

| | 一般資料夾 | Resources 資料夾 |
| --- | --- | --- |
| 打包 | 有被引用才打包 | **全部**打包 |
| 執行時用路徑讀 | 不行 | `Resources.Load<T>("路徑")` |
| 編輯器裡用路徑讀 | `AssetDatabase.LoadAssetAtPath`（只有編輯器有） | 同左 |

`Planets.csv` 本來就是用同樣的方法讀的：`Resources.Load<TextAsset>("Planets")`。

```csharp
GameObject LoadPrefab(PlanetSpec spec)
{
    if (!usePrefabs || string.IsNullOrEmpty(spec.prefab)) return null;
    var prefab = Resources.Load<GameObject>(spec.prefab);   // 讀不到會回傳 null，不會出錯
    if (prefab == null) Debug.LogWarning(...);              // 提醒路徑打錯，改用純色球
    return prefab;
}
```

`Resources.Load` 讀到的是 **Prefab 資產本身**，不在場景裡。要再 `Instantiate(prefab)` 複製一份，才會出現在場景中。

**代價**：Resources 裡的東西不管有沒有用到都會打包。這個專案的東西不多，沒有問題；
大型專案通常改用 *Addressables* 套件（也是用字串載入，但可以分批下載、管理記憶體）。

---

## 4. 建立天體的流程

```
SolarSystemBuilder.Create(spec, ...)
├─ LoadPrefab(spec)
│    有 → Instantiate(prefab)          外觀、材質、子物件（日冕、土星環）、自轉軸傾角都來自 Prefab
│    沒有 → CreatePrimitive(Sphere)    刪掉 Collider，顏色 = CSV color
├─ localScale = displayRadius × 2      Prefab 根物件的大小一律由 CSV 決定，子物件等比例跟著放大
├─ TrailRenderer（有公轉週期才加）
└─ CelestialBody（質量、位置、速度、displayColor …）
```

- **位置**由 `NBodySimulator` 每幀設定；**旋轉**不會被動到，所以 Prefab 上設好的傾角（土星 26.7° 等）會保留。
- 太陽：Sun.prefab 已經有 `SunVisual`。沒有 Prefab 時，若 `Sun Visual` 勾選，程式會補一個。
- 左側列表的文字顏色用 CSV 的 `color`，跟外觀無關。

---

## 5. 可調參數（Simulator > SolarSystemBuilder > 外觀）

| 參數 | 預設 | 說明 |
| --- | --- | --- |
| Use Prefabs | 開 | 關掉 = 全部用純色球，方便比較或排查問題（在按 Play 前設定） |
| Sun Visual | 開 | 太陽沒有 Prefab 時，程式產生的球是否加上電漿表面與日冕 |

---

## 6. 新增一個 Prefab 的步驟（例如給月球）

1. 做好 Prefab，根物件**直徑 1**（Unity 內建球的大小）、不要 Collider，放到 `Assets/Resources/Prefabs/` 底下，例如 `Prefabs/Moons/Moon.prefab`。
2. Planets.csv 的 Moon 那一列最後填 `Prefabs/Moons/Moon`。
3. 按 Play。Console 出現「Prefab 不存在」的警告就是路徑打錯了。

---

## 7. 已知限制

- **Shader 尚未在 Unity 中確認**：行星 Prefab 之前只在 Prefab 模式試看過，這是第一次在模擬裡用。
  行星的光從太陽來（`SunVisual` 每幀設定 `_StarrySkySunPos`），場景裡的 Directional Light 不影響它們。
- 行星 Prefab 的大小完全由 `displayRadius` 決定；土星環是子物件，所以環也跟著土星一起縮放。
- 執行時才產生的物件，停止 Play 後就消失。要在 Inspector 調材質，請直接改 `Assets/Materials/` 裡的材質檔。
