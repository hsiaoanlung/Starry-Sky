# N-body 太陽系模擬教學

目標：理解 N-body 模擬所需的全部數學，並能在 Unity 用 C# 實作一個穩定的太陽系。
公式以 LaTeX 寫在 `$...$` 裡，VS Code（Markdown Preview Enhanced）、Obsidian、GitHub 都能直接渲染。

搭配檔案：

| 檔案 | 用途 |
| --- | --- |
| `two_body_check.py` | 30 行 Python，先確認積分器寫對、能畫出一圈橢圓 |
| `Unity/Vec3d.cs` | double 精度三維向量（Unity 的 Vector3 是 float，不夠用） |
| `Unity/CelestialBody.cs` | 每顆天體的狀態：質量、位置、速度 |
| `Unity/NBodySimulator.cs` | 核心：重力計算 + Velocity Verlet 積分 + 能量檢查 |
| `Unity/SolarSystemBuilder.cs` | 用行星資料表自動產生太陽與八大行星 |

---

## 1. 概觀：N-body 模擬是什麼

N-body 模擬就是「每一顆天體都受到其他所有天體的重力，把這些力加總，再用數值方法一步一步推進時間」。
太陽系的 N 很小（太陽加八大行星是 9 個），所以最直接的 O(N²) 暴力法完全夠用，重點是把數學與積分法弄對。

整個模擬只需要三件事：

1. **力的公式**：牛頓萬有引力，算出每顆天體的加速度（第 2 節）。
2. **積分器**：用加速度更新速度與位置，每幀推進一個時間步 dt（第 3、4 節）。
3. **正確的初始條件與單位**：位置、速度、質量用一致的單位，否則軌道會立刻飛掉（第 5、6 節）。

建議順序：先跑 `two_body_check.py` 看到一圈橢圓；再加入其他行星；最後搬到 Unity。
第 7 節的能量守恆檢查是判斷「程式有沒有寫對」的唯一客觀標準，務必實作。

---

## 2. 核心物理：牛頓萬有引力

兩顆質量 $m_i$、$m_j$ 的天體相距 $r$ 時，引力大小：

$$F = G \frac{m_i m_j}{r^2}$$

$G$ 是萬有引力常數，SI 單位下 $G = 6.674 \times 10^{-11}\ \mathrm{m^3\,kg^{-1}\,s^{-2}}$。

模擬裡需要的是**向量**，因為力有方向。設天體 $i$ 的位置向量為 $\vec r_i$，天體 $j$ 為 $\vec r_j$，從 $i$ 指向 $j$ 的向量是 $\vec r_{ij} = \vec r_j - \vec r_i$。$j$ 對 $i$ 的引力指向 $j$：

$$\vec F_{ij} = G \frac{m_i m_j}{|\vec r_{ij}|^2} \cdot \frac{\vec r_{ij}}{|\vec r_{ij}|} = G \frac{m_i m_j}{|\vec r_{ij}|^3}\, \vec r_{ij}$$

最右邊的形式（分母是距離的三次方）是程式裡最常用的寫法：把「平方反比」和「單位向量」合併成一次除法。

天體 $i$ 受的總力是其他所有天體引力的向量和。再用牛頓第二定律 $\vec F = m \vec a$ 除掉 $m_i$，得到加速度：

$$\boxed{\ \vec a_i = \sum_{j \neq i} G \frac{m_j}{|\vec r_j - \vec r_i|^3} (\vec r_j - \vec r_i)\ }$$

這條就是整個 N-body 模擬的心臟。注意兩件事：

- 加速度裡**沒有 $m_i$**，只有「對方」的質量 $m_j$。程式可以直接累加加速度，不必先算力再除回去。
- 求和時 **$j \neq i$**，否則距離為 0 會除以零。

兩體之間的力大小相等、方向相反（牛頓第三定律），所以可以只算 $i < j$ 的配對，一次更新兩顆，計算量減半。

對應的程式碼（虛擬碼）：

```
for i in bodies: a[i] = 0
for i < j:
    d     = r[j] - r[i]
    r2    = |d|²
    invR3 = 1 / (r2 * sqrt(r2))
    a[i] += G * m[j] * invR3 * d
    a[j] -= G * m[i] * invR3 * d
```

---

## 3. 運動方程式：從加速度到位置

加速度是位置對時間的二階導數，所以第 2 節的公式其實是一組二階常微分方程式（ODE）：

$$\frac{d^2 \vec r_i}{dt^2} = \vec a_i(\vec r_1, \ldots, \vec r_N)$$

電腦不直接解二階方程式，所以引入速度 $\vec v_i$，拆成兩條一階方程式：

$$\frac{d\vec r_i}{dt} = \vec v_i, \qquad \frac{d\vec v_i}{dt} = \vec a_i(\vec r_1, \ldots, \vec r_N)$$

讀法：位置的變化率是速度，速度的變化率是加速度，而加速度**只取決於所有天體當下的位置**（與速度無關）。
這個性質很重要，第 4 節的 Verlet 積分器就是靠它才能又簡單又穩定。

每顆天體的狀態 = 位置向量 + 速度向量 + 質量。每一幀做的事：

1. 用所有天體的位置算出每顆的加速度 $\vec a_i$。
2. 用 $\vec a_i$ 更新 $\vec v_i$。
3. 用 $\vec v_i$ 更新 $\vec r_i$。
4. 時間 $t \leftarrow t + dt$，回到步驟 1。

「用什麼順序、用哪個時刻的 $a$ 與 $v$ 來更新」就是不同積分器的差別，也決定了軌道會不會慢慢漂移。

---

## 4. 數值積分法

以下 $h$ 代表時間步長 $dt$，上標 $n$ 代表第 $n$ 步。

### 4.1 顯式 Euler（不要用）

$$\vec r^{\,n+1} = \vec r^{\,n} + h\,\vec v^{\,n}, \qquad \vec v^{\,n+1} = \vec v^{\,n} + h\,\vec a(\vec r^{\,n})$$

最直覺，但能量會系統性地增加，行星會螺旋向外飛走。只適合用來「看壞的例子長什麼樣」。

### 4.2 半隱式 Euler（Symplectic Euler）

$$\vec v^{\,n+1} = \vec v^{\,n} + h\,\vec a(\vec r^{\,n}), \qquad \vec r^{\,n+1} = \vec r^{\,n} + h\,\vec v^{\,n+1}$$

只是把順序對調：**先更新速度，再用新速度更新位置**。這一個小改動讓它變成 symplectic（辛）積分器，能量不再單向漂移，而是在真值附近震盪。一階精度，但很多遊戲就是用這個。

### 4.3 Velocity Verlet / Leapfrog（推薦）

$$\begin{aligned}
\vec v^{\,n+1/2} &= \vec v^{\,n} + \tfrac{h}{2}\,\vec a(\vec r^{\,n}) \\
\vec r^{\,n+1} &= \vec r^{\,n} + h\,\vec v^{\,n+1/2} \\
\vec v^{\,n+1} &= \vec v^{\,n+1/2} + \tfrac{h}{2}\,\vec a(\vec r^{\,n+1})
\end{aligned}$$

二階精度、symplectic、時間可逆，而且每步只需要算一次加速度（$\vec a(\vec r^{\,n+1})$ 可以留到下一步重用）。
這是天體力學模擬的標準選擇，本教學的 Unity 程式碼用的就是它。

程式碼：

```
for each body: v += 0.5 * h * a
for each body: r += h * v
ComputeAccelerations()        // 用新位置算 a
for each body: v += 0.5 * h * a
```

### 4.4 RK4（四階 Runge-Kutta）

把狀態 $\vec y = (\vec r, \vec v)$ 當成一個整體，$\vec f(\vec y) = (\vec v, \vec a(\vec r))$：

$$\begin{aligned}
\vec k_1 &= \vec f(\vec y^{\,n}) \\
\vec k_2 &= \vec f(\vec y^{\,n} + \tfrac{h}{2}\vec k_1) \\
\vec k_3 &= \vec f(\vec y^{\,n} + \tfrac{h}{2}\vec k_2) \\
\vec k_4 &= \vec f(\vec y^{\,n} + h\,\vec k_3) \\
\vec y^{\,n+1} &= \vec y^{\,n} + \tfrac{h}{6}(\vec k_1 + 2\vec k_2 + 2\vec k_3 + \vec k_4)
\end{aligned}$$

四階精度，短時間內誤差最小，但**不是 symplectic**：跑幾千個軌道週期後能量會慢慢流失。每步要算 4 次加速度。適合短時間高精度，不適合「讓太陽系跑一萬年」。

### 4.5 比較

| 積分器 | 精度階數 | 每步算 a 次數 | Symplectic | 長時間能量行為 |
| --- | --- | --- | --- | --- |
| 顯式 Euler | 1 | 1 | 否 | 單向增加，軌道外飄 |
| 半隱式 Euler | 1 | 1 | 是 | 有界震盪 |
| Velocity Verlet | 2 | 1 | 是 | 有界震盪，誤差 ∝ h² |
| RK4 | 4 | 4 | 否 | 緩慢漂移 |

結論：太陽系長時間模擬用 **Velocity Verlet**。要更準就把 $h$ 縮小，不要換 RK4。

---

## 5. 單位與尺度

### 5.1 不要用 SI 單位

地球到太陽 $1.5 \times 10^{11}$ m，太陽質量 $2 \times 10^{30}$ kg，$G = 6.67 \times 10^{-11}$。
這些數字乘在一起會讓 float 精度爆掉，Unity 的場景座標也不可能用公尺。
解法是改用**天文單位制**：

| 量 | 單位 | 說明 |
| --- | --- | --- |
| 長度 | AU（天文單位） | 1 AU = 1.495978707 × 10¹¹ m，地球軌道半長軸 |
| 質量 | $M_\odot$（太陽質量） | 太陽 = 1，地球 ≈ 3.0 × 10⁻⁶ |
| 時間 | 天（day） | 86400 秒 |

在這組單位下：

$$G = 2.959122 \times 10^{-4}\ \mathrm{AU^3\, M_\odot^{-1}\, day^{-2}}$$

這個數值就是高斯引力常數的平方 $k^2$（$k = 0.01720209895$）。若時間改用「年」，則 $G = 4\pi^2 \approx 39.478$，這是克卜勒第三定律的直接結果。

驗算：地球圓軌道速度 $v = \sqrt{G M_\odot / 1\,\mathrm{AU}} = \sqrt{2.959 \times 10^{-4}} = 0.01720$ AU/day。
換算成 km/s：$0.01720 \times 1.496 \times 10^8 / 86400 \approx 29.8$ km/s，與實際相符。

### 5.2 double 計算、float 顯示

- 物理狀態（位置、速度、質量）全部用 `double`。海王星 30 AU 與水星 0.39 AU 相差近 100 倍，float 只有 7 位有效數字，累積幾千步後誤差會明顯。
- 只在把位置丟給 `transform.position` 時才轉成 float，並乘上顯示比例（例如 1 AU = 10 Unity 單位）。
- 行星大小不要按真實比例畫，否則一顆都看不見（太陽直徑只有 0.0093 AU）。

### 5.3 時間比例

模擬時間與真實時間分開控制：

- `dt`：每一步積分推進多少「模擬天」，由精度決定（第 8 節）。
- `daysPerSecond`：每一秒真實時間要推進多少模擬天，由觀賞速度決定。
- 每幀跑 `Time.deltaTime × daysPerSecond / dt` 步。

兩者分開後，拖慢或加速播放不會影響物理精度。

---

## 6. 初始條件

### 6.1 圓軌道速度

質量 $M$ 的中心天體，半徑 $r$ 的圓軌道，向心力等於重力：

$$\frac{m v^2}{r} = \frac{G M m}{r^2} \quad\Rightarrow\quad v_{circ} = \sqrt{\frac{GM}{r}}$$

把行星放在 $(r, 0, 0)$，速度給 $(0, 0, v_{circ})$（垂直於位置向量），就會得到圓軌道。

### 6.2 橢圓軌道：活力公式（vis-viva）

半長軸 $a$ 的橢圓軌道上，距離中心 $r$ 時的速率：

$$v^2 = GM\left(\frac{2}{r} - \frac{1}{a}\right)$$

$r = a$ 時退化成圓軌道公式。要用真實的離心率 $e$ 啟動，最方便是從**近日點**開始：

$$r_p = a(1-e), \qquad v_p = \sqrt{\frac{GM(1+e)}{a(1-e)}}$$

把行星放在 $(r_p, 0, 0)$，速度 $(0, 0, v_p)$，它就會跑出半長軸 $a$、離心率 $e$ 的橢圓。
嚴格來說兩體問題的 $M$ 應該用 $M_\odot + m_{planet}$，對木星差 0.1%，程式裡直接用總和即可。

### 6.3 行星資料表

| 天體 | 質量（$M_\odot$） | 半長軸 a（AU） | 離心率 e | 公轉週期（天） |
| --- | --- | --- | --- | --- |
| 太陽 | 1 | — | — | — |
| 水星 | 1.66 × 10⁻⁷ | 0.3871 | 0.2056 | 88.0 |
| 金星 | 2.45 × 10⁻⁶ | 0.7233 | 0.0068 | 224.7 |
| 地球 | 3.00 × 10⁻⁶ | 1.0000 | 0.0167 | 365.25 |
| 火星 | 3.23 × 10⁻⁷ | 1.5237 | 0.0934 | 687.0 |
| 木星 | 9.55 × 10⁻⁴ | 5.2026 | 0.0489 | 4332.6 |
| 土星 | 2.86 × 10⁻⁴ | 9.5549 | 0.0565 | 10759 |
| 天王星 | 4.37 × 10⁻⁵ | 19.218 | 0.0457 | 30687 |
| 海王星 | 5.15 × 10⁻⁵ | 30.110 | 0.0113 | 60190 |

質量換算：地球 5.972 × 10²⁴ kg ÷ 太陽 1.989 × 10³⁰ kg = 3.00 × 10⁻⁶。

這張表讓所有行星都從近日點、同一方向出發，是「教學用」的排列，不是真實的某一天。
要重現真實日期的太陽系（例如 2026-10-14 的行星排列），到 NASA JPL Horizons（<https://ssd.jpl.nasa.gov/horizons/>）選「Vectors」輸出，取得每顆行星以太陽為中心、單位 AU 與 AU/day 的位置與速度向量，直接填進程式即可。

### 6.4 讓質心不動

每顆行星都有動量，太陽若從靜止出發，整個系統的質心會慢慢漂移。修正方法是讓總動量為零：

$$\vec v_\odot = -\frac{1}{M_\odot}\sum_{planets} m_i \vec v_i$$

或者更一般地，把所有天體的速度減掉質心速度 $\vec v_{cm} = \sum m_i \vec v_i / \sum m_i$。

---

## 7. 驗證：守恆量

沒有解析解可以對答案，但有兩個量在正確的模擬裡必須守恆。

### 7.1 總能量

$$E = \underbrace{\sum_i \tfrac{1}{2} m_i |\vec v_i|^2}_{\text{動能}} \;-\; \underbrace{\sum_{i<j} \frac{G m_i m_j}{|\vec r_i - \vec r_j|}}_{\text{位能}}$$

注意位能是**負的**，而且每對只算一次（$i<j$）。束縛系統的 $E < 0$。

每隔一段時間計算相對誤差：

$$\epsilon_E = \frac{|E(t) - E_0|}{|E_0|}$$

- Velocity Verlet：$\epsilon_E$ 會震盪但不會成長，量級約 $h^2$。
- 顯式 Euler：$\epsilon_E$ 線性成長。
- 如果 $\epsilon_E$ 一直往上跑，代表 dt 太大、或程式有 bug（最常見：位能少算一半、或加速度忘了乘 $m_j$）。

### 7.2 總角動量

$$\vec L = \sum_i m_i\, (\vec r_i \times \vec v_i)$$

所有行星在同一平面時 $\vec L$ 只有一個分量非零（Unity 用 XZ 平面當軌道面時是 y 分量）。Verlet 對角動量守恆得非常好，通常只剩浮點捨入誤差。

### 7.3 週期檢查

讓地球跑一圈，記錄它回到初始角度的時間，應該是 365.25 天左右。差超過 1% 代表 $G$ 或初速算錯。

---

## 8. 實務細節

### 8.1 時間步長怎麼選

經驗法則：$h$ 不超過**最短軌道週期**的 1/100，想要高精度用 1/1000。

- 只有八大行星：最短是水星 88 天，$h = 0.25 \sim 1$ 天。
- 加入月球（週期 27.3 天，軌道半徑 0.00257 AU）：$h$ 要降到 0.05 天以下，否則月球會飛走。
- 加入人造衛星：$h$ 要到分鐘等級。這時太陽系的其他部分被迫用一樣小的步長，是 N-body 模擬的經典難題，解法是第 10 節的可變步長或分層步長。

### 8.2 Softening（軟化）

兩顆天體靠太近時 $1/r^3$ 會爆炸，產生巨大的加速度把天體彈飛。加一個小常數 $\epsilon$：

$$\vec a_i = \sum_{j \neq i} \frac{G m_j\, (\vec r_j - \vec r_i)}{\left(|\vec r_j - \vec r_i|^2 + \epsilon^2\right)^{3/2}}$$

太陽系的行星彼此不會靠近，$\epsilon = 0$ 即可。要做「隨機撒幾百顆粒子」的演示時才需要，$\epsilon$ 取粒子間典型距離的幾分之一。

### 8.3 Unity 相關注意事項

- **不要用 Unity 內建 Rigidbody / PhysX**：它用 float、固定 50 Hz、沒有萬有引力。自己寫積分器反而簡單。
- **用 `Update` 配合累加器**而不是 `FixedUpdate`：模擬時間與真實時間比例自由，也能暫停或加速。
- **軌道面放在 XZ 平面**（y 朝上），符合 Unity 的習慣，攝影機從 +y 俯視。
- **軌跡**用 `TrailRenderer` 最簡單；要畫完整預測軌道可以複製一份狀態往前跑幾百步再畫 `LineRenderer`。
- 行星「顯示半徑」與「物理」完全無關，隨便調。

---

## 9. Unity 實作

四個檔案都在 `Unity/` 資料夾，直接丟進 `Assets/Scripts/` 即可。設定步驟：

1. 建一個空物件 `Simulator`，掛上 `NBodySimulator` 與 `SolarSystemBuilder`。
2. `SolarSystemBuilder` 的 `simulator` 欄位拖入同一物件上的 `NBodySimulator`。
3. 按 Play。Builder 會自動建立太陽與八顆行星（Sphere + TrailRenderer），Simulator 每幀推進。
4. 看 Console：每 100 天印一次相對能量誤差，應該停在 10⁻⁶ 到 10⁻⁴ 之間震盪。

### 9.1 架構

```
SolarSystemBuilder  (Awake)  ──建立──>  CelestialBody × 9  (GameObject + 物理狀態)
                                              │
NBodySimulator      (Start)  ──收集──>  bodies 清單
        │
        └─ Update: 累加 deltaTime × daysPerSecond
                   while 累加 >= dt: Step(dt)      ← Velocity Verlet
                   SyncTransforms()                ← double → float，乘顯示比例
```

### 9.2 核心程式碼（NBodySimulator.cs 節錄）

```csharp
public const double G = 2.959122082855911e-4;   // AU^3 / (M_sun * day^2)

void Step(double h)
{
    // Velocity Verlet：半步速度 → 整步位置 → 重算加速度 → 半步速度
    foreach (var b in bodies)
    {
        b.velocity += b.acceleration * (0.5 * h);
        b.position += b.velocity * h;
    }
    ComputeAccelerations();
    foreach (var b in bodies)
        b.velocity += b.acceleration * (0.5 * h);
}

void ComputeAccelerations()
{
    int n = bodies.Count;
    for (int i = 0; i < n; i++) bodies[i].acceleration = Vec3d.zero;

    double eps2 = softening * softening;
    for (int i = 0; i < n; i++)
    {
        for (int j = i + 1; j < n; j++)
        {
            Vec3d d = bodies[j].position - bodies[i].position;   // i -> j
            double r2 = d.SqrMagnitude + eps2;
            double invR3 = 1.0 / (r2 * System.Math.Sqrt(r2));
            bodies[i].acceleration += d * (G * bodies[j].mass * invR3);
            bodies[j].acceleration -= d * (G * bodies[i].mass * invR3);
        }
    }
}

public double TotalEnergy()
{
    double kinetic = 0, potential = 0;
    int n = bodies.Count;
    for (int i = 0; i < n; i++)
    {
        kinetic += 0.5 * bodies[i].mass * bodies[i].velocity.SqrMagnitude;
        for (int j = i + 1; j < n; j++)
        {
            double r = (bodies[j].position - bodies[i].position).Magnitude;
            potential -= G * bodies[i].mass * bodies[j].mass / r;
        }
    }
    return kinetic + potential;
}
```

### 9.3 初始條件（SolarSystemBuilder.cs 節錄）

```csharp
// 近日點啟動：位置在 +x 軸，速度沿 +z（軌道面 = XZ 平面）
double mu = NBodySimulator.G * (1.0 + p.mass);          // G (M_sun + m_planet)
double rp = p.a * (1.0 - p.e);
double vp = System.Math.Sqrt(mu * (1.0 + p.e) / rp);
Create(p.name, p.mass, new Vec3d(rp, 0, 0), new Vec3d(0, 0, vp), p.displayRadius, p.color);
```

建立完所有天體後呼叫 `simulator.RemoveCenterOfMassDrift()`，讓總動量歸零（第 6.4 節）。

---

## 10. 進階方向

做完上面的基本版之後，可以往這幾個方向延伸：

- **可變／分層時間步長**：讓月球用小步、外行星用大步。關鍵字：block time step、Hermite integrator（天體力學模擬標準做法）。
- **高階 symplectic 積分器**：Yoshida 4 階、Wisdom-Holman（專為行星系統設計，把克卜勒運動解析解出來只積分擾動）。
- **Barnes-Hut 八叉樹**：N 到幾千以上時把 O(N²) 降到 O(N log N)。太陽系用不到，做星系碰撞才需要。
- **GPU / Burst**：N 上萬時用 Compute Shader 或 Unity Jobs + Burst 平行計算加速度。
- **廣義相對論修正**：加一項後置牛頓修正可以重現水星近日點每世紀 43 角秒的進動。
- **真實曆元**：從 JPL Horizons 抓某一天的向量，模擬結果可與真實星曆比對。

參考資料：

- Piet Hut & Jun Makino, *Moving Stars Around*（免費線上書，從零手把手寫 N-body，含 Verlet 與 Hermite）：<https://www.ids.ias.edu/~piet/act/comp/algorithms/starter/>
- NASA JPL Horizons System（真實行星位置與速度）：<https://ssd.jpl.nasa.gov/horizons/>
- NASA Planetary Fact Sheet（質量、軌道參數）：<https://nssdc.gsfc.nasa.gov/planetary/factsheet/>
- Wikipedia: Leapfrog integration、Vis-viva equation、Symplectic integrator
