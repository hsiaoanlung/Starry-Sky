using System;

public struct Vector2d
{
    public double X;
    public double Y;

    public Vector2d(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double Magnitude
    {
        get
        {
            return Math.Sqrt(X * X + Y * Y);
        }
    }

    public static Vector2d operator +(
        Vector2d a, Vector2d b)
    {
        return new Vector2d(
            a.X + b.X,
            a.Y + b.Y);
    }

    public static Vector2d operator -(
        Vector2d a, Vector2d b)
    {
        return new Vector2d(
            a.X - b.X,
            a.Y - b.Y);
    }

    public static Vector2d operator *(
        Vector2d v, double scalar)
    {
        return new Vector2d(
            v.X * scalar,
            v.Y * scalar);
    }

    public override string ToString()
    {
        return $"({X:F3}, {Y:F3})";
    }
}

