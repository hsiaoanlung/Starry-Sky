"""完整 N-body 驗證：讀取 Planets.csv，用與 Unity 相同的方法模擬，檢查結果是否正確。
執行：python nbody_check.py                       （預設 dt = 0.001 天，模擬 100 天）
      python nbody_check.py --dt 0.002 --days 365
      python nbody_check.py --csv 其他資料表.csv
需要 numpy。說明見 nbody_check說明.md。
"""
import argparse
import csv
import math
import os
import time

import numpy as np

G = 2.959122082855911e-4         # AU^3 / (M_sun * day^2)，與 NBodySimulator.G 相同

ENERGY_TOL = 1e-8                # 相對能量誤差上限
BOUND_TOL = 0.5                  # 與 parent 距離 / a 的允許偏差（超過視為被甩掉或撞上）

here = os.path.dirname(os.path.abspath(__file__))
parser = argparse.ArgumentParser()
parser.add_argument("--csv", default=os.path.join(here, "..", "Resources", "Planets.csv"))
parser.add_argument("--dt", type=float, default=0.001, help="步長（天）")
parser.add_argument("--days", type=float, default=100, help="模擬總天數")
parser.add_argument("--sample", type=int, default=20, help="每幾步記錄一次距離與角度")
args = parser.parse_args()


# ---------- 1. 讀 CSV（對應 PlanetSpec.ParseCsv） ----------

rows = []
with open(args.csv, encoding="utf-8-sig") as f:
    for r in csv.DictReader(f):
        if r["name"].startswith("#"):
            continue
        rows.append(r)


# ---------- 2. 初始條件（對應 SolarSystemBuilder.Awake） ----------

names, mass, pos, vel, parent, a_list, e_list, kepler = [], [], [], [], [], [], [], []
index = {}
for r in rows:
    m = float(r["mass"]); a = float(r["a"]); e = float(r["e"])
    if not r["parent"]:                                  # 沒有 parent：原點靜止
        p, v, pi, T = np.zeros(3), np.zeros(3), -1, 0.0
    else:
        pi = index[r["parent"]]                          # parent 必須排在前面
        mu = G * (mass[pi] + m)
        rp = a * (1 - e)                                 # 近日點距離
        vp = math.sqrt(mu * (1 + e) / rp)                # vis-viva 近日點速度
        if r.get("retrograde", "0").strip() == "1":
            vp = -vp
        p = pos[pi] + [rp, 0, 0]                         # 疊加 parent 的位置
        v = vel[pi] + [0, 0, vp]                         # 疊加 parent 的速度
        T = 2 * math.pi * math.sqrt(a ** 3 / mu)         # 克卜勒第三定律（理論週期）
    index[r["name"]] = len(names)
    names.append(r["name"]); mass.append(m); pos.append(p); vel.append(v)
    parent.append(pi); a_list.append(a); e_list.append(e); kepler.append(T)

m = np.array(mass); P = np.array(pos); V = np.array(vel)
n = len(m)

# 總動量歸零（對應 NBodySimulator.RemoveCenterOfMassDrift）
V -= (m[:, None] * V).sum(0) / m.sum()
P -= (m[:, None] * P).sum(0) / m.sum()


# ---------- 3. 重力與能量（對應 ComputeAccelerations、TotalEnergy） ----------

def accelerations(P):
    d = P[None, :, :] - P[:, None, :]                    # d[i, j] = r_j - r_i
    r2 = (d ** 2).sum(-1)
    np.fill_diagonal(r2, 1.0)                            # 避免 i = j 除以零
    inv_r3 = r2 ** -1.5
    np.fill_diagonal(inv_r3, 0.0)                        # 自己不吸引自己
    return G * (d * (m[None, :, None] * inv_r3[:, :, None])).sum(1)


def total_energy(P, V):
    iu = np.triu_indices(n, 1)                           # 只算 i < j
    r = np.linalg.norm(P[None] - P[:, None], axis=-1)[iu]
    kinetic = 0.5 * (m * (V ** 2).sum(1)).sum()
    potential = -G * (m[:, None] * m[None])[iu].dot(1 / r)
    return kinetic + potential


# ---------- 4. Velocity Verlet（對應 NBodySimulator.Step） ----------

orbiters = [i for i in range(n) if parent[i] >= 0]

def relative(i):
    return P[i] - P[parent[i]]

r_min = {i: np.linalg.norm(relative(i)) for i in orbiters}
r_max = dict(r_min)
angle_prev = {i: math.atan2(relative(i)[2], relative(i)[0]) for i in orbiters}
angle_total = {i: 0.0 for i in orbiters}                 # 累積轉過的角度（弧度）

A = accelerations(P)
E0 = total_energy(P, V)
h = args.dt
steps = int(round(args.days / h))
t0 = time.time()

for s in range(1, steps + 1):
    V += 0.5 * h * A                                     # 半步速度
    P += h * V                                           # 整步位置
    A = accelerations(P)                                 # 新加速度
    V += 0.5 * h * A                                     # 半步速度

    if s % args.sample == 0:
        for i in orbiters:
            rel = relative(i)
            dist = np.linalg.norm(rel)
            r_min[i] = min(r_min[i], dist); r_max[i] = max(r_max[i], dist)
            ang = math.atan2(rel[2], rel[0])
            d_ang = (ang - angle_prev[i] + math.pi) % (2 * math.pi) - math.pi   # 取 -π ~ π 的差值
            angle_total[i] += d_ang
            angle_prev[i] = ang

elapsed = time.time() - t0
dE = abs(total_energy(P, V) - E0) / abs(E0)


# ---------- 5. 報告 ----------

print(f"資料表：{os.path.normpath(args.csv)}")
print(f"{n} 顆天體，dt = {h} 天，模擬 {args.days} 天（{steps} 步），耗時 {elapsed:.1f} 秒\n")

energy_ok = dE < ENERGY_TOL
print(f"[{'通過' if energy_ok else '失敗'}] 相對能量誤差 |dE/E| = {dE:.2e}（上限 {ENERGY_TOL:.0e}）\n")

print(f"{'天體':<10}{'parent':<9}{'r/a 最小':>9}{'r/a 最大':>9}{'圈數':>9}{'實測週期':>11}{'理論週期':>11}{'差異':>8}  結果")
all_bound = True
for i in orbiters:
    lo, hi = r_min[i] / a_list[i], r_max[i] / a_list[i]
    bound = (1 - BOUND_TOL) < lo and hi < (1 + BOUND_TOL)
    all_bound &= bound
    turns = abs(angle_total[i]) / (2 * math.pi)
    if turns >= 2:                                       # 至少兩圈才量週期，否則受離心率影響太大
        measured = args.days / turns
        diff = f"{(measured - kepler[i]) / kepler[i] * 100:+.2f}%"
        measured = f"{measured:.3f}"
    else:
        measured, diff = "—", "—"
    print(f"{names[i]:<10}{names[parent[i]]:<9}{lo:>9.3f}{hi:>9.3f}{turns:>9.1f}"
          f"{measured:>11}{kepler[i]:>11.3f}{diff:>8}  {'穩定' if bound else '脫離!'}")

print(f"\n[{'通過' if all_bound else '失敗'}] 所有天體與 parent 的距離維持在 a 的 ±{BOUND_TOL:.0%} 內")
