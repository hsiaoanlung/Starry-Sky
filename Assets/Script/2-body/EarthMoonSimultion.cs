using UnityEngine;

public class EarthMoonSimulation : MonoBehaviour
{
    public Transform earth;

    // 模擬時間：每 1 秒現實時間 = 1 模擬天
    public float simulationDaysPerSecond = 1f;

    // 月球約 27.32 天繞行一圈
    public float orbitalPeriodDays = 27.321661f;

    private Vector3 velocity;
    private float mu;

    void Start()
    {
        if (earth == null)
        {
            Debug.LogError("請在 Inspector 指定 Earth！");
            enabled = false;
            return;
        }

        Vector3 offset = transform.position - earth.position;
        float radius = offset.magnitude;

        // 由軌道半徑與週期計算重力參數
        mu = 4f * Mathf.PI * Mathf.PI
             * radius * radius * radius
             / (orbitalPeriodDays * orbitalPeriodDays);

        // 初始速度與地月連線垂直
        Vector3 tangent = Vector3.Cross(
            Vector3.up, offset.normalized
        ).normalized;

        velocity = tangent * Mathf.Sqrt(mu / radius);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime
                   * simulationDaysPerSecond;

        Vector3 offset = transform.position - earth.position;

        // 第一步：計算目前的重力加速度
        Vector3 a0 = -mu * offset
                     / Mathf.Pow(offset.magnitude, 3);

        // 第二步：更新位置
        Vector3 nextPosition =
            transform.position
            + velocity * dt
            + 0.5f * a0 * dt * dt;

        // 第三步：在新位置重新計算重力
        Vector3 nextOffset = nextPosition - earth.position;

        Vector3 a1 = -mu * nextOffset
                     / Mathf.Pow(nextOffset.magnitude, 3);

        // 第四步：更新速度
        velocity += 0.5f * (a0 + a1) * dt;

        transform.position = nextPosition;
    }
}
