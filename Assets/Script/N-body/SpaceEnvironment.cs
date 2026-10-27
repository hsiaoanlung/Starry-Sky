using UnityEngine;

/// <summary>
/// 太空環境（只影響畫面）：星空天空盒（StarrySky/Starfield）或純色背景、環境光調暗，並關掉場景裡的 Directional Light。
/// 所有天體的光都由自訂 Shader 從太陽位置算（PlanetLighting.cginc），不需要 Directional Light。
/// 由 SolarSystemBuilder 掛在 Simulator 上；數值在 LateUpdate 每幀套用，Play 中可即時調整。停止 Play 後場景設定會還原。
/// </summary>
public class SpaceEnvironment : MonoBehaviour
{
    [Header("星空")]
    [Tooltip("顯示星空天空盒。關掉 = 純色背景")]
    public bool starfield = true;

    [Tooltip("星空材質（StarrySky/Starfield）。留空 = 讀 Resources/Materials/Starfield")]
    public Material starfieldMaterial;

    [Tooltip("星星與銀河的整體亮度")]
    [Range(0f, 3f)] public float starBrightness = 1f;

    [Tooltip("銀河亮度（0 = 關）")]
    [Range(0f, 3f)] public float milkyWay = 1f;

    [Header("背景與光")]
    [Tooltip("關掉星空時的相機背景顏色")]
    public Color backgroundColor = Color.black;

    [Tooltip("環境光顏色（只影響 Unity 內建的 Lit 材質；StarrySky 的 Shader 用各自材質的「背光面最低亮度」）")]
    public Color ambientColor = new Color(0.01f, 0.01f, 0.012f);

    [Tooltip("關掉場景裡的 Directional Light。光改由太陽的位置決定")]
    public bool disableDirectionalLights = true;

    Light[] directionalLights;
    Material skyInstance;   // 複製一份，Play 中調亮度不會改到材質檔

    void Start()
    {
        if (starfieldMaterial == null) starfieldMaterial = Resources.Load<Material>("Materials/Starfield");
        if (starfieldMaterial != null) skyInstance = new Material(starfieldMaterial);
        else Debug.LogWarning("[SpaceEnvironment] 找不到 Resources/Materials/Starfield，改用純色背景");

        // 環境光改成單一顏色（Flat），不從天空盒取樣：星空很暗，取樣出來也幾乎是黑的，而且每次換天空盒都要重算
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

        directionalLights = System.Array.FindAll(
            FindObjectsByType<Light>(), l => l.type == LightType.Directional);
    }

    void LateUpdate()
    {
        bool sky = starfield && skyInstance != null;
        if (sky)
        {
            skyInstance.SetFloat("_Exposure", starBrightness);
            skyInstance.SetFloat("_MilkyWay", milkyWay);
        }
        if (RenderSettings.skybox != (sky ? skyInstance : null)) RenderSettings.skybox = sky ? skyInstance : null;

        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = sky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;   // URP 的 Background Type
            cam.backgroundColor = backgroundColor;
        }

        RenderSettings.ambientLight = ambientColor;

        foreach (var l in directionalLights)
            if (l != null) l.enabled = !disableDirectionalLights;
    }
}
