using UnityEngine;

/// <summary>
/// 天體自轉（只影響畫面，不影響物理）：繞自己的自轉軸（物件的 local y，也就是 Prefab 傾斜後的方向）旋轉。
/// 轉速跟著模擬時間走，但模擬播放很快時，自轉用的時間有上限（SolarSystemBuilder.maxSpinDaysPerSecond），
/// 所有天體一起變慢，彼此的快慢比例不變。由 SolarSystemBuilder 依 CSV 的 rotationPeriod 欄掛上。
/// </summary>
public class BodySpin : MonoBehaviour
{
    [Tooltip("恆星自轉週期（模擬天）。正 = 從自轉軸北極（local y）往下看逆時針，和公轉同向；負 = 反向；0 = 不轉")]
    public double rotationPeriodDays = 1.0;

    [System.NonSerialized] public NBodySimulator simulator;
    [System.NonSerialized] public SolarSystemBuilder builder;

    double lastSimulatedDays;

    void Start()
    {
        if (simulator != null) lastSimulatedDays = simulator.SimulatedDays;
    }

    // LateUpdate：NBodySimulator 在 Update 推進完這一幀的模擬時間之後才轉
    void LateUpdate()
    {
        if (simulator == null) return;

        // 這一幀實際推進了幾個模擬天（暫停時為 0；算力不足時比 daysPerSecond 少）
        double days = simulator.SimulatedDays - lastSimulatedDays;
        lastSimulatedDays = simulator.SimulatedDays;

        if (builder != null)
        {
            if (!builder.addSpin) return;
            // 上限：每真實秒最多轉 maxSpinDaysPerSecond 天份，避免轉太快看起來亂閃
            if (builder.maxSpinDaysPerSecond > 0)
                days = System.Math.Min(days, builder.maxSpinDaysPerSecond * Time.deltaTime);
        }
        if (rotationPeriodDays == 0 || days <= 0) return;

        // Unity 是左手座標：繞 +y 轉正角度，從上方看是順時針，所以逆時針（和公轉同向）要用負號
        float degrees = (float)(-360.0 * days / rotationPeriodDays);
        transform.Rotate(Vector3.up, degrees, Space.Self);
    }
}
