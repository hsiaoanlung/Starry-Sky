using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 畫面右上角顯示播放速度與模擬時間，並用鍵盤控制 NBodySimulator.daysPerSecond。
/// 按鍵說明見 Assets/Docs/時間控制按鍵說明.md。
/// </summary>
public class TimeControlUI : MonoBehaviour
{
    public NBodySimulator simulator;

    [Header("速度")]
    [Tooltip("數字鍵 1~6 對應的播放速度（天/秒）")]
    public double[] presets = { 0.1, 1, 5, 30, 100, 365 };

    public double minDaysPerSecond = 0.01;
    public double maxDaysPerSecond = 1000;

    [Header("顯示")]
    public bool showHelp = true;
    public int fontSize = 16;

    bool paused;
    double measuredRate;                // 實際每秒推進的模擬天數
    double lastSimDays;
    float lastMeasureTime;
    GUIStyle style;

    void Awake()
    {
        if (simulator == null) simulator = GetComponent<NBodySimulator>();
    }

    void Start()
    {
        lastSimDays = simulator.SimulatedDays;
        lastMeasureTime = Time.unscaledTime;
    }

    void OnDisable() => Time.timeScale = 1f;   // 離開時別讓遊戲停在暫停狀態

    void Update()
    {
        HandleKeys();
        MeasureRate();
    }

    void HandleKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame)
            SetSpeed(simulator.daysPerSecond * 2);
        if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame)
            SetSpeed(simulator.daysPerSecond / 2);

        Key[] digitKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6 };
        for (int i = 0; i < digitKeys.Length && i < presets.Length; i++)
            if (kb[digitKeys[i]].wasPressedThisFrame) SetSpeed(presets[i]);

        if (kb.spaceKey.wasPressedThisFrame)
        {
            // 用 timeScale 暫停：NBodySimulator 用 Time.deltaTime 推進時間，TrailRenderer 也跟著停住，軌跡不會在暫停時消失
            paused = !paused;
            Time.timeScale = paused ? 0f : 1f;
        }

        if (kb.hKey.wasPressedThisFrame) showHelp = !showHelp;
    }

    void SetSpeed(double daysPerSecond)
    {
        simulator.daysPerSecond = System.Math.Clamp(daysPerSecond, minDaysPerSecond, maxDaysPerSecond);
    }

    /// <summary>每 0.5 秒（真實時間）量一次模擬實際推進多少天，用來發現算力跟不上的情況。</summary>
    void MeasureRate()
    {
        float elapsed = Time.unscaledTime - lastMeasureTime;
        if (elapsed < 0.5f) return;
        measuredRate = (simulator.SimulatedDays - lastSimDays) / elapsed;
        lastSimDays = simulator.SimulatedDays;
        lastMeasureTime = Time.unscaledTime;
    }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, richText = true };
            style.padding = new RectOffset(10, 10, 8, 8);
        }
        style.fontSize = fontSize;

        double target = simulator.daysPerSecond;
        bool lagging = !paused && measuredRate < target * 0.9;
        int years = (int)(simulator.SimulatedDays / 365.25);
        double days = simulator.SimulatedDays - years * 365.25;

        string text =
            $"播放速度：<b>{FormatSpeed(target)}</b> 天/秒" + (paused ? "  <color=#FFD040>（暫停中）</color>" : "") + "\n" +
            $"實際速度：" + (lagging ? $"<color=#FF6060>{FormatSpeed(measuredRate)} 天/秒（算力跟不上）</color>" : $"{FormatSpeed(measuredRate)} 天/秒") + "\n" +
            $"模擬時間：{years} 年 {days:F1} 天";

        if (showHelp)
            text += "\n\n" +
                "<b>+</b> / <b>-</b>　加速 ×2 / 減速 ÷2\n" +
                $"<b>1</b>~<b>{System.Math.Min(6, presets.Length)}</b>　　預設速度 {string.Join(" / ", System.Array.ConvertAll(presets, FormatSpeed))}\n" +
                "<b>空白鍵</b>　暫停 / 繼續\n" +
                "<b>T</b>　　 軌跡顯示模式\n" +
                "<b>H</b>　　 隱藏說明";
        else
            text += "\n<color=#A0A0A0>按 H 顯示按鍵說明</color>";

        var content = new GUIContent(text);
        Vector2 size = style.CalcSize(content);
        GUI.Box(new Rect(Screen.width - size.x - 10, 10, size.x, size.y), content, style);   // 右上角，左邊留給天體列表
    }

    static string FormatSpeed(double v) =>
        v >= 10 ? v.ToString("0") : v >= 1 ? v.ToString("0.#") : v.ToString("0.###");
}
