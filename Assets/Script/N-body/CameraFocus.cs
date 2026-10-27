using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 左上天體列表 + 點選天體後相機跟隨並自動縮放；選了天體後，左下角可以切換視角（俯視、斜視、側視、迎光、背光、從星球看出去）。
/// 相機是「繞著目標的軌道相機」：位置 = 目標 − 視線方向 × 距離，視線方向由俯仰角（pitch）與方位角（yaw）決定。
/// 操作說明見 Assets/Docs/相機與天體列表說明.md、視角切換說明.md。
/// </summary>
public class CameraFocus : MonoBehaviour
{
    public enum ViewMode { Top, Oblique, Side, SunLit, Backlit, FromSurface }

    static readonly string[] ViewNames =
    {
        "俯視", "斜視", "側視（軌道面）", "迎光（太陽在身後）", "背光（看向太陽）", "從星球看出去",
    };

    public NBodySimulator simulator;
    public Camera cam;

    [Header("鏡頭")]
    [Tooltip("自動縮放時，在要容納的範圍外再留多少空間（倍數）")]
    public float framingMargin = 1.4f;

    [Tooltip("沒有衛星的天體：畫面範圍 = 星球半徑的幾倍")]
    public float radiusMultiplier = 6f;

    [Tooltip("切換目標或視角時飛過去的時間（秒）")]
    public float transitionTime = 0.8f;

    [Tooltip("滾輪每一格縮放幾倍")]
    public float zoomStep = 1.25f;

    [Tooltip("在畫面上點擊時，離星球幾像素內算點中")]
    public float pickRadiusPixels = 25f;

    [Header("視角")]
    [Tooltip("斜視的俯仰角（度）。90 = 正上方往下看，0 = 水平")]
    public float obliquePitch = 35f;

    [Tooltip("側視的俯仰角（度）。稍微大於 0，軌跡才不會完全變成一條線")]
    public float sidePitch = 3f;

    [Tooltip("迎光（太陽在相機身後）的俯仰角（度）")]
    public float sunLitPitch = 10f;

    [Tooltip("背光（從星球背後看向太陽）的俯仰角（度）")]
    public float backlitPitch = 5f;

    [Tooltip("從星球看出去：相機離星球中心幾倍半徑（1 = 貼在表面）。越接近 1，畫面下方的星球越大")]
    public float surfaceHeight = 1.15f;

    [Tooltip("從星球看出去：預設的俯仰角（度），正 = 往下看、負 = 往上看。8 = 稍微往下，畫面下緣看得到一點星球")]
    public float surfacePitch = 8f;

    [Tooltip("右鍵拖曳：每移動 1 像素轉幾度")]
    public float dragSensitivity = 0.2f;

    [Header("列表")]
    public int fontSize = 15;
    public float panelWidth = 190f;

    CelestialBody target;               // null = 初始視角
    ViewMode view = ViewMode.Top;
    float yawOffset, pitchOffset;       // 右鍵拖曳累積的角度，切換視角時歸零
    float baseHeight;                   // 自動算出的相機距離
    float zoom = 1f;                    // 滾輪縮放倍數，乘在 baseHeight 上
    float currentDistance;
    float homeHeight;
    float defaultFov, surfaceFov;       // 「從星球看出去」時滾輪改的是視野角度（望遠鏡）
    Vector3 transitionStart;
    Quaternion transitionStartRot;
    float transitionT = 1f;             // 0 → 1 表示飛行進度，1 = 已到達、直接跟隨

    Rect panelRect, viewRect;
    Vector2 scroll;
    GUIStyle itemStyle, selectedStyle, headerStyle, hintStyle;
    readonly Dictionary<CelestialBody, int> depth = new Dictionary<CelestialBody, int>();
    readonly Dictionary<CelestialBody, Color> colors = new Dictionary<CelestialBody, Color>();

    void Awake()
    {
        if (simulator == null) simulator = GetComponent<NBodySimulator>();
        if (cam == null) cam = Camera.main;
    }

    void Start()
    {
        // 初始視角：太陽系原點正上方，高度沿用場景中相機的設定
        homeHeight = cam.transform.position.y;
        baseHeight = currentDistance = homeHeight;
        defaultFov = surfaceFov = cam.fieldOfView;

        foreach (var b in simulator.bodies)
        {
            int d = 0;
            for (var p = b.parent; p != null; p = p.parent) d++;
            depth[b] = d;
            colors[b] = b.displayColor;   // 用 CSV 的顏色；Prefab 的 Shader（例如太陽）不一定有 _Color
        }
    }

    // ---------- 輸入 ----------

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 p = mouse.position.ReadValue();
            Vector2 gui = new Vector2(p.x, Screen.height - p.y);   // IMGUI 的 y 軸朝下
            bool overPanel = panelRect.Contains(gui) || (target != null && viewRect.Contains(gui));

            float wheel = mouse.scroll.ReadValue().y;
            if (wheel != 0 && !overPanel)
            {
                float f = wheel > 0 ? 1f / zoomStep : zoomStep;
                if (target != null && view == ViewMode.FromSurface)
                    surfaceFov = Mathf.Clamp(surfaceFov * f, 1f, 90f);   // 視野越小 = 望遠鏡倍率越高
                else
                    zoom = Mathf.Clamp(zoom * f, 0.02f, 50f);
            }

            if (mouse.leftButton.wasPressedThisFrame && !overPanel)
            {
                var picked = Pick(p);
                if (picked != null) Focus(picked);
            }

            // 右鍵拖曳：在目前的視角上再轉（軌道視角 = 繞著星球轉；從星球看出去 = 轉頭）
            if (target != null && mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yawOffset += delta.x * dragSensitivity;
                pitchOffset -= delta.y * dragSensitivity;
            }
        }

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.escapeKey.wasPressedThisFrame) GoHome();
            if (kb.vKey.wasPressedThisFrame && target != null) CycleView();
        }
    }

    /// <summary>找出螢幕上離滑鼠最近、且在 pickRadiusPixels 內的天體。</summary>
    CelestialBody Pick(Vector2 mousePos)
    {
        CelestialBody best = null;
        float bestDist = pickRadiusPixels;
        foreach (var b in simulator.bodies)
        {
            Vector3 s = cam.WorldToScreenPoint(b.transform.position);
            if (s.z < 0) continue;                                  // 在相機後面
            float d = Vector2.Distance(mousePos, s);
            if (d < bestDist) { bestDist = d; best = b; }
        }
        return best;
    }

    // ---------- 鏡頭 ----------

    public void Focus(CelestialBody body)
    {
        target = body;
        zoom = 1f;
        baseHeight = FrameHeight(body);
        if (!IsAvailable(view)) view = ViewMode.Top;   // 例如選太陽時沒有「迎光 / 背光」
        yawOffset = pitchOffset = 0f;
        StartTransition();
    }

    public void GoHome()
    {
        target = null;
        view = ViewMode.Top;
        zoom = 1f;
        baseHeight = homeHeight;
        yawOffset = pitchOffset = 0f;
        StartTransition();
    }

    /// <summary>切換視角；再按一次同一個視角 = 把右鍵拖曳的角度重設。</summary>
    public void SetView(ViewMode mode)
    {
        if (!IsAvailable(mode)) return;
        view = mode;
        yawOffset = pitchOffset = 0f;
        surfaceFov = defaultFov;
        StartTransition();
    }

    void CycleView()
    {
        var m = view;
        do m = (ViewMode)(((int)m + 1) % ViewNames.Length); while (!IsAvailable(m));
        SetView(m);
    }

    void StartTransition()
    {
        transitionStart = cam.transform.position;
        transitionStartRot = cam.transform.rotation;
        transitionT = 0f;
    }

    /// <summary>「迎光 / 背光」需要知道太陽在哪個方向，所以目標本身是太陽時不能用。</summary>
    bool IsAvailable(ViewMode mode)
    {
        if (target == null) return mode == ViewMode.Top;
        if (mode == ViewMode.SunLit || mode == ViewMode.Backlit) return Sun(target) != target;
        return true;
    }

    /// <summary>光源 = 沿 parent 往上找到的最上層天體（太陽）。</summary>
    static CelestialBody Sun(CelestialBody b)
    {
        while (b.parent != null) b = b.parent;
        return b;
    }

    /// <summary>水平方向 v 的方位角（度）：Unity 的 yaw = 0 朝 +z、yaw = 90 朝 +x。</summary>
    static float YawOf(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

    /// <summary>
    /// 算出能把天體（含它的衛星）放進畫面的相機距離。
    /// 範圍 = max(星球半徑 × radiusMultiplier, 最遠衛星在畫面上的距離)，距離 = 範圍 × 留白 ÷ tan(視角/2)。
    /// </summary>
    float FrameHeight(CelestialBody body)
    {
        float extent = body.transform.localScale.x * 0.5f * radiusMultiplier;
        foreach (var b in simulator.bodies)
            if (b.parent == body)
                extent = Mathf.Max(extent, Vector3.Distance(b.transform.position, body.transform.position));
        return extent * framingMargin / Mathf.Tan(defaultFov * 0.5f * Mathf.Deg2Rad);
    }

    /// <summary>依目前的視角算出相機該在的位置與方向。</summary>
    void ComputeView(out Vector3 position, out Quaternion rotation)
    {
        if (target == null)   // 初始視角：太陽系中心正上方
        {
            rotation = Quaternion.Euler(90f, 0f, 0f);
            position = Vector3.up * currentDistance;
            return;
        }

        Vector3 focus = target.transform.position;
        Vector3 toSun = Sun(target).transform.position - focus;   // 目標是太陽時為 0
        float pitch = 90f, yaw = 0f;
        switch (view)
        {
            case ViewMode.Oblique:     pitch = obliquePitch; break;
            case ViewMode.Side:        pitch = sidePitch; break;
            case ViewMode.SunLit:      pitch = sunLitPitch;  yaw = YawOf(-toSun); break;   // 和陽光同方向看 → 太陽在身後
            case ViewMode.Backlit:     pitch = backlitPitch; yaw = YawOf(toSun);  break;   // 朝太陽看 → 星球擋在前面
            case ViewMode.FromSurface: pitch = surfacePitch; yaw = toSun.sqrMagnitude > 0 ? YawOf(toSun) : 0f; break;
        }
        // Euler(pitch, yaw, 0)：先繞 y 轉 yaw（左右），再繞 x 轉 pitch（上下）。pitch 正 = 往下看
        rotation = Quaternion.Euler(Mathf.Clamp(pitch + pitchOffset, -89f, 89f), yaw + yawOffset, 0f);

        if (view == ViewMode.FromSurface)
        {
            // 站在星球上方（世界的 +y 那一側），往外看；星球在相機下方
            float radius = target.transform.localScale.x * 0.5f;
            position = focus + Vector3.up * radius * surfaceHeight;
        }
        else
        {
            position = focus - rotation * Vector3.forward * currentDistance;   // 軌道相機：從目標往視線反方向退 distance
        }
    }

    /// <summary>在 LateUpdate 移動相機：NBodySimulator 已在 Update 更新完天體位置，相機才不會慢一幀。</summary>
    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;   // 用真實時間，暫停時鏡頭仍可移動
        float smooth = 1f - Mathf.Exp(-8f * dt);
        currentDistance = Mathf.Lerp(currentDistance, baseHeight * zoom, smooth);

        ComputeView(out Vector3 desired, out Quaternion desiredRot);

        if (transitionT < 1f)
        {
            transitionT = Mathf.Min(1f, transitionT + dt / transitionTime);
            float s = Mathf.SmoothStep(0f, 1f, transitionT);
            cam.transform.position = Vector3.Lerp(transitionStart, desired, s);
            cam.transform.rotation = Quaternion.Slerp(transitionStartRot, desiredRot, s);   // 旋轉用 Slerp：沿球面最短路徑轉
        }
        else
        {
            cam.transform.position = desired;   // 到達後直接鎖定，跑得再快也不會落後
            cam.transform.rotation = desiredRot;
        }

        bool surface = target != null && view == ViewMode.FromSurface;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, surface ? surfaceFov : defaultFov, smooth);

        // 放大到衛星時距離很近，near clip 要跟著變小，否則星球會被切掉
        float near = surface ? target.transform.localScale.x * 0.5f * (surfaceHeight - 1f) * 0.1f : currentDistance * 0.01f;
        cam.nearClipPlane = Mathf.Max(0.0005f, near);
        cam.farClipPlane = Mathf.Max(1000f, currentDistance * 3f);
    }

    // ---------- 列表 ----------

    void OnGUI()
    {
        if (itemStyle == null)
        {
            itemStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
            selectedStyle = new GUIStyle(itemStyle) { fontStyle = FontStyle.Bold };
            selectedStyle.normal.textColor = selectedStyle.hover.textColor = new Color(1f, 0.85f, 0.3f);
            headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            hintStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            hintStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
        }
        itemStyle.fontSize = selectedStyle.fontSize = headerStyle.fontSize = fontSize;
        hintStyle.fontSize = fontSize - 2;

        float rowHeight = fontSize + 12;

        // 左下角：視角面板（選了天體才出現）
        float viewHeight = 0f;
        if (target != null)
        {
            viewHeight = (ViewNames.Length + 3.5f) * rowHeight;
            viewRect = new Rect(10, Screen.height - viewHeight - 10, panelWidth, viewHeight);
            DrawViewPanel();
        }

        // 左上角：天體列表（有視角面板時讓出下方空間）
        float wanted = (simulator.bodies.Count + 6) * rowHeight;
        float available = Screen.height - 20 - (viewHeight > 0 ? viewHeight + 10 : 0);
        panelRect = new Rect(10, 10, panelWidth, Mathf.Min(available, wanted));

        GUILayout.BeginArea(panelRect, GUI.skin.box);
        GUILayout.Label(target != null ? $"天體列表　目前：{target.bodyName}" : "天體列表", headerStyle);

        scroll = GUILayout.BeginScrollView(scroll);
        foreach (var b in simulator.bodies)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(depth.TryGetValue(b, out int d) ? d * 14 : 0);   // 衛星縮排

            var old = GUI.color;
            GUI.color = colors.TryGetValue(b, out var c) ? c : Color.white;
            GUILayout.Label("●", GUILayout.Width(fontSize));
            GUI.color = old;

            if (GUILayout.Button(b.bodyName, b == target ? selectedStyle : itemStyle))
                Focus(b);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        if (GUILayout.Button("重設視角（Esc）")) GoHome();
        GUILayout.Label("點畫面上的星球也可選取\n滾輪：縮放", hintStyle);
        GUILayout.EndArea();
    }

    void DrawViewPanel()
    {
        GUILayout.BeginArea(viewRect, GUI.skin.box);
        GUILayout.Label($"視角　{target.bodyName}", headerStyle);
        for (int i = 0; i < ViewNames.Length; i++)
        {
            var mode = (ViewMode)i;
            GUI.enabled = IsAvailable(mode);   // 不能用的視角顯示成灰色
            if (GUILayout.Button(ViewNames[i], mode == view ? selectedStyle : itemStyle))
                SetView(mode);
        }
        GUI.enabled = true;
        GUILayout.Label(view == ViewMode.FromSurface
            ? "右鍵拖曳：轉頭　滾輪：望遠鏡\nV：下一個視角"
            : "右鍵拖曳：繞著轉　V：下一個視角\n再按一次同一個視角 = 重設角度", hintStyle);
        GUILayout.EndArea();
    }
}
