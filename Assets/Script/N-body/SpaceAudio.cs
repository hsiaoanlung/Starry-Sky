using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 背景音樂（只影響聲音，不碰物理）：循環播放程序合成的太空氛圍音樂 SpaceAmbience.wav。
/// 音樂由 Assets/Docs/space_music_gen.py 產生，頭尾無縫，直接 loop 就不會聽到接縫。
/// M 鍵靜音 / 取消靜音（淡入淡出；暫停模擬時也能切換）。音樂不受播放速度與暫停影響。
/// 由 SolarSystemBuilder 掛在 Simulator 上；數值在 Update 每幀套用，Play 中可即時調整。停止 Play 後數值會還原。
/// 說明見 Assets/Docs/背景音樂說明.md。
/// </summary>
public class SpaceAudio : MonoBehaviour
{
    [Tooltip("背景音樂。留空 = 讀 Resources/Audio/SpaceAmbience")]
    public AudioClip clip;

    [Tooltip("音量（0 = 無聲，1 = 原始音量）。背景音樂通常 0.2 ~ 0.5 就夠")]
    [Range(0f, 1f)] public float volume = 0.35f;

    [Tooltip("靜音（M 鍵切換）")]
    public bool muted = false;

    [Tooltip("開始播放時淡入的秒數。越大進場越慢")]
    public float fadeInSeconds = 4f;

    [Tooltip("靜音 / 取消靜音的淡入淡出秒數")]
    public float muteFadeSeconds = 0.6f;

    AudioSource source;
    float fade;         // 0 ~ 1，實際音量 = volume × fade²
    bool introDone;     // 第一次淡入結束後，改用比較快的靜音淡入淡出

    void Start()
    {
        if (clip == null) clip = Resources.Load<AudioClip>("Audio/SpaceAmbience");
        if (clip == null)
        {
            Debug.LogWarning("[SpaceAudio] 找不到 Resources/Audio/SpaceAmbience，沒有背景音樂");
            enabled = false;
            return;
        }

        source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;            // 檔案本身頭尾接得起來，交給 AudioSource 循環
        source.spatialBlend = 0f;      // 2D 聲音：音量不隨相機距離變化
        source.playOnAwake = false;
        source.priority = 0;           // 最高優先權，聲音多時不會被擠掉
        source.volume = 0f;
        source.Play();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.mKey.wasPressedThisFrame) muted = !muted;

        // 用真實時間淡入淡出：暫停（timeScale = 0）時也能切換。
        // 第一次進場用 fadeInSeconds，之後靜音切換用 muteFadeSeconds
        float goal = muted ? 0f : 1f;
        float seconds = introDone ? muteFadeSeconds : fadeInSeconds;
        fade = Mathf.MoveTowards(fade, goal, Time.unscaledDeltaTime / Mathf.Max(seconds, 0.01f));
        if (fade >= 1f || muted) introDone = true;

        // 人耳對音量是對數感受：fade² 聽起來比線性 fade 更平均
        source.volume = volume * fade * fade;
    }
}
