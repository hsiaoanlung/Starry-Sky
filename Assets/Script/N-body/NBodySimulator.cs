using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// N-body 重力模擬核心：O(N^2) 直接求和 + Velocity Verlet 積分。
/// 單位：AU、太陽質量、天。
/// </summary>
public class NBodySimulator : MonoBehaviour
{
    /// <summary>萬有引力常數，AU^3 / (M_sun * day^2)，等於高斯引力常數的平方 k^2。</summary>
    public const double G = 2.959122082855911e-4;

    [Header("時間")]
    [Tooltip("每一步積分推進多少模擬天。決定精度，水星需要 <= 1，月球需要 <= 0.05。")]
    public double dt = 0.25;

    [Tooltip("每秒真實時間推進多少模擬天。決定播放速度。")]
    public double daysPerSecond = 30;

    [Tooltip("單一幀最多跑幾步，避免掉幀時雪崩。超過時本幀剩下的時間直接捨棄，模擬會比設定的速度慢。")]
    public int maxStepsPerFrame = 2000;

    [Header("顯示")]
    [Tooltip("1 AU 對應幾個 Unity 單位")]
    public float unityUnitsPerAU = 10f;

    [Header("數值")]
    [Tooltip("softening 長度 (AU)。太陽系設 0 即可。")]
    public double softening = 0.0;

    [Header("診斷")]
    [Tooltip("每隔幾模擬天在 Console 印一次能量誤差，0 = 不印")]
    public double logEveryDays = 100;

    public List<CelestialBody> bodies = new List<CelestialBody>();

    public double SimulatedDays { get; private set; }

    double accumulator;
    double initialEnergy;
    double nextLogTime;
    bool initialized;

    void Start()
    {
        if (!initialized) Initialize();
    }

    /// <summary>蒐集場景中的 CelestialBody，算初始加速度與能量。Builder 建完天體後會呼叫一次。</summary>
    public void Initialize()
    {
        if (bodies.Count == 0)
            bodies.AddRange(FindObjectsOfType<CelestialBody>());

        ComputeAccelerations();
        initialEnergy = TotalEnergy();
        nextLogTime = logEveryDays;
        SyncTransforms();
        initialized = true;
        Debug.Log($"[NBody] {bodies.Count} bodies, E0 = {initialEnergy:E6}");
    }

    void Update()
    {
        if (!initialized) return;

        accumulator += Time.deltaTime * daysPerSecond;
        int steps = 0;
        while (accumulator >= dt && steps < maxStepsPerFrame)
        {
            Step(dt);
            accumulator -= dt;
            steps++;
        }
        if (steps >= maxStepsPerFrame) accumulator = 0;   // 算力跟不上：捨棄積欠的時間，否則降速後還會持續追趕
        SyncTransforms();

        if (logEveryDays > 0 && SimulatedDays >= nextLogTime)
        {
            nextLogTime += logEveryDays;
            Debug.Log($"[NBody] t = {SimulatedDays:F1} d, dE/E = {RelativeEnergyError():E3}, |L| = {TotalAngularMomentum().Magnitude:E6}");
        }
    }

    /// <summary>Velocity Verlet：半步速度 → 整步位置 → 重算加速度 → 半步速度。</summary>
    void Step(double h)
    {
        foreach (var b in bodies)
        {
            b.velocity += b.acceleration * (0.5 * h);
            b.position += b.velocity * h;
        }
        ComputeAccelerations();
        foreach (var b in bodies)
            b.velocity += b.acceleration * (0.5 * h);

        SimulatedDays += h;
    }

    /// <summary>a_i = sum_j G m_j (r_j - r_i) / (|r_j - r_i|^2 + eps^2)^(3/2)，利用對稱只算 i&lt;j。</summary>
    void ComputeAccelerations()
    {
        int n = bodies.Count;
        for (int i = 0; i < n; i++) bodies[i].acceleration = Vec3d.zero;

        double eps2 = softening * softening;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                Vec3d d = bodies[j].position - bodies[i].position;   // 從 i 指向 j
                double r2 = d.SqrMagnitude + eps2;
                double invR3 = 1.0 / (r2 * System.Math.Sqrt(r2));
                bodies[i].acceleration += d * (G * bodies[j].mass * invR3);
                bodies[j].acceleration -= d * (G * bodies[i].mass * invR3);
            }
        }
    }

    /// <summary>衛星的顯示位置 = parent 的顯示位置 + 放大 orbitScale 倍的相對位置。parent 必須排在前面。</summary>
    void SyncTransforms()
    {
        foreach (var b in bodies)
        {
            if (b.parent == null || b.orbitScale == 1f)
                b.transform.position = b.position.ToVector3(unityUnitsPerAU);
            else
                b.transform.position = b.parent.transform.position
                    + (b.position - b.parent.position).ToVector3(unityUnitsPerAU * b.orbitScale);
        }
    }

    // ---------- 守恆量 ----------

    /// <summary>E = sum 1/2 m v^2 - sum_{i&lt;j} G m_i m_j / r_ij</summary>
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

    public double RelativeEnergyError() =>
        System.Math.Abs(TotalEnergy() - initialEnergy) / System.Math.Abs(initialEnergy);

    /// <summary>L = sum m_i (r_i x v_i)</summary>
    public Vec3d TotalAngularMomentum()
    {
        Vec3d L = Vec3d.zero;
        foreach (var b in bodies)
            L += Vec3d.Cross(b.position, b.velocity) * b.mass;
        return L;
    }

    /// <summary>把所有速度減去質心速度，讓總動量為零，系統不會整體漂移。</summary>
    public void RemoveCenterOfMassDrift()
    {
        double totalMass = 0;
        Vec3d momentum = Vec3d.zero;
        Vec3d massPos = Vec3d.zero;
        foreach (var b in bodies)
        {
            totalMass += b.mass;
            momentum += b.velocity * b.mass;
            massPos += b.position * b.mass;
        }
        Vec3d vCm = momentum / totalMass;
        Vec3d rCm = massPos / totalMass;
        foreach (var b in bodies)
        {
            b.velocity -= vCm;
            b.position -= rCm;
        }
    }
}
