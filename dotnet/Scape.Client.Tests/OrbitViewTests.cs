using Orblit.VectorMath;

namespace Scape.Client.Tests;

public class OrbitViewTests
{
    private static readonly Vector3 Target = new(20, 3, 30);

    [Fact]
    public void TheRayThroughTheMiddleOfTheViewReachesTheTarget()
    {
        var view = new OrbitView { Target = Target, Yaw = 0.7, Pitch = 0.6, Distance = 10 };

        var ray = view.RayThrough(400, 300, 800, 600);
        var reached = ray.Origin + (ray.Direction * view.Distance);

        Assert.True((reached - Target).Length < 1e-9);
    }

    [Fact]
    public void APixelToTheRightLooksToTheRightOfTheTarget()
    {
        var view = new OrbitView { Target = Target, Yaw = 1.1 };
        var right = view.Forward.Cross(new Vector3(0, 1, 0));

        var left = view.RayThrough(100, 300, 800, 600).Direction;
        var far = view.RayThrough(700, 300, 800, 600).Direction;

        Assert.True(far.Dot(right) > 0);
        Assert.True(left.Dot(right) < 0);
    }

    [Fact]
    public void APixelHigherInTheViewLooksHigher()
    {
        var view = new OrbitView { Target = Target };
        var top = view.RayThrough(400, 0, 800, 600).Direction;
        var bottom = view.RayThrough(400, 600, 800, 600).Direction;
        Assert.True(top.Y > bottom.Y);
    }

    [Fact]
    public void TurningZoomingAndFollowingMakeANewView()
    {
        var view = new OrbitView { Target = Target, Yaw = 1, Pitch = 0.8, Distance = 10 };

        var turned = view.Turned(0.5, 0.1);
        Assert.Equal(1.5, turned.Yaw, 1e-12);
        Assert.Equal(0.9, turned.Pitch, 1e-12);
        Assert.Equal(1, view.Yaw);

        Assert.Equal(5, view.Zoomed(0.5).Distance, 1e-12);
        var moved = view.Following(new Vector3(1, 2, 3));
        Assert.Equal(new Vector3(1, 2, 3), moved.Target);
        Assert.Equal(1, moved.Yaw);
        Assert.Equal(Target, view.Target);
    }

    [Fact]
    public void PitchAndDistanceStopAtTheirLimits()
    {
        var view = new OrbitView { Target = Target };
        Assert.Equal(OrbitView.MaxPitch, view.Turned(0, 10).Pitch);
        Assert.Equal(OrbitView.MinPitch, view.Turned(0, -10).Pitch);
        Assert.Equal(OrbitView.MaxDistance, view.Zoomed(100).Distance);
        Assert.Equal(OrbitView.MinDistance, view.Zoomed(0.001).Distance);
    }

    [Fact]
    public void AViewNeedsAFieldOfViewAndAFinitePlace()
    {
        Assert.ThrowsAny<ArgumentException>(() => new OrbitView { Target = Target, FovY = 0 });
        Assert.ThrowsAny<ArgumentException>(() => new OrbitView { Target = Target, FovY = Math.PI });
        Assert.ThrowsAny<ArgumentException>(() => new OrbitView { Target = new Vector3(double.NaN, 0, 0) });
        Assert.ThrowsAny<ArgumentException>(() => new OrbitView { Target = Target, Yaw = double.PositiveInfinity });
    }

    [Fact]
    public void EveryPartOfTheTargetMustBeFinite()
    {
        Assert.ThrowsAny<ArgumentException>(() => new OrbitView { Target = new Vector3(0, double.NaN, 0) });
        Assert.ThrowsAny<ArgumentException>(() => new OrbitView { Target = new Vector3(0, 0, double.PositiveInfinity) });
    }
}
