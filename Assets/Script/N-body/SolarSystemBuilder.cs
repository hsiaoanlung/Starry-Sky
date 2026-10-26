using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 從 Planets.csv 讀取天體資料（parent、質量、半長軸 a、離心率 e），自動建立太陽、行星與衛星。
/// 每顆天體從相對 parent 的近日點出發，位置在 parent 的 +x 方向、速度沿 +z，軌道面為 XZ 平面。
/// parent 必須在 CSV 中排在前面。
/// </summary>
public class SolarSystemBuilder : MonoBehaviour
{
    public NBodySimulator simulator;

    [Header("天體資料")]
    [Tooltip("天體資料表 CSV（可用 Excel 編輯）。留空則讀 Assets/Resources/Planets.csv")]
    public TextAsset planetTable;

    [System.NonSerialized] public PlanetSpec[] planets;   // 執行時由 CSV 載入

    [Header("外觀")]
    [Tooltip("太陽使用電漿表面與日冕效果（SunVisual）")]
    public bool sunVisual = true;

    [Header("軌跡")]
    public bool addTrails = true;

    [Tooltip("軌跡長度 = 公轉週期的幾分之幾。0.3 = 顯示 0.3 圈；1 = 剛好一整圈")]
    [Range(0.05f, 1f)] public float trailOrbitFraction = 0.3f;

    // 每條軌跡與其天體的公轉週期（天），Update 時依播放速度換算成 TrailRenderer.time
    readonly List<(TrailRenderer trail, double periodDays)> trails = new List<(TrailRenderer, double)>();

    void Awake()
    {
        if (simulator == null) simulator = GetComponent<NBodySimulator>();

        if (planetTable == null) planetTable = Resources.Load<TextAsset>("Planets");
        if (planetTable == null)
        {
            Debug.LogError("[SolarSystemBuilder] 找不到天體資料表 Planets.csv");
            planets = new PlanetSpec[0];
        }
        else
        {
            planets = PlanetSpec.ParseCsv(planetTable.text);
            Debug.Log($"[SolarSystemBuilder] 從 {planetTable.name} 載入 {planets.Length} 顆天體");
        }

        var created = new Dictionary<string, CelestialBody>();
        foreach (var p in planets)
        {
            if (string.IsNullOrEmpty(p.parent))
            {
                // 沒有 parent（太陽）：放在原點靜止
                created[p.name] = Create(p, Vec3d.zero, Vec3d.zero, null, 0);   // 不畫軌跡
                if (sunVisual) created[p.name].gameObject.AddComponent<SunVisual>();   // 電漿表面 + 日冕
                continue;
            }

            if (!created.TryGetValue(p.parent, out var parent))
            {
                Debug.LogError($"[SolarSystemBuilder] {p.name} 的 parent「{p.parent}」不存在，或排在它後面，略過");
                continue;
            }

            // 近日點啟動（教學第 6.2 節）：r_p = a(1-e)，v_p = sqrt(mu (1+e) / r_p)，再疊加 parent 的位置與速度
            double mu = NBodySimulator.G * (parent.mass + p.mass);
            double rp = p.a * (1.0 - p.e);
            double vp = System.Math.Sqrt(mu * (1.0 + p.e) / rp);
            if (p.retrograde) vp = -vp;   // 逆行：速度沿 -z，順時針公轉
            double period = 2.0 * System.Math.PI * System.Math.Sqrt(p.a * p.a * p.a / mu);   // 克卜勒第三定律
            created[p.name] = Create(p,
                parent.position + new Vec3d(rp, 0, 0),
                parent.velocity + new Vec3d(0, 0, vp),
                parent, period);
        }

        simulator.RemoveCenterOfMassDrift();   // 總動量歸零（第 6.4 節）
        simulator.Initialize();
        UpdateTrailTimes();
        foreach (var t in trails) t.trail.Clear();   // 清掉從原點拉到初始位置的那一段
    }

    void Update() => UpdateTrailTimes();

    /// <summary>軌跡秒數 = 週期(天) × 比例 ÷ 每秒模擬天數。每幀更新，執行中調整播放速度或比例也會立刻反映。</summary>
    void UpdateTrailTimes()
    {
        double daysPerSecond = System.Math.Max(simulator.daysPerSecond, 1e-6);
        foreach (var t in trails)
            t.trail.time = (float)(t.periodDays * trailOrbitFraction / daysPerSecond);
    }

    CelestialBody Create(PlanetSpec spec, Vec3d pos, Vec3d vel, CelestialBody parent, double periodDays)
    {
        string name = spec.name;
        float radius = spec.displayRadius;
        Color color = spec.color;

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.localScale = Vector3.one * radius * 2f;
        Destroy(go.GetComponent<Collider>());            // 不需要碰撞

        var renderer = go.GetComponent<Renderer>();
        renderer.material.color = color;

        if (addTrails && periodDays > 0)
        {
            var trail = go.AddComponent<TrailRenderer>();
            trail.minVertexDistance = radius * 0.5f;   // 小星（月球）軌跡更細緻，大星省頂點
            trail.startWidth = radius * 0.5f;
            trail.endWidth = 0f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.startColor = color;
            trail.endColor = new Color(color.r, color.g, color.b, 0f);
            trails.Add((trail, periodDays));
        }

        var body = go.AddComponent<CelestialBody>();
        body.bodyName = name;
        body.mass = spec.mass;
        body.position = pos;
        body.velocity = vel;
        body.parent = parent;
        body.orbitScale = spec.orbitScale;

        simulator.bodies.Add(body);
        return body;
    }
}
