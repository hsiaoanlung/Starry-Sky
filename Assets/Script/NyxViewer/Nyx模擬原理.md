# Nyx 是怎麼做宇宙學模擬的

這份文件根據 Nyx 官方文件（[amrex-astro.github.io/Nyx/docs_html](https://amrex-astro.github.io/Nyx/docs_html/)，原始檔在 repo 的 `Docs/sphinx_documentation/source/`）和原始碼整理而成。每個主題都對照到：

- 程式碼的位置；
- 我們實際跑的 `LyA` 64³ 範例用了哪些設定。

---

## 目錄

1. [一句話總結](#1-一句話總結)
2. [要模擬的東西：膨脹宇宙中的暗物質和氣體](#2-要模擬的東西膨脹宇宙中的暗物質和氣體)
3. [共動座標與尺度因子 a(t)](#3-共動座標與尺度因子-at)
4. [暗物質：N-body 粒子與 Particle-Mesh 方法](#4-暗物質n-body-粒子與-particle-mesh-方法)
5. [重力：在網格上解 Poisson 方程](#5-重力在網格上解-poisson-方程)
6. [氣體：有限體積法流體力學](#6-氣體有限體積法流體力學)
7. [輻射加熱與冷卻](#7-輻射加熱與冷卻)
8. [把所有東西串起來：一個時間步做了什麼](#8-把所有東西串起來一個時間步做了什麼)
9. [時間步長怎麼決定](#9-時間步長怎麼決定)
10. [自適應網格加密（AMR）](#10-自適應網格加密amr)
11. [初始條件](#11-初始條件)
12. [輸出檔案格式](#12-輸出檔案格式)
13. [單位系統](#13-單位系統)
14. [對照我們這次的模擬結果](#14-對照我們這次的模擬結果)
15. [程式碼導覽](#15-程式碼導覽)

---

## 1. 一句話總結

> Nyx 在一個**跟著宇宙一起膨脹的週期性盒子**裡，同時模擬兩種物質：
> - **暗物質**：用大量粒子表示，用 N-body 方法移動；
> - **一般物質（重子氣體）**：放在 3D 網格上，用有限體積法解流體方程。
>
> 兩者透過**共同的重力場**互相吸引。氣體還會受到宇宙紫外線背景的**加熱和冷卻**。
> 從大爆炸後約 1 千萬年（z≈159）一路演化到今天或任何指定時間，就能看到宇宙網（cosmic web）逐漸形成。

---

## 2. 要模擬的東西：膨脹宇宙中的暗物質和氣體

| 成分 | 占物質的比例 | 在 Nyx 裡怎麼表示 | 受到的作用 |
|---|---|---|---|
| 暗物質 | 約 83%（Ω_m − Ω_b） | **粒子**（拉格朗日描述，粒子跟著物質移動） | 只受重力 |
| 重子氣體（氫、氦） | 約 17%（Ω_b） | **網格**（歐拉描述，網格固定、物質流過） | 重力、壓力、震波、加熱冷卻 |
| 暗能量（Λ） | — | 只出現在 a(t) 的演化裡 | 讓宇宙加速膨脹 |

LyA 範例用的宇宙學參數（`inputs`）：
```
nyx.comoving_OmM = 0.275   # 物質密度 Ω_m
nyx.comoving_OmB = 0.046   # 重子密度 Ω_b
nyx.comoving_h   = 0.702   # 哈伯常數 H0 = 70.2 km/s/Mpc
```

為什麼叫 **LyA**？這類模擬主要用來研究 **Lyman-α 森林**：遙遠類星體的光穿過星系之間的稀薄氫氣時，被吸收而在光譜上留下一排吸收線。要預測這些吸收線，必須精確算出星系際介質（IGM）的密度、溫度和速度，這正是 Nyx 擅長的。

---

## 3. 共動座標與尺度因子 a(t)

宇宙在膨脹，如果用固定的「物理座標」，所有東西都會一直彼此遠離。Nyx 改用**共動座標**（comoving coordinates），也就是讓座標網格**跟著宇宙一起膨脹**：

- **尺度因子** $a(t)$ 描述宇宙的大小，今天 $a=1$。它和紅移的關係是
  $$a = \frac{1}{1+z}$$
- 共動密度 $\rho_b = a^3 \rho_{\text{proper}}$，也就是去掉膨脹造成的稀釋效果。
- 速度 $\mathbf{U}$ 是**本動速度**（peculiar velocity），也就是扣掉哈伯膨脹之後，物質自己的運動速度。

$a(t)$ 由 Friedmann 方程決定，在 `Source/Driver/comoving.cpp` 裡數值積分：

$$\dot a = H_0 \sqrt{\Omega_m / a + \Omega_\Lambda a^2 \,(+\,\Omega_r/a^2)}$$

因為所有方程式都寫在共動座標下，方程式裡會多出跟 $a$ 和 $\dot a$ 有關的項，例如「宇宙膨脹會讓氣體絕熱冷卻」。

---

## 4. 暗物質：N-body 粒子與 Particle-Mesh 方法

> 原始碼：`Source/Particle/`，主要類別是 `DarkMatterParticleContainer`

每顆粒子代表一大團暗物質。這次的範例有 64³ = 262,144 顆粒子，每顆大約 10⁹ 到 10¹⁰ 個太陽質量。

### 運動方程（共動座標）
$$\frac{d\mathbf{x}_i}{dt} = \frac{1}{a}\mathbf{u}_i, \qquad \frac{d(a\,\mathbf{u}_i)}{dt} = \mathbf{g}(\mathbf{x}_i)$$

### 時間積分：Kick–Drift–Kick（蛙跳法）
每個時間步分成三段：
1. **Kick（半步）**：用舊的重力 $\mathbf{g}^n$ 更新速度半個時間步
   $\mathbf{u}^{n+1/2} = \frac{1}{a^{n+1/2}}\left(a^n\mathbf{u}^n + \frac{\Delta t}{2}\mathbf{g}^n\right)$
2. **Drift（整步）**：用半步速度移動位置
   $\mathbf{x}^{n+1} = \mathbf{x}^n + \frac{\Delta t}{a^{n+1/2}}\mathbf{u}^{n+1/2}$
3. 用新位置重新算重力 $\mathbf{g}^{n+1}$，再做 **Kick（半步）** 把速度補完

這個方法是二階精度、時間可逆，長時間積分也不會累積能量誤差。另外它每步只需要解一次重力。

### Particle-Mesh（PM）：粒子 → 網格 → 粒子
直接算 N 顆粒子兩兩之間的重力是 O(N²)，太慢了。PM 方法改成這樣：

```
 粒子質量 ──CIC 分配──▶ 網格密度 ρ_DM ──解 Poisson──▶ 網格上的 φ ──差分──▶ g ──插值──▶ 每顆粒子的加速度
```

- **CIC（Cloud-in-Cell）**：把每顆粒子想成一個邊長 Δx 的小立方體，依照它和周圍 8 個格子重疊的體積，把質量分配下去。
- 重力在網格上解完之後，再用線性插值算回每顆粒子位置上的值。

---

## 5. 重力：在網格上解 Poisson 方程

> 原始碼：`Source/Gravity/Gravity.cpp`，求解器用 AMReX 的 `MLMG`（多層網格）

暗物質和氣體的密度加總後，一起當作重力源：

$$\nabla^2\phi = \frac{4\pi G}{a}\left(\rho_{\text{gas}} + \rho_{\text{DM}} - \bar\rho\right), \qquad \mathbf{g} = -\nabla\phi$$

- 減掉平均密度 $\bar\rho$：在週期性的盒子裡，只有「比平均多或少」的部分會產生淨重力。
- 用 7 點的 Laplacian 離散，再用**多層網格法（multigrid）**解。多層網格的計算量和格點數成正比（O(N)），是解這類橢圓方程最快的方法之一。
- 每一步會拿上一步的 $\phi$ 當作初始猜測，可以更快收斂。

相關設定：`gravity.ml_tol = 1e-10`（收斂容許誤差）。

---

## 6. 氣體：有限體積法流體力學

> 原始碼：`Source/Hydro/`，包括 `Godunov.cpp`、`trace_ppm.cpp`、`Riemann.H`

### 方程式：共動座標下的 Euler 方程
狀態向量是 $\overline{\mathbf U} = (\rho,\ a\rho\mathbf{U},\ a^2\rho E,\ a^2\rho e)$，分別代表**密度、動量、總能量、內能**。

$$\frac{\partial\rho}{\partial t} = -\frac{1}{a}\nabla\cdot(\rho\mathbf U)$$

$$\frac{\partial(a\rho\mathbf U)}{\partial t} = -\nabla\cdot(\rho\mathbf U\mathbf U) - \nabla p + \rho\mathbf g$$

$$\frac{\partial(a^2\rho E)}{\partial t} = a\left[-\nabla\cdot(\rho\mathbf U E + p\mathbf U) + \rho\mathbf U\cdot\mathbf g + \rho\Lambda_{HC}\right]$$

$\Lambda_{HC}$ 是輻射加熱減掉冷卻（見第 7 節）。氣體視為 γ = 5/3 的理想氣體。

### 數值方法：非分裂 Godunov 方法
每個格子儲存的是**格子內的平均值**，每一步根據「流過格子邊界的通量」來更新：

1. **重建**：用 PPM（Piecewise Parabolic Method，分段拋物線法，`nyx.ppm_type = 1`）從格子平均值重建出格子內的拋物線分布，可以清楚捕捉震波和接觸面。
2. **特徵追蹤**：把邊界上的值預測到半個時間步，達到二階時間精度。
3. **角落耦合**（`nyx.corner_coupling = 1`）：三個方向同時更新（非分裂），而不是一個方向一個方向輪流做。這樣能減少網格方向造成的假象。
4. **Riemann 求解器**：在每個格子邊界上，把左右兩邊的狀態當作「兩團氣體突然接觸」來解，得到通過邊界的通量。
5. **保守更新**：$\mathbf U^{n+1} = \mathbf U^n - \Delta t\,\nabla\cdot\mathbf F$，質量、動量和能量都嚴格守恆。

### 雙能量方法
同時追蹤總能量 $E$ 和內能 $e$。在冷而快速流動的區域，$e = E - \frac12 U^2$ 是兩個很接近的大數相減，誤差會很大，這時改用獨立演化的 $e$。這對**星系際介質的溫度**非常重要。

---

## 7. 輻射加熱與冷卻

> 原始碼：`Source/HeatCool/`（ODE 積分）、`Source/EOS/eos_hc.H`（狀態方程）、`Source/EOS/atomic_rates*`（原子反應率）

### 物理
宇宙中的類星體和星系發出紫外線，形成均勻的**紫外線背景（UVB）**。UVB 對氫和氦的作用有兩種：

- **光致電離**：把中性的 H、He 游離，同時**加熱**氣體；
- **冷卻**：氣體透過復合、碰撞激發、軔致輻射、逆康普頓散射等過程，把能量輻射出去。

UVB 的強度隨紅移變化，資料來自 `TREECOOL_middle` 這個表（`nyx.uvb_rates_file`）。這個表裡，UVB 大約在 **z ≈ 10 到 8 之間打開**，這就是**再電離**。

### 數值方法
- 假設**光學薄**（光線不會被擋住）和**電離平衡**：給定密度和內能之後，用牛頓迭代（`iterate_ne`）解出各種離子的比例（H⁰、H⁺、He⁰、He⁺、He⁺⁺）、電子密度和溫度。
- 每個格子的內能變化是一條 ODE：$\frac{de}{dt} = \Lambda_{HC}(\rho, e, z)$。這條 ODE **很「硬」（stiff）**，冷卻的時間尺度可能比流體的時間步短好幾個數量級。
- 所以用 **SUNDIALS 的 CVODE**（隱式 BDF 方法，會自動調整步長）來解。`nyx.heat_cool_type = 11` 代表「向量化」版本：把一整塊格子的 ODE 合成一個大系統一起解，提升平行效率。
- `USE_CONST_SPECIES`：H 和 He 的總質量比例固定（`nyx.h_species = 0.76`、`nyx.he_species = 0.24`），只解它們之間的電離平衡。

> 💡 這就是為什麼我們的模擬在 **z < 10 之後每步變慢**（從約 1 秒變成約 2.6 秒）：UVB 打開後，加熱冷卻的 ODE 變得很硬，CVODE 需要更多子步驟。

---

## 8. 把所有東西串起來：一個時間步做了什麼

流體、重力、粒子、加熱冷卻的時間尺度都不一樣，Nyx 用**算子分裂**（operator splitting）把它們組合起來。有兩種方法可以選：

| 方法 | 設定 | 概念 |
|---|---|---|
| **Strang 分裂** | `nyx.strang_split = 1` | 加熱冷卻半步 → 流體整步 → 加熱冷卻半步 |
| **SDC（Spectral Deferred Correction）** | `nyx.sdc_split = 1` | 流體和加熱冷卻反覆修正，彼此耦合得更緊。**LyA 預設用這個** |

### SDC 一個時間步的流程（LyA 用的方法）

> 對應程式碼：`Nyx::advance` → `advance_hydro_plus_particles`（`Source/TimeStep/Nyx_advance.cpp`）→ `sdc_hydro`（`Source/Hydro/sdc_hydro.cpp`）

```
┌─ 0. 決定 Δt，並把 a 從 aⁿ 推進到 aⁿ⁺¹（Friedmann 方程）
│
├─ 1. 重力 gⁿ：粒子 CIC 分配 + 氣體密度 → 解 Poisson
├─ 2. 粒子 Kick(Δt/2) + Drift(Δt)：速度用 gⁿ 更新半步，位置更新整步
│
├─ 3. 流體：用 Godunov/PPM 算出通量造成的變化 A（加上上一步的加熱冷卻修正項 I_R 當作源項）
├─ 4. 更新動量和總能量（流體通量 + 重力源項 Sgⁿ）
├─ 5. CVODE：在 [tⁿ, tⁿ⁺¹] 上同時積分「流體造成的變化 + 加熱冷卻」
├─ 6. 算出新的修正項 I_R = 加熱冷卻實際造成的能量變化
├─ 7. 用 I_R 修正內能和總能量，並更新溫度和電子密度
├─ 8. 重複 3 到 7（SDC 迭代，讓流體和加熱冷卻彼此一致）
│
├─ 9. 重力 gⁿ⁺¹：用新的粒子位置和新的氣體密度再解一次 Poisson
├─ 10. 重力源項做時間置中修正：U += Δt/2 · (Sgⁿ⁺¹ − Sgⁿ)
└─ 11. 粒子 Kick(Δt/2)：用 gⁿ⁺¹ 把速度補完
```

這樣每一步在時間上都是二階精度，而且質量、動量都守恆。

---

## 9. 時間步長怎麼決定

> 程式碼：`Nyx::est_time_step`（`Source/Driver/Nyx.cpp`）、`comoving_est_time_step`（`comoving.cpp`）

Δt 取以下幾個限制裡**最小的那一個**：

| 限制 | 設定 | 意義 |
|---|---|---|
| 流體 CFL | `nyx.cfl = 0.5` | 聲波和流體在一步內最多走半個格子 |
| 粒子 CFL | `particles.cfl = 0.5` | 粒子在一步內最多走半個格子 |
| 宇宙膨脹 | `nyx.relative_max_change_a = 0.01` | 尺度因子 a 一步最多變化 1% |
| 輸出時間點 | `nyx.plot_z_values` | 會自動縮短 Δt，剛好停在指定的紅移輸出 |
| 變化速率 | `nyx.change_max = 2.0` | Δt 一步最多變成原本的 2 倍 |

在我們的模擬裡，**早期主要受「a 每步最多變 1%」限制**。從 a = 1/160 到 1/3 至少需要 ln(160/3)/ln(1.01) ≈ 400 步，和實際的 407 步幾乎一樣。這代表整個過程幾乎都是被宇宙膨脹限制住，而不是被 CFL 限制。

---

## 10. 自適應網格加密（AMR）

> 底層是 AMReX 的 `AmrLevel` 框架；文件見 `Refinement.rst`、`GridCreation.rst`

- 在密度高的地方（例如暈、纖維）自動放上更細的網格，加密比例通常是 2 或 4，可以多層巢狀。
- 每隔幾步做一次 **regrid**：根據條件標記需要加密的格子（例如 `amr.density.value_greater`），再用 Berger–Rigoutsos 演算法把標記的格子組成矩形網格塊。
- 細網格用**較小的 Δt 做子循環**（subcycling）；粗細網格之間在交界面做通量修正，維持守恆。
- 重力可以跨層一起解（composite solve），也可以分層解（level solve）。

> 我們的 LyA 範例設定 **`amr.max_level = 0`，也就是沒有加密**，只用一層均勻的 64³ 網格。網格切成 8 塊 32³（`amr.max_grid_size = 32`），由 OpenMP 平行處理。

---

## 11. 初始條件

> 文件：`ICs.rst`；程式碼：`Source/Initialization/`

LyA 範例的設定：
```
nyx.particle_init_type   = BinaryFile
nyx.binary_particle_file = 64sssss_20mpc.nyx    # 14 MB
nyx.do_santa_barbara     = 1
nyx.init_sb_vels         = 1
nyx.initial_z            = 159.0
```

1. **暗物質粒子**是從二進位檔讀進來的。檔頭是粒子數加上維度資訊，接著每顆粒子有 7 個 float：`x y z mass vx vy vz`。這些位置和速度是外部初始條件產生器（例如用 Zel'dovich 近似，根據功率譜擾動均勻排列的粒子）事先算好的。
2. **氣體**用「Santa Barbara」的做法初始化：假設一開始**重子追隨暗物質**，把粒子用 CIC 分配到網格上當作氣體密度，再乘上 Ω_b/Ω_m；速度也從粒子分配過來（`init_sb_vels = 1`）。
3. 初始溫度由早期宇宙的狀態決定，大約幾百 K。

也可以用 `Random`、`OnePerCell`、讀網格檔（`Cosmological` + `do_readinics`）等其他方式初始化，主要用於測試。

---

## 12. 輸出檔案格式

### Plotfile（`pltNNNNN/`，NNNNN 是步數）
AMReX 的標準格式，`yt`、VisIt、ParaView、AMReX 的 `amrvis` 都能讀：

```
plt00407/
├── Header              # 文字檔：變數名稱、維度、時間、網格幾何
├── Level_0/            # 網格資料（二進位，按網格塊分開存）
│   ├── Cell_H
│   └── Cell_D_00000
├── DM/                 # 暗物質粒子（位置、速度、質量）
├── comoving_a          # 這個輸出時間點的尺度因子 a
├── job_info            # 編譯和執行資訊
└── the_parameters      # 所有 inputs 參數
```

LyA 輸出的網格變數（`amr.plot_vars` 加上 `amr.derive_plot_vars`）：

| 變數 | 意義 | 單位 |
|---|---|---|
| `density` | 氣體共動密度 | M☉/Mpc³ |
| `xmom` `ymom` `zmom` | 氣體動量 | M☉/Mpc³ · km/s |
| `rho_e` | 內能密度 | M☉/Mpc³ · (km/s)² |
| `Temp` | 溫度 | K |
| `phi_grav` | 重力位能 | (km/s)² |
| `particle_mass_density` | 暗物質密度（CIC 分配到網格上） | M☉/Mpc³ |
| `particle_count` | 每個格子裡的粒子數 | — |

用 yt 讀取時要加 `hint="NyxDataset"`：
```python
ds = yt.load("plt00407", hint="NyxDataset")
```

### Checkpoint（`chkNNNNN/`）
用來中斷後從那個時間點**繼續跑**（`amr.restart = chk00100`），不適合拿來分析。這次我們關掉了。

---

## 13. 單位系統

Nyx 內部用宇宙學單位，在呼叫狀態方程時才轉成 CGS：

| 物理量 | 單位 |
|---|---|
| 長度 | Mpc |
| 質量 | M☉（太陽質量） |
| 速度 | km/s |
| 時間 | Mpc/(km/s) ≈ 3.086×10¹⁹ 秒 ≈ 9,778 億年 |
| 溫度 | K |
| G | 4.3019425 × 10⁻⁹ Mpc (km/s)² / M☉ |

> 注意：網格大小是 `geometry.prob_hi = 28.49 Mpc`，也就是 20 Mpc/h（h = 0.702），這就是檔名 `20mpc` 的由來。

---

## 14. 對照我們這次的模擬結果

![LyA 64³ 氣體過密度投影（每張圖的顏色範圍各自縮放）](nyx_lya64_density.png)

| z | 宇宙年齡（約） | 氣體最大過密度 | 暗物質最大過密度 | 溫度中位數 | 最高溫度 | 物理意義 |
|---|---|---|---|---|---|---|
| 50 | 4,700 萬年 | 1.5 | 1.5 | 98 K | 3×10³ K | 線性階段，結構只是微小起伏；膨脹讓氣體絕熱冷卻 |
| 20 | 1.8 億年 | 3.2 | 2.7 | 368 K | 1×10⁴ K | 重力讓起伏慢慢變大 |
| 10 | 4.8 億年 | 11.6 | 7.0 | 3,759 K | 1×10⁴ K | **UVB 打開，再電離開始**，溫度跳升 |
| 8 | 6.5 億年 | 21 | 16 | 8,045 K | 3×10⁴ K | 再電離完成，IGM 被加熱到約 10⁴ K |
| 5 | 12 億年 | 85 | 92 | 7,472 K | 3×10⁵ K | 進入非線性階段，纖維和暈形成 |
| 3 | 22 億年 | 300 | 377 | 10,961 K | 4×10⁶ K | 氣體掉進暈裡，**被震波加熱**到百萬度 |
| 2 | 33 億年 | 207 | 415 | 9,025 K | 8×10⁶ K | 宇宙網成形 |

幾個值得注意的地方：

- **早期氣體和暗物質的過密度幾乎一樣**，這符合「重子追隨暗物質」的初始條件。到了後期，**氣體比暗物質平滑**，因為氣體有壓力，會抵抗重力塌縮（Jeans 平滑），暗物質則沒有壓力。
- **溫度中位數大約維持在 10⁴ K**：這是光致電離加熱和膨脹冷卻互相抵銷後的平衡溫度，也就是 Lyman-α 森林氣體的典型溫度。
- **最高溫度一路升高到 10⁶ 到 10⁷ K**：這是掉進重力位井的氣體被震波加熱，形成溫熱星系際介質（WHIM）。
- 64³ 的解析度很低（每格 0.45 Mpc），所以暈的中心解析不出來，過密度只到幾百。真正的科學模擬會用 1024³ 到 4096³ 加上 AMR。

---

## 15. 程式碼導覽

```
Source/
├── Driver/
│   ├── main.cpp / nyx_main.cpp   # 程式進入點：初始化 AMReX，建立 Amr 物件，跑主迴圈
│   ├── Nyx.cpp / Nyx.H           # 核心類別 Nyx（繼承 AmrLevel）：時間步估計、post_timestep、regrid、負載平衡
│   ├── NyxBld.cpp                # 工廠類別，讓 AMReX 可以建立 Nyx level
│   ├── comoving.cpp              # Friedmann 方程積分：a(t)、Δt 受 a 變化率的限制
│   └── Nyx_LyAForest.cpp         # Lyman-α 相關分析
├── TimeStep/
│   ├── Nyx_advance.cpp           # ★ 每個時間步的總指揮：advance → advance_hydro_plus_particles
│   └── Nyx_sources.cpp           # 外部源項（預設為空）
├── Hydro/
│   ├── sdc_hydro.cpp             # ★ SDC 分裂的流體加加熱冷卻迭代
│   ├── strang_hydro.cpp          # Strang 分裂版本
│   ├── Godunov.cpp/.H            # 非分裂 Godunov：重建、角落耦合、通量
│   ├── trace_ppm.cpp, PPM.H      # PPM 重建和特徵追蹤
│   └── Riemann.H                 # Riemann 求解器
├── Gravity/Gravity.cpp           # ★ Poisson 方程（AMReX MLMG 多層網格）
├── Particle/
│   ├── DarkMatterParticleContainer.*  # ★ 暗物質粒子：Kick-Drift-Kick、CIC
│   └── NyxParticles.cpp          # 粒子初始化和管理
├── HeatCool/
│   ├── sdc_reactions.cpp         # SDC 版的加熱冷卻
│   ├── integrate_state_*.cpp     # ★ 呼叫 CVODE 解每個格子的能量 ODE
│   └── f_rhs.H                   # ODE 右手邊：de/dt = Λ_HC
├── EOS/
│   ├── eos_hc.H                  # 電離平衡的牛頓迭代：(ρ, e) → (T, n_e, 各種離子比例)
│   └── atomic_rates*             # 復合率、碰撞電離率等原子資料，加上 TREECOOL 的 UVB
├── Initialization/               # 初始條件：讀粒子檔、Santa Barbara 氣體初始化
├── DerivedQuantities/            # 衍生變數（particle_mass_density 等）
└── IO/Nyx_output.cpp             # plotfile 和 checkpoint 輸出
```

### 建議的閱讀順序
1. `Driver/nyx_main.cpp`：了解主迴圈；
2. `TimeStep/Nyx_advance.cpp` 的 `advance_hydro_plus_particles`：看一個時間步的完整流程；
3. `Hydro/sdc_hydro.cpp`：流體和加熱冷卻怎麼耦合；
4. `Particle/DarkMatterParticleContainer.cpp`：粒子怎麼移動；
5. `Gravity/Gravity.cpp`：重力怎麼解。

---

## 參考資料
- Almgren et al. 2013, *Nyx: A Massively Parallel AMR Code for Computational Cosmology*, ApJ 765, 39
- Lukić et al. 2015, *The Lyman α forest in optically thin hydrodynamical simulations*, MNRAS 446, 3697（加熱冷卻演算法）
- Sexton et al. 2021, *Nyx: A Massively Parallel AMR Code for Computational Cosmology*, JOSS 6(63), 3068
- 官方文件：<https://amrex-astro.github.io/Nyx/docs_html/>
- AMReX 文件：<https://amrex-codes.github.io/amrex/docs_html/>
