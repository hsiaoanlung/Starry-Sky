using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 軌跡外觀（只影響畫面）：發光細線、固定像素寬度、顯示模式。
/// 1. 寬度：每幀依「相機到星球的距離」把像素換算成世界單位，拉近拉遠在螢幕上都差不多粗。
/// 2. 材質：StarrySky/Trail（加法混合），線只會讓畫面變亮，不會蓋住後面的行星。
/// 3. 顯示模式：T 鍵或右下角按鈕切換「全部 / 行星 + 目前系統 / 只有目前系統 / 關閉」，切換時淡入淡出。
/// 軌跡本身（TrailRenderer、長度 = 公轉週期的比例）仍由 SolarSystemBuilder 建立；這裡只管外觀。
/// 由 SolarSystemBuilder 掛在 Simulator 上；數值在 LateUpdate 每幀套用，Play 中可即時調整。停止 Play 後數值會還原。
/// 說明見 Assets/Docs/軌跡外觀說明.md。
/// </summary>
[DefaultExecutionOrder(100)]   // 在 CameraFocus 移動相機之後才算寬度，才不會慢一幀
public class TrailVisual : MonoBehaviour
{
    public enum Mode { All, PlanetsAndFocus, FocusOnly, Off }

    static readonly string[] ModeNames = { "全部", "行星 + 目前系統", "只有目前系統", "關閉" };

    [Tooltip("軌跡材質（StarrySky/Trail）。留空 = 讀 Resources/Materials/Trail")]
    public Material trailMaterial;

    [Header("顯示")]
    [Tooltip("全部：所有天體。行星 + 目前系統：八大行星，加上目前跟隨的行星的衛星。" +
             "只有目前系統：只有跟隨中的天體和它的衛星（選衛星時 = 它的母行星系統）。關閉：都不顯示")]
    public Mode mode = Mode.PlanetsAndFocus;

    [Tooltip("切換顯示模式時淡入淡出的時間（秒）")]
    public float fadeTime = 0.4f;

    [Tooltip("在右下角顯示切換按鈕")]
    public bool showButton = true;

    [Header("線")]
    [Tooltip("頭端的寬度（螢幕像素）。越大越粗")]
    [Range(0.5f, 6f)] public float widthPixels = 1.5f;

    [Tooltip("尾端寬度 = 頭端的幾倍。1 = 一樣粗，0 = 收成尖點")]
    [Range(0f, 1f)] public float tailWidth = 0.4f;

    [Tooltip("亮度（越大越亮）。最亮的顏色 × 亮度保持在 1 以下，才不會觸發 Bloom")]
    [Range(0f, 2f)] public float intensity = 0.5f;

    [Tooltip("頭端淡入長度（佔軌跡比例）。越大星球正後方空出越多")]
    [Range(0f, 0.3f)] public float headFade = 0.04f;

    [Tooltip("尾端淡出曲線。越大尾巴消失得越快；1 = 線性")]
    [Range(0.5f, 4f)] public float tailPower = 1.5f;

    [Tooltip("文字大小")]
    public int fontSize = 15;

    class Entry
    {
        public TrailRenderer trail;
        public CelestialBody body;
        public Color color;
        public float visibility = 1f;     // 0 = 隱藏、1 = 顯示，淡入淡出用
        public float appliedVisibility = -1f;
    }

    readonly List<Entry> entries = new List<Entry>();
    Material instance;          // 複製一份，Play 中調參數不會改到材質檔
    CameraFocus cameraFocus;
    GUIStyle buttonStyle;

    /// <summary>軌跡用的材質（共用一份）。SolarSystemBuilder 建立軌跡時呼叫；讀不到時回傳 null。</summary>
    public Material Material
    {
        get
        {
            if (instance == null)
            {
                if (trailMaterial == null) trailMaterial = Resources.Load<Material>("Materials/Trail");
                if (trailMaterial != null) instance = new Material(trailMaterial);
                else Debug.LogWarning("[TrailVisual] 找不到 Resources/Materials/Trail，軌跡改用 Sprites/Default");
            }
            return instance;
        }
    }

    /// <summary>登記一條軌跡。顏色由這裡每幀設定（rgb = 天體顏色、alpha = 沿軌跡的位置 1 → 0，Shader 用它算淡出）。</summary>
    public void Register(TrailRenderer trail, CelestialBody body, Color color)
    {
        trail.startWidth = 1f;   // 實際寬度 = widthMultiplier，每幀依距離算
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        entries.Add(new Entry { trail = trail, body = body, color = color });
    }

    void Start()
    {
        cameraFocus = GetComponent<CameraFocus>();
        if (cameraFocus == null) cameraFocus = FindAnyObjectByType<CameraFocus>();
        foreach (var e in entries) e.visibility = IsShown(e.body, Target) ? 1f : 0f;   // 一開始不要淡入
    }

    CelestialBody Target => cameraFocus != null ? cameraFocus.Target : null;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.tKey.wasPressedThisFrame) CycleMode();
    }

    public void CycleMode() => mode = (Mode)(((int)mode + 1) % ModeNames.Length);

    void LateUpdate()
    {
        if (instance != null)
        {
            instance.SetFloat("_Intensity", intensity);
            instance.SetFloat("_HeadFade", headFade);
            instance.SetFloat("_TailPower", tailPower);
        }

        var cam = Camera.main;
        var target = Target;
        // 用真實時間淡入淡出：暫停（timeScale = 0）時也能切換
        float step = fadeTime > 0f ? Time.unscaledDeltaTime / fadeTime : 1f;
        // 1 像素在距離 1 的地方 = 多少世界單位（透視相機：畫面高度 = 2 × 距離 × tan(視角/2)）
        float pixelAtUnitDistance = cam != null ? 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height : 0f;

        foreach (var e in entries)
        {
            if (e.trail == null) continue;
            e.visibility = Mathf.MoveTowards(e.visibility, IsShown(e.body, target) ? 1f : 0f, step);
            e.trail.forceRenderingOff = e.visibility <= 0f;   // 只是不畫，仍繼續記錄位置，重新顯示時不會拉出直線

            if (cam != null)
            {
                // 沿相機視線方向的距離（深度），透視投影的縮小比例由它決定
                float depth = Vector3.Dot(e.trail.transform.position - cam.transform.position, cam.transform.forward);
                depth = Mathf.Max(depth, cam.nearClipPlane);
                e.trail.widthMultiplier = widthPixels * pixelAtUnitDistance * depth;
            }
            if (e.trail.endWidth != tailWidth) e.trail.endWidth = tailWidth;

            if (e.visibility != e.appliedVisibility) ApplyColor(e);
        }
    }

    /// <summary>rgb = 天體顏色 × 可見度；alpha 從頭 1 到尾 0，Shader 拿來當「在軌跡上的位置」。只在可見度改變時重設（Gradient 每次都會配置記憶體）。</summary>
    static void ApplyColor(Entry e)
    {
        Color c = e.color * e.visibility;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        e.trail.colorGradient = g;
        e.appliedVisibility = e.visibility;
    }

    /// <summary>依顯示模式決定這顆天體的軌跡要不要顯示。</summary>
    bool IsShown(CelestialBody body, CelestialBody target)
    {
        switch (mode)
        {
            case Mode.All:             return true;
            case Mode.PlanetsAndFocus: return IsPlanet(body) || InSystem(body, target);
            case Mode.FocusOnly:       return InSystem(body, target);
            default:                   return false;
        }
    }

    /// <summary>行星 = parent 是最上層天體（太陽）。</summary>
    static bool IsPlanet(CelestialBody b) => b.parent != null && b.parent.parent == null;

    /// <summary>
    /// 「目前系統」= 系統中心和它的直接衛星。系統中心：沒有跟隨 = 太陽（→ 八大行星）；
    /// 跟隨行星或太陽 = 它自己；跟隨衛星 = 它的母行星（→ 看到兄弟衛星）。
    /// </summary>
    static bool InSystem(CelestialBody b, CelestialBody target)
    {
        CelestialBody root;
        if (target == null) { root = b; while (root.parent != null) root = root.parent; }
        else root = target.parent != null && target.parent.parent != null ? target.parent : target;
        return b == root || b.parent == root;
    }

    void OnGUI()
    {
        if (!showButton) return;
        if (buttonStyle == null) buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.fontSize = fontSize;

        var content = new GUIContent($"軌跡：{ModeNames[(int)mode]}（T）");
        Vector2 size = buttonStyle.CalcSize(content);
        size.x += 10;
        size.y += 6;
        if (GUI.Button(new Rect(Screen.width - size.x - 10, Screen.height - size.y - 10, size.x, size.y), content, buttonStyle))   // 右下角
            CycleMode();
    }
}
