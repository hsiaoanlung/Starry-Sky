using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 從 Planets.csv 讀取天體資料（parent、質量、半長軸 a、離心率 e），自動建立太陽、行星與衛星。
/// 每顆天體從相對 parent 的近日點出發，位置在 parent 的 +x 方向、速度沿 +z，軌道面為 XZ 平面。
/// parent 必須在 CSV 中排在前面。CSV 的 prefab 欄有填路徑時，用 Resources.Load 讀取對應的 Prefab 當外觀。
/// </summary>
public class SolarSystemBuilder : MonoBehaviour
{
    public NBodySimulator simulator;

    [Header("天體資料")]
    [Tooltip("天體資料表 CSV（可用 Excel 編輯）。留空則讀 Assets/Resources/Planets.csv")]
    public TextAsset planetTable;

    [System.NonSerialized] public PlanetSpec[] planets;   // 執行時由 CSV 載入

    [Header("外觀")]
    [Tooltip("使用 CSV prefab 欄指定的 Prefab。取消勾選 = 全部用純色球（比較用）")]
    public bool usePrefabs = true;

    [Tooltip("太陽沒有 Prefab（prefab 欄空白、讀不到或 Use Prefabs 關閉）時，程式產生的球是否加上 SunVisual（電漿表面 + 日冕）")]
    public bool sunVisual = true;

    [Header("自轉")]
    [Tooltip("依 CSV 的 rotationPeriod 欄讓天體自轉（BodySpin）。Play 中可即時開關")]
    public bool addSpin = true;

    [Tooltip("自轉用的模擬時間上限（模擬天 / 真實秒）。播放速度超過這個值時，所有天體的自轉一起變慢，快慢比例不變。" +
             "1 = 地球每秒轉一圈；0 = 不設上限（完全照模擬時間，高速時會轉到看不清）")]
    public float maxSpinDaysPerSecond = 1f;

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
                // 沒有 parent（太陽）：放在原點靜止，不畫軌跡
                var sun = Create(p, Vec3d.zero, Vec3d.zero, null, 0);
                created[p.name] = sun;
                if (sunVisual && sun.GetComponent<SunVisual>() == null) sun.gameObject.AddComponent<SunVisual>();   // Sun.prefab 已經有 SunVisual
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

    /// <summary>讀取 CSV prefab 欄的 Prefab（路徑相對於任一 Resources 資料夾、不含 .prefab）。空白或讀不到就回傳 null。</summary>
    GameObject LoadPrefab(PlanetSpec spec)
    {
        if (!usePrefabs || string.IsNullOrEmpty(spec.prefab)) return null;
        var prefab = Resources.Load<GameObject>(spec.prefab);
        if (prefab == null)
            Debug.LogWarning($"[SolarSystemBuilder] {spec.name} 的 Prefab「{spec.prefab}」不存在（要放在 Resources 底下、路徑不含副檔名），改用純色球");
        return prefab;
    }

    /// <summary>建立一顆天體。CSV 有指定 Prefab 就複製它（外觀由 Prefab 決定），否則建立純色球體。大小一律用 CSV 的 displayRadius。</summary>
    CelestialBody Create(PlanetSpec spec, Vec3d pos, Vec3d vel, CelestialBody parent, double periodDays)
    {
        string name = spec.name;
        float radius = spec.displayRadius;
        Color color = spec.color;
        var prefab = LoadPrefab(spec);

        GameObject go;
        if (prefab != null)
        {
            go = Instantiate(prefab);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());        // 不需要碰撞
            go.GetComponent<Renderer>().material.color = color;
        }
        go.name = name;
        go.transform.localScale = Vector3.one * radius * 2f;   // Prefab 的子物件（日冕、土星環）跟著等比例縮放

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

        if (spec.rotationPeriod != 0)
        {
            var spin = go.AddComponent<BodySpin>();   // 繞 local y（Prefab 的自轉軸）轉
            spin.rotationPeriodDays = spec.rotationPeriod;
            spin.simulator = simulator;
            spin.builder = this;
        }

        var body = go.AddComponent<CelestialBody>();
        body.bodyName = name;
        body.mass = spec.mass;
        body.position = pos;
        body.velocity = vel;
        body.parent = parent;
        body.orbitScale = spec.orbitScale;
        body.displayColor = color;

        simulator.bodies.Add(body);
        return body;
    }
}
