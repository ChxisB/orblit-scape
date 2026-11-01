using Orblit.VectorMath;

namespace Scape.Client;

/// <summary>A ray from the camera through one pixel.</summary>
public readonly record struct PickRay(Vector3 Origin, Vector3 Direction);

/// <summary>
/// A camera circling a point, looking at it. Input makes a new view, so the one on screen never
/// changes under a pick that is half done.
/// </summary>
public sealed record OrbitView
{
    public const double MinPitch = 0.15;
    public const double MaxPitch = 1.45;
    public const double MinDistance = 4;
    public const double MaxDistance = 40;

    /// <summary>The point looked at. Never changed after the view is made.</summary>
    public required Vector3 Target
    {
        get;
        init
        {
            if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z))
            {
                throw new ArgumentException("A view needs a finite target.", nameof(Target));
            }

            field = value;
        }
    }

    /// <summary>Turn about the vertical axis, in radians.</summary>
    public double Yaw
    {
        get;
        init
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentException("A view needs a finite yaw.", nameof(Yaw));
            }

            field = value;
        }
    }

    /// <summary>Angle above the ground, in radians.</summary>
    public double Pitch { get; init => field = Math.Clamp(value, MinPitch, MaxPitch); } = 0.9;

    public double Distance { get; init => field = Math.Clamp(value, MinDistance, MaxDistance); } = 14;

    /// <summary>Vertical field of view, in radians.</summary>
    public double FovY
    {
        get;
        init
        {
            if (!double.IsFinite(value) || value <= 0 || value >= Math.PI)
            {
                throw new ArgumentOutOfRangeException(nameof(FovY), value, "It is not between 0 and pi.");
            }

            field = value;
        }
    } = 0.8;

    public Vector3 Position =>
        Target + (new Vector3(Math.Sin(Yaw) * Math.Cos(Pitch), Math.Sin(Pitch), Math.Cos(Yaw) * Math.Cos(Pitch)) * Distance);

    /// <summary>The unit direction the camera looks along.</summary>
    public Vector3 Forward => (Target - Position).Normalized();

    public OrbitView Turned(double byYaw, double byPitch) => this with { Yaw = Yaw + byYaw, Pitch = Pitch + byPitch };

    /// <summary>Moves in by <paramref name="factor"/> below one and out above it.</summary>
    public OrbitView Zoomed(double factor) => this with { Distance = Distance * factor };

    public OrbitView Following(Vector3 point) => this with { Target = point };

    /// <summary>
    /// The ray through the pixel (<paramref name="x"/>, <paramref name="y"/>) of a viewport
    /// <paramref name="width"/> by <paramref name="height"/>, measured from its top left.
    /// </summary>
    public PickRay RayThrough(double x, double y, double width, double height)
    {
        var look = Forward;
        var right = look.Cross(new Vector3(0, 1, 0)).Normalized();
        var up = right.Cross(look);
        var reach = Math.Tan(FovY / 2);
        var across = ((2 * x / width) - 1) * reach * (width / height);
        var down = (1 - (2 * y / height)) * reach;
        return new PickRay(Position, (look + (right * across) + (up * down)).Normalized());
    }
}
