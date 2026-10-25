using UnityEngine;

/// <summary>
/// double 精度三維向量。物理計算全部用它，只在顯示時轉成 Vector3。
/// </summary>
[System.Serializable]
public struct Vec3d
{
    public double x, y, z;

    public Vec3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

    public static readonly Vec3d zero = new Vec3d(0, 0, 0);

    public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vec3d operator -(Vec3d a) => new Vec3d(-a.x, -a.y, -a.z);
    public static Vec3d operator *(Vec3d a, double s) => new Vec3d(a.x * s, a.y * s, a.z * s);
    public static Vec3d operator *(double s, Vec3d a) => new Vec3d(a.x * s, a.y * s, a.z * s);
    public static Vec3d operator /(Vec3d a, double s) => new Vec3d(a.x / s, a.y / s, a.z / s);

    public double SqrMagnitude => x * x + y * y + z * z;
    public double Magnitude => System.Math.Sqrt(SqrMagnitude);

    public static double Dot(Vec3d a, Vec3d b) => a.x * b.x + a.y * b.y + a.z * b.z;

    public static Vec3d Cross(Vec3d a, Vec3d b) => new Vec3d(
        a.y * b.z - a.z * b.y,
        a.z * b.x - a.x * b.z,
        a.x * b.y - a.y * b.x);

    /// <summary>轉成 Unity 顯示座標，scale = 1 AU 對應幾個 Unity 單位。</summary>
    public Vector3 ToVector3(float scale = 1f) =>
        new Vector3((float)(x * scale), (float)(y * scale), (float)(z * scale));

    public override string ToString() => $"({x:G6}, {y:G6}, {z:G6})";
}
