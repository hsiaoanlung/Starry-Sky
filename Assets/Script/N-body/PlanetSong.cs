using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 行星之歌（只發出聲音，不碰物理）：每顆行星一個音，音高由它「現在」繞太陽的角速度決定，即時合成。
/// 克卜勒在《世界的和諧》（1619）裡就用這個想法：近日點走得快 → 音高高，遠日點走得慢 → 音高低。
///   f = 參考音高 × (n / n_地球)^spread × (ω / n)
///   n = 平均角速度（由位置、速度用 vis-viva 算出半長軸 a，再 n = sqrt(μ / a³)）
///   ω = 瞬時角速度 |r × v| / r²
/// 音量：離相機越近的行星越大聲（跟隨某顆行星時就主要聽到它），其他行星保留一點當背景和聲；左右聲道依畫面位置。
/// 合成在音訊執行緒的 OnAudioFilterRead 進行；主執行緒每幀只算目標音高 / 音量 / 聲像，音訊執行緒平滑地滑過去。
/// P 鍵開關。由 SolarSystemBuilder 建立在子物件「Planet Song」上；數值在 Update 每幀套用，Play 中可即時調整。停止 Play 後數值會還原。
/// 說明見 Assets/Docs/行星之歌說明.md。
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class PlanetSong : MonoBehaviour
{
    public NBodySimulator simulator;

    [Header("音量")]
    [Tooltip("整體音量（0 = 無聲）")]
    [Range(0f, 1f)] public float volume = 0.12f;

    [Tooltip("靜音（P 鍵切換）")]
    public bool muted = false;

    [Tooltip("離相機遠的行星最小音量（相對最近那顆）。0 = 只聽得到最近的行星；1 = 全部一樣大聲")]
    [Range(0f, 1f)] public float backgroundLevel = 0.15f;

    [Tooltip("距離造成的音量差。越大，最近的行星越突出")]
    [Range(0.5f, 4f)] public float distancePower = 2f;

    [Tooltip("左右聲道的分開程度（0 = 全在中間）")]
    [Range(0f, 1f)] public float stereoWidth = 0.7f;

    [Tooltip("開始播放與開關時的淡入淡出秒數")]
    public float fadeSeconds = 2f;

    [Header("音高")]
    [Tooltip("地球的平均音高（Hz）。587.33 = D5，和背景音樂同一個調")]
    public float referencePitch = 587.33f;

    [Tooltip("參考週期（天）：公轉週期等於這個值的行星，平均音高 = Reference Pitch")]
    public float referencePeriodDays = 365.25f;

    [Tooltip("行星之間音高差距的壓縮。1 = 真實比例（水星到海王星差 9.4 個八度，超出聽得到的範圍）；0.4 ≈ 4 個八度")]
    [Range(0.1f, 1f)] public float spread = 0.4f;

    [Tooltip("音高跟著瞬時角速度變（近日點高、遠日點低），起伏是真實比例：地球約一個半音，水星超過一個八度")]
    public bool keplerWobble = true;

    [Tooltip("把音高對齊到 D 小調五聲音階（和背景音樂比較和諧，但音高會一格一格跳）")]
    public bool snapToScale = false;

    [Tooltip("音高變化的平滑時間（秒）")]
    [Range(0.01f, 1f)] public float glideSeconds = 0.15f;

    [Tooltip("泛音量。0 = 純正弦波（很純、像音叉）；越大越亮")]
    [Range(0f, 1f)] public float brightness = 0.35f;

    [Header("顯示")]
    [Tooltip("跟隨行星（或它的衛星）時，在畫面下方顯示這顆行星的音名與頻率")]
    public bool showLabel = true;

    public int fontSize = 15;

    static readonly string[] NoteNames = { "C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B" };
    static readonly int[] Pentatonic = { 0, 2, 5, 7, 9 };   // D 小調五聲音階 D F G A C 的音級（C = 0）

    class Voice
    {
        public CelestialBody body;
        public float freq;          // 目前算出的音高（主執行緒）
        public float omegaRatio;    // ω / n，顯示用
    }

    readonly List<Voice> voices = new List<Voice>();
    CelestialBody sun;
    CameraFocus cameraFocus;
    GUIStyle labelStyle;

    // 主執行緒寫、音訊執行緒讀的目標值（float 讀寫是原子的，最多晚一個 buffer，聽不出來）
    float[] targetFreq, targetGain, targetPan;
    float[] dist;   // 每顆行星到相機的距離（重複使用，不每幀配置記憶體）
    float targetMaster;
    // 只有音訊執行緒用
    float[] curFreq, curGain, curPan;
    double[] phase;
    float curMaster, audioBrightness, audioGlide;
    int sampleRate;
    volatile bool ready;

    void Start()
    {
        if (simulator == null) simulator = FindAnyObjectByType<NBodySimulator>();
        cameraFocus = FindAnyObjectByType<CameraFocus>();

        // 找出太陽（沒有 parent）和行星（parent 是太陽）；衛星不發聲
        foreach (var b in simulator.bodies)
            if (b.parent == null) { sun = b; break; }
        foreach (var b in simulator.bodies)
            if (b.parent != null && b.parent == sun) voices.Add(new Voice { body = b });
        if (sun == null || voices.Count == 0)
        {
            Debug.LogWarning("[PlanetSong] 找不到太陽或行星，不發聲");
            enabled = false;
            return;
        }

        int n = voices.Count;
        targetFreq = new float[n]; targetGain = new float[n]; targetPan = new float[n];
        curFreq = new float[n]; curGain = new float[n]; curPan = new float[n];
        phase = new double[n];
        dist = new float[n];
        sampleRate = AudioSettings.outputSampleRate;
        UpdateTargets();
        System.Array.Copy(targetFreq, curFreq, n);   // 第一個 buffer 不要從 0 Hz 滑上來
        ready = true;

        // OnAudioFilterRead 要掛在「正在播放」的 AudioSource 上才會被呼叫，所以放一段無聲的循環當載體
        var source = GetComponent<AudioSource>();
        source.clip = AudioClip.Create("Silence", sampleRate, 2, sampleRate, false);   // 立體聲，濾波器才拿得到左右聲道
        source.loop = true;
        source.spatialBlend = 0f;
        source.playOnAwake = false;
        source.Play();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.pKey.wasPressedThisFrame) muted = !muted;

        // 淡入淡出用真實時間：暫停時也能開關
        float goal = muted ? 0f : volume;
        targetMaster = Mathf.MoveTowards(targetMaster, goal, Time.unscaledDeltaTime * Mathf.Max(volume, 0.01f) / Mathf.Max(fadeSeconds, 0.01f));
        audioBrightness = brightness;
        audioGlide = glideSeconds;
        UpdateTargets();
    }

    /// <summary>每幀依物理狀態算每顆行星的音高，依相機算音量與聲像。</summary>
    void UpdateTargets()
    {
        double nRef = 2.0 * System.Math.PI / referencePeriodDays;
        var cam = Camera.main;
        float minDist = float.MaxValue;

        for (int i = 0; i < voices.Count; i++)
        {
            var v = voices[i];
            Vec3d r = v.body.position - sun.position;   // 相對太陽（AU）
            Vec3d u = v.body.velocity - sun.velocity;   // 相對速度（AU/天）
            double r2 = r.SqrMagnitude;
            double omega = Vec3d.Cross(r, u).Magnitude / r2;   // 瞬時角速度（弧度/天）= 角動量 / r²

            // vis-viva：v² = μ (2/r - 1/a) → 1/a = 2/r - v²/μ；再由克卜勒第三定律 n = sqrt(μ / a³)
            double mu = NBodySimulator.G * (sun.mass + v.body.mass);
            double invA = 2.0 / System.Math.Sqrt(r2) - u.SqrMagnitude / mu;
            double n = invA > 0 ? System.Math.Sqrt(mu * invA * invA * invA) : omega;   // 萬一不是橢圓軌道就退回 ω

            v.omegaRatio = (float)(omega / n);
            double f = referencePitch * System.Math.Pow(n / nRef, spread) * (keplerWobble ? omega / n : 1.0);
            if (snapToScale) f = SnapToPentatonic(f);
            v.freq = Mathf.Clamp((float)f, 20f, 8000f);
            targetFreq[i] = v.freq;

            if (cam != null)
            {
                Vector3 p = v.body.transform.position;
                dist[i] = Mathf.Max((p - cam.transform.position).magnitude, 1e-3f);
                minDist = Mathf.Min(minDist, dist[i]);
                Vector3 vp = cam.WorldToViewportPoint(p);
                targetPan[i] = vp.z > 0 ? Mathf.Clamp((vp.x - 0.5f) * 2f, -1f, 1f) * stereoWidth : 0f;   // 在相機後面就放中間
            }
            else
            {
                dist[i] = minDist = 1f;
                targetPan[i] = 0f;
            }
        }

        // 最近的行星 = 1，其他依距離比例變小，但不低於 backgroundLevel；總和超過 1 時整體壓回來，疊起來才不會爆音
        float total = 0f;
        for (int i = 0; i < voices.Count; i++)
        {
            float w = Mathf.Pow(minDist / dist[i], distancePower);
            targetGain[i] = backgroundLevel + (1f - backgroundLevel) * w;
            total += targetGain[i];
        }
        float norm = 1f / Mathf.Max(1f, total);
        for (int i = 0; i < voices.Count; i++) targetGain[i] *= norm;
    }

    static double SnapToPentatonic(double f)
    {
        double m = 69.0 + 12.0 * System.Math.Log(f / 440.0, 2.0);
        int baseNote = (int)System.Math.Floor(m);
        double best = m, bestDiff = double.MaxValue;
        for (int k = baseNote - 3; k <= baseNote + 3; k++)
        {
            int pc = ((k % 12) + 12) % 12;
            if (System.Array.IndexOf(Pentatonic, pc) < 0) continue;
            double d = System.Math.Abs(k - m);
            if (d < bestDiff) { bestDiff = d; best = k; }
        }
        return 440.0 * System.Math.Pow(2.0, (best - 69.0) / 12.0);
    }

    /// <summary>
    /// 音訊執行緒：每次被呼叫時填一個 buffer（通常 1024 個取樣、約 20 毫秒）。不能呼叫 Unity API。
    /// 參數在 buffer 內線性內插，相位連續累加，音高改變時不會有喀聲。
    /// </summary>
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!ready) return;
        int frames = data.Length / channels;
        float dtBuffer = (float)frames / sampleRate;
        float kFreq = 1f - Mathf.Exp(-dtBuffer / Mathf.Max(audioGlide, 0.001f));   // 一階平滑：每個 buffer 往目標靠近一點
        float kGain = 1f - Mathf.Exp(-dtBuffer / 0.08f);
        float b = audioBrightness;
        float hNorm = 1f / (1f + 0.75f * b);

        float m0 = curMaster;
        float m1 = curMaster + (targetMaster - curMaster) * kGain;
        curMaster = m1;

        for (int i = 0; i < curFreq.Length; i++)
        {
            float f0 = curFreq[i], g0 = curGain[i], p0 = curPan[i];
            float f1 = f0 + (targetFreq[i] - f0) * kFreq;
            float g1 = g0 + (targetGain[i] - g0) * kGain;
            float p1 = p0 + (targetPan[i] - p0) * kGain;
            curFreq[i] = f1; curGain[i] = g1; curPan[i] = p1;
            if (g0 < 1e-5f && g1 < 1e-5f && m1 < 1e-5f) continue;

            double ph = phase[i];
            for (int s = 0; s < frames; s++)
            {
                float t = (float)s / frames;
                float f = f0 + (f1 - f0) * t;
                float amp = (g0 + (g1 - g0) * t) * (m0 + (m1 - m0) * t);
                ph += 2.0 * System.Math.PI * f / sampleRate;
                // 基音 + 第 2、3 泛音（和背景音樂的 pad 同樣的作法，泛音越高越弱）
                float x = (float)(System.Math.Sin(ph) + b * 0.5 * System.Math.Sin(2 * ph) + b * 0.25 * System.Math.Sin(3 * ph)) * hNorm * amp;

                if (channels >= 2)
                {
                    float pan = p0 + (p1 - p0) * t;
                    float ang = (pan + 1f) * Mathf.PI * 0.25f;            // 等功率聲像
                    data[s * channels] += x * Mathf.Cos(ang);
                    data[s * channels + 1] += x * Mathf.Sin(ang);
                }
                else data[s] += x;
            }
            phase[i] = ph % (2.0 * System.Math.PI);   // 防止相位越加越大、精度變差
        }
    }

    void OnGUI()
    {
        if (!showLabel || muted || cameraFocus == null || !ready) return;
        var target = cameraFocus.Target;
        if (target == null) return;
        Voice v = voices.Find(x => x.body == target || x.body == target.parent);   // 選衛星時顯示它的行星
        if (v == null) return;

        if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, richText = true };
        labelStyle.fontSize = fontSize;
        string text = $"♪ {v.body.bodyName}　<b>{NoteName(v.freq)}</b>　{v.freq:0} Hz　" +
                      $"<color=#A0A0A0>角速度 = 平均的 {v.omegaRatio:0.00} 倍（P 開關）</color>";
        GUI.Label(new Rect(0, Screen.height - fontSize * 2 - 14, Screen.width, fontSize * 2), text, labelStyle);
    }

    /// <summary>頻率 → 音名 + 偏差音分，例如 587 Hz → D5 +0。</summary>
    static string NoteName(float f)
    {
        double m = 69.0 + 12.0 * System.Math.Log(f / 440.0, 2.0);
        int r = (int)System.Math.Round(m);
        int cents = (int)System.Math.Round((m - r) * 100);
        return $"{NoteNames[((r % 12) + 12) % 12]}{r / 12 - 1} {(cents >= 0 ? "+" : "")}{cents}";
    }
}
