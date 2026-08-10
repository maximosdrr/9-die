using System;

namespace Pool.Simulation;

/// <summary>
/// Double-precision 3D vector. The simulation deliberately avoids Godot's Vector3 (which is
/// single-precision) because ball speeds span four orders of magnitude within a single shot —
/// a break leaves the tip at ~8 m/s and the same ball has to come to rest cleanly at ~1e-3 m/s.
/// In float, the sliding/rolling transition test loses too many significant digits near the
/// bottom of that range to be stable, and any error there shows up as a ball that creeps
/// forever instead of stopping. Conversion to Godot types happens only at the presentation
/// boundary.
/// </summary>
public readonly struct Vec3d : IEquatable<Vec3d>
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public static readonly Vec3d Zero = new(0, 0, 0);
    public static readonly Vec3d Up = new(0, 1, 0);

    public Vec3d(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(LengthSquared);

    /// <summary>The horizontal (table-plane) part only. Most billiard math is 2D on the cloth.</summary>
    public Vec3d Flat => new(X, 0.0, Z);

    public double FlatLengthSquared => X * X + Z * Z;
    public double FlatLength => Math.Sqrt(FlatLengthSquared);

    /// <summary>Returns Zero rather than NaN for a zero-length vector; callers rely on this.</summary>
    public Vec3d Normalized()
    {
        var lengthSquared = LengthSquared;
        if (lengthSquared <= 0.0)
            return Zero;

        var inverse = 1.0 / Math.Sqrt(lengthSquared);
        return new Vec3d(X * inverse, Y * inverse, Z * inverse);
    }

    public Vec3d Cross(Vec3d other) => new(
        Y * other.Z - Z * other.Y,
        Z * other.X - X * other.Z,
        X * other.Y - Y * other.X);

    public double Dot(Vec3d other) => X * other.X + Y * other.Y + Z * other.Z;

    public Vec3d WithY(double y) => new(X, y, Z);

    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3d operator -(Vec3d v) => new(-v.X, -v.Y, -v.Z);
    public static Vec3d operator *(Vec3d v, double s) => new(v.X * s, v.Y * s, v.Z * s);
    public static Vec3d operator *(double s, Vec3d v) => v * s;
    public static Vec3d operator /(Vec3d v, double s) => new(v.X / s, v.Y / s, v.Z / s);

    public bool Equals(Vec3d other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    public override bool Equals(object obj) => obj is Vec3d other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public override string ToString() => $"({X:F5}, {Y:F5}, {Z:F5})";
}
