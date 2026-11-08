// Portable stand-ins for the few WPF base types that the shared view models use.
// The GTK frontend compiles the WPF app's view models unchanged, so these keep the
// original namespaces and member names; only the members those sources touch exist.

namespace System.Windows;

/// <summary>Mirror of WPF's Visibility. The view models expose it for XAML bindings;
/// GTK views read the boolean the value encodes.</summary>
public enum Visibility
{
    Visible = 0,
    Hidden = 1,
    Collapsed = 2,
}

/// <summary>Device-independent point used by the calibration view models.</summary>
public struct Point : IEquatable<Point>
{
    public Point(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; set; }

    public double Y { get; set; }

    public readonly bool Equals(Point other) => X.Equals(other.X) && Y.Equals(other.Y);

    public override readonly bool Equals(object? obj) => obj is Point other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(Point left, Point right) => left.Equals(right);

    public static bool operator !=(Point left, Point right) => !left.Equals(right);

    public override readonly string ToString() =>
        string.Create(Globalization.CultureInfo.InvariantCulture, $"{X},{Y}");
}

/// <summary>Integer pixel rectangle for <c>BitmapSource.CopyPixels</c>.</summary>
public readonly record struct Int32Rect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width == 0 || Height == 0;
}
