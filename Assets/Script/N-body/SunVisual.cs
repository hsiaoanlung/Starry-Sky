using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 太陽的外觀：流動電漿表面、包住太陽的體積日冕（從任何角度看都正確），並在 URP 下自動開啟 Bloom。
/// 平常已設定在 Prefabs/Sun.prefab 上；沒有指定 Prefab 時，SolarSystemBuilder 會把它加在程式產生的球上，由它自己建立材質與日冕。
/// 只影響畫面，不影響物理。
/// </summary>
public class SunVisual : MonoBehaviour
{
    [Header("元件（Prefab 已設定；留空時由程式建立）")]
    [Tooltip("太陽表面的 Renderer（StarrySky/SunSurface）。留空 = 用本物件的 Renderer 並換成電漿材質")]
    public Renderer surface;
    [Tooltip("日冕球的 Renderer（StarrySky/SunCorona）。留空 = 執行時建立子物件 Corona")]
    public Renderer corona;

    [Header("日冕")]
    [Tooltip("日冕球殼的半徑，以太陽半徑為單位。4 = 日冕延伸到 4 倍太陽半徑")]
    public float coronaSize = 4f;
    [Tooltip("日冕整體亮度")]
    public float coronaIntensity = 0.8f;

    [Header("Bloom（只在 URP 下有效）")]
    public bool addBloom = true;
    public float bloomThreshold = 1f;
    public float bloomIntensity = 1f;
    [Range(0f, 1f)] public float bloomScatter = 0.6f;

    Material coronaMaterial;
    Camera cam;
    Volume bloomVolume;   // URP 下才會建立
    Bloom bloom;

    void Start()
    {
        cam = Camera.main;

        // 表面：自發光電漿。Prefab 已經用 Sun.mat；程式產生的球才需要換材質
        if (surface == null) surface = GetComponent<Renderer>();
        if (surface.sharedMaterial == null || surface.sharedMaterial.shader.name != "StarrySky/SunSurface")
        {
            var surfaceShader = Shader.Find("StarrySky/SunSurface");
            if (surfaceShader != null) surface.material = new Material(surfaceShader);
            else Debug.LogError("[SunVisual] 找不到 StarrySky/SunSurface Shader");
        }
        surface.shadowCastingMode = ShadowCastingMode.Off;
        surface.receiveShadows = false;

        // 日冕：太陽的子球。球體 localScale 1 = 直徑，所以 localScale = coronaSize 時半徑剛好是 coronaSize 倍太陽半徑
        if (corona == null) corona = CreateCorona();
        if (corona != null) coronaMaterial = corona.material;   // 複製一份材質，Play 中調參數不會改到 SunCorona.mat

        SetupBloom();
    }

    static readonly int SunPosId = Shader.PropertyToID("_StarrySkySunPos");

    void LateUpdate()
    {
        // 讓行星 Shader（例如 StarrySky/Earth）知道太陽在哪，光從太陽照過去；w = 1 表示有效
        Vector3 p = transform.position;
        Shader.SetGlobalVector(SunPosId, new Vector4(p.x, p.y, p.z, 1f));

        // 每幀套用 Inspector 的數值，Play 中調整會立刻看到
        if (bloomVolume != null)
        {
            bloomVolume.enabled = addBloom;
            bloom.threshold.value = bloomThreshold;
            bloom.intensity.value = bloomIntensity;
            bloom.scatter.value = bloomScatter;
        }

        if (corona == null) return;
        corona.transform.localScale = Vector3.one * coronaSize;   // 球殼大小和 Shader 的 _CoronaSize 要一致
        coronaMaterial.SetFloat("_CoronaSize", coronaSize);
        coronaMaterial.SetFloat("_Intensity", coronaIntensity);
    }

    /// <summary>沒有 Prefab 時：建立子物件 Corona（球體 + StarrySky/SunCorona 材質）。</summary>
    Renderer CreateCorona()
    {
        var coronaShader = Shader.Find("StarrySky/SunCorona");
        if (coronaShader == null)
        {
            Debug.LogError("[SunVisual] 找不到 StarrySky/SunCorona Shader");
            return null;
        }
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Corona";
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        var r = go.GetComponent<Renderer>();
        r.material = new Material(coronaShader);
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    // 太陽消失後，行星 Shader 改回用材質上的預覽光線方向
    void OnDisable() => Shader.SetGlobalVector(SunPosId, Vector4.zero);

    /// <summary>建立全域 Volume 並加上 Bloom，讓 HDR 亮度超過 threshold 的部分發光。數值與開關由 LateUpdate 每幀套用。</summary>
    void SetupBloom()
    {
        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
        {
            Debug.LogWarning("[SunVisual] 目前沒有使用 URP Pipeline Asset，無法加 Bloom。" +
                             "請到 Project Settings > Graphics / Quality 指定 URP Asset，詳見 Docs/太陽視覺效果說明.md");
            return;
        }

        if (cam != null) cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        bloom = profile.Add<Bloom>(true);   // true：所有參數都勾選 Override

        bloomVolume = new GameObject("Sun Bloom Volume").AddComponent<Volume>();
        bloomVolume.isGlobal = true;
        bloomVolume.sharedProfile = profile;
    }
}
