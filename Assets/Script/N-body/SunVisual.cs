using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 太陽的外觀：表面換成流動電漿 Shader、加上面向相機的日冕 Quad，並在 URP 下自動開啟 Bloom。
/// 由 SolarSystemBuilder 掛在沒有 parent 的天體（太陽）上，只影響畫面，不影響物理。
/// </summary>
public class SunVisual : MonoBehaviour
{
    [Header("日冕")]
    [Tooltip("日冕 Quad 的半徑，以太陽半徑為單位。4 = 日冕延伸到 4 倍太陽半徑")]
    public float coronaSize = 4f;
    [Tooltip("日冕整體亮度")]
    public float coronaIntensity = 0.8f;

    [Header("Bloom（只在 URP 下有效）")]
    public bool addBloom = true;
    public float bloomThreshold = 1f;
    public float bloomIntensity = 1f;
    [Range(0f, 1f)] public float bloomScatter = 0.6f;

    Transform corona;
    Material coronaMaterial;
    Camera cam;
    Volume bloomVolume;   // URP 下才會建立
    Bloom bloom;

    void Start()
    {
        cam = Camera.main;

        var surfaceShader = Shader.Find("StarrySky/SunSurface");
        var coronaShader = Shader.Find("StarrySky/SunCorona");
        if (surfaceShader == null || coronaShader == null)
        {
            Debug.LogError("[SunVisual] 找不到 StarrySky/SunSurface 或 StarrySky/SunCorona Shader");
            return;
        }

        // 表面：自發光電漿，不投射也不接收陰影
        var sunRenderer = GetComponent<Renderer>();
        sunRenderer.material = new Material(surfaceShader);
        sunRenderer.shadowCastingMode = ShadowCastingMode.Off;
        sunRenderer.receiveShadows = false;

        // 日冕：太陽的子物件。球體 localScale 1 = 直徑，Quad 邊長 1，所以 localScale = coronaSize 時半徑剛好是 coronaSize 倍太陽半徑
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Corona";
        Destroy(quad.GetComponent<Collider>());
        corona = quad.transform;
        corona.SetParent(transform, false);
        corona.localScale = Vector3.one * coronaSize;
        coronaMaterial = new Material(coronaShader);
        var quadRenderer = quad.GetComponent<Renderer>();
        quadRenderer.material = coronaMaterial;
        quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
        quadRenderer.receiveShadows = false;

        SetupBloom();
    }

    static readonly int SunPosId = Shader.PropertyToID("_StarrySkySunPos");

    void LateUpdate()
    {
        // 讓行星 Shader（例如 StarrySky/Earth）知道太陽在哪，光從太陽照過去；w = 1 表示有效
        Vector3 p = transform.position;
        Shader.SetGlobalVector(SunPosId, new Vector4(p.x, p.y, p.z, 1f));

        if (corona == null || cam == null) return;
        corona.rotation = cam.transform.rotation;   // Billboard：永遠正對相機
        corona.localScale = Vector3.one * coronaSize;
        coronaMaterial.SetFloat("_CoronaSize", coronaSize);
        coronaMaterial.SetFloat("_Intensity", coronaIntensity);

        // 每幀套用 Inspector 的數值，Play 中調整會立刻看到
        if (bloomVolume != null)
        {
            bloomVolume.enabled = addBloom;
            bloom.threshold.value = bloomThreshold;
            bloom.intensity.value = bloomIntensity;
            bloom.scatter.value = bloomScatter;
        }
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
