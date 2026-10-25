using UnityEngine;

public class SolarSystem : MonoBehaviour
{
    // 萬有引力常數，單位 m^3 / (kg * s^2)
    const double G = 6.67430e-11;

    // 太陽質量，單位 kg
    const double SunMass = 1.989e30;

    // 天文單位：地球與太陽的平均距離，單位 m
    const double AU = 1.496e11;

    // 模擬中的時間步長
    const double TimeStep = 3600.0; // 每次模擬前進一小時

    // 畫面縮放：1 AU 對應 5 個 Unity units
    const float DisplayScale = 5f;

    Vector2d sunPosition;
    Vector2d earthPosition;
    Vector2d earthVelocity;

    GameObject earthObject;

    void Start()
    {
        sunPosition = new Vector2d(0, 0);

        // 地球初始位置：距離太陽 1 AU
        earthPosition = new Vector2d(AU, 0);

        // 初始速度方向與太陽—地球連線垂直
        earthVelocity = new Vector2d(0, 29780);

        // 建立太陽
        GameObject sun = GameObject.CreatePrimitive(
            PrimitiveType.Sphere
        );
        sun.name = "Sun";
        sun.transform.position = Vector3.zero;
        sun.transform.localScale = Vector3.one * 1.093f;

        // 建立地球
        earthObject = GameObject.CreatePrimitive(
            PrimitiveType.Sphere
        );
        earthObject.name = "Earth";
        earthObject.transform.localScale = Vector3.one * 0.01f;

        UpdateEarthDisplay();

        Debug.Log("太陽系模擬開始");
    }

    void Update()
    {
        // 1. 計算從地球指向太陽的向量
        Vector2d direction = sunPosition - earthPosition;

        // 2. 計算太陽與地球的距離
        double distance = direction.Magnitude;

        // 3. 計算重力造成的加速度
        Vector2d acceleration = direction *
            (G * SunMass /
            (distance * distance * distance));

        // 4. 更新速度，再更新位置
        // 這是半隱式 Euler 積分
        earthVelocity = earthVelocity +
            acceleration * TimeStep;

        earthPosition = earthPosition +
            earthVelocity * TimeStep;

        // 5. 更新 Unity 畫面
        UpdateEarthDisplay();
    }

    void UpdateEarthDisplay()
    {
        float x = (float)(earthPosition.X / AU * DisplayScale);
        float y = (float)(earthPosition.Y / AU * DisplayScale);

        earthObject.transform.position = new Vector3(x, 0, y);
    }
}
