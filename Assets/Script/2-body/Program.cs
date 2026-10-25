using System;

class Program
{
    const double G = 6.67430e-11;
    const double SunMass = 1.989e30;

    static void Main()
    {
        Vector2d sunPosition =
            new Vector2d(0, 0);

        Vector2d earthPosition =
            new Vector2d(1.496e11, 0);

        Vector2d earthVelocity =
            new Vector2d(0, 29780);

        Vector2d direction =
            sunPosition - earthPosition;

        double distance = direction.Magnitude;

        Vector2d acceleration =
            direction * (
                G * SunMass /
                (distance * distance * distance));

        Console.WriteLine(
            $"Distance: {distance:E3} m");

        Console.WriteLine(
            $"Acceleration: {acceleration}");

        Console.WriteLine(
            $"Velocity: {earthVelocity}");
    }
}
