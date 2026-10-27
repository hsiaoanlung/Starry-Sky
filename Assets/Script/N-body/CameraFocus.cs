using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 左側天體列表 + 點選天體後，相機從正上方跟隨並自動縮放到適合的範圍。
/// 操作說明見 Assets/Docs/相機與天體列表說明.md。
/// </summary>
public class CameraFocus : MonoBehaviour
{
    public NBodySimulator simulator;
    public Camera cam;

    [Header("鏡頭")]
    [Tooltip("自動縮放時，在要容納的範圍外再留多少空間（倍數）")]
    public float framingMargin = 1.4f;

    [Tooltip("沒有衛星的天體：畫面範圍 = 星球半徑的幾倍")]
    public float radiusMultiplier = 6f;

    [Tooltip("切換目標時飛過去的時間（秒）")]
    public float transitionTime = 0.8f;

    [Tooltip("滾輪每一格縮放幾倍")]
    public float zoomStep = 1.25f;

    [Tooltip("在畫面上點擊時，離星球幾像素內算點中")]
    public float pickRadiusPixels = 25f;

    [Header("列表")]
    public int fontSize = 15;
    public float panelWidth = 190f;

    CelestialBody target;               // null = 初始視角
    float baseHeight;                   // 自動算出的相機高度
    float zoom = 1f;                    // 滾輪縮放倍數，乘在 baseHeight 上
    float currentHeight;
    float homeHeight;
    Vector3 transitionStart;
    float transitionT = 1f;             // 0 → 1 表示飛行進度，1 = 已到達、直接跟隨

    Rect panelRect;
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
        baseHeight = currentHeight = homeHeight;

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
            bool overPanel = panelRect.Contains(new Vector2(p.x, Screen.height - p.y));   // IMGUI 的 y 軸朝下

            float wheel = mouse.scroll.ReadValue().y;
            if (wheel != 0 && !overPanel)
                zoom = Mathf.Clamp(zoom * (wheel > 0 ? 1f / zoomStep : zoomStep), 0.02f, 50f);

            if (mouse.leftButton.wasPressedThisFrame && !overPanel)
            {
                var picked = Pick(p);
                if (picked != null) Focus(picked);
            }
        }

        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) GoHome();
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
        transitionStart = cam.transform.position;
        transitionT = 0f;
    }

    public void GoHome()
    {
        target = null;
        zoom = 1f;
        baseHeight = homeHeight;
        transitionStart = cam.transform.position;
        transitionT = 0f;
    }

    /// <summary>
    /// 算出能把天體（含它的衛星）放進畫面的相機高度。
    /// 範圍 = max(星球半徑 × radiusMultiplier, 最遠衛星在畫面上的距離)，高度 = 範圍 × 留白 ÷ tan(視角/2)。
    /// </summary>
    float FrameHeight(CelestialBody body)
    {
        float extent = body.transform.localScale.x * 0.5f * radiusMultiplier;
        foreach (var b in simulator.bodies)
            if (b.parent == body)
                extent = Mathf.Max(extent, Vector3.Distance(b.transform.position, body.transform.position));
        return extent * framingMargin / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
    }

    /// <summary>在 LateUpdate 移動相機：NBodySimulator 已在 Update 更新完天體位置，相機才不會慢一幀。</summary>
    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;   // 用真實時間，暫停時鏡頭仍可移動
        Vector3 focus = target != null ? target.transform.position : Vector3.zero;

        currentHeight = Mathf.Lerp(currentHeight, baseHeight * zoom, 1f - Mathf.Exp(-8f * dt));
        Vector3 desired = focus + Vector3.up * currentHeight;

        if (transitionT < 1f)
        {
            transitionT = Mathf.Min(1f, transitionT + dt / transitionTime);
            cam.transform.position = Vector3.Lerp(transitionStart, desired, Mathf.SmoothStep(0f, 1f, transitionT));
        }
        else
        {
            cam.transform.position = desired;   // 到達後直接鎖定，跑得再快也不會落後
        }
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 正上方往下看

        // 放大到衛星時距離很近，near clip 要跟著變小，否則星球會被切掉
        cam.nearClipPlane = Mathf.Max(0.001f, currentHeight * 0.01f);
        cam.farClipPlane = Mathf.Max(1000f, currentHeight * 3f);
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
        float wanted = (simulator.bodies.Count + 6) * rowHeight;
        panelRect = new Rect(10, 10, panelWidth, Mathf.Min(Screen.height - 20, wanted));

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
}
