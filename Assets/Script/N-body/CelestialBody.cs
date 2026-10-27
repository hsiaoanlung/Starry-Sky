using UnityEngine;

/// <summary>
/// 一顆天體的物理狀態。單位：質量 = 太陽質量，位置 = AU，速度 = AU/day。
/// 軌道面放在 Unity 的 XZ 平面（y 朝上）。
/// </summary>
public class CelestialBody : MonoBehaviour
{
    public string bodyName = "Body";

    [Tooltip("太陽質量為 1")]
    public double mass = 1.0;

    [Tooltip("AU")]
    public Vec3d position;

    [Tooltip("AU / day")]
    public Vec3d velocity;

    [Tooltip("繞著哪顆天體轉，只影響顯示；物理上所有天體互相吸引")]
    public CelestialBody parent;

    [Tooltip("顯示時把與 parent 的距離放大幾倍（月球用），不影響物理")]
    public float orbitScale = 1f;

    [Tooltip("CSV 的 color 欄，左側列表的文字顏色用；不影響物理")]
    public Color displayColor = Color.white;

    [HideInInspector] public Vec3d acceleration;   // 由 NBodySimulator 填入
}
