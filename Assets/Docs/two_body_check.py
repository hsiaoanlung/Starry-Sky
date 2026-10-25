"""太陽 + 地球 兩體問題，Velocity Verlet。
執行：python two_body_check.py
應看到一圈橢圓，並印出相對能量誤差（量級 1e-8 以下）。
需要 matplotlib（沒有的話把最後幾行畫圖註解掉）。
"""
import math

G = 2.959122082855911e-4        # AU^3 / (M_sun * day^2)
M_SUN, M_EARTH = 1.0, 3.0e-6
A, E = 1.0, 0.0167               # 地球半長軸(AU)、離心率
DT, DAYS = 0.25, 365.25 * 2      # 步長(天)、模擬總天數

# 近日點啟動（第 6.2 節）
mu = G * (M_SUN + M_EARTH)
rp = A * (1 - E)
vp = math.sqrt(mu * (1 + E) / rp)
r = [rp, 0.0]                    # 地球相對太陽的位置（二維即可）
v = [0.0, vp]


def accel(r):                    # 兩體：只算太陽對地球的加速度（相對座標）
    d2 = r[0] ** 2 + r[1] ** 2
    inv_r3 = 1.0 / (d2 * math.sqrt(d2))
    return [-mu * inv_r3 * r[0], -mu * inv_r3 * r[1]]


def energy(r, v):                # 單位質量的比能量
    return 0.5 * (v[0] ** 2 + v[1] ** 2) - mu / math.hypot(r[0], r[1])


a = accel(r)
e0 = energy(r, v)
xs, ys = [], []
t = 0.0
while t < DAYS:
    v = [v[0] + 0.5 * DT * a[0], v[1] + 0.5 * DT * a[1]]   # 半步速度
    r = [r[0] + DT * v[0], r[1] + DT * v[1]]               # 整步位置
    a = accel(r)                                           # 新加速度
    v = [v[0] + 0.5 * DT * a[0], v[1] + 0.5 * DT * a[1]]   # 半步速度
    t += DT
    xs.append(r[0]); ys.append(r[1])

print(f"相對能量誤差: {abs(energy(r, v) - e0) / abs(e0):.3e}")

import matplotlib.pyplot as plt
plt.plot(xs, ys, lw=0.8); plt.plot(0, 0, "o", color="orange")
plt.gca().set_aspect("equal"); plt.xlabel("x (AU)"); plt.ylabel("y (AU)")
plt.title("Earth orbit, Velocity Verlet, dt = 0.25 day"); plt.show()
