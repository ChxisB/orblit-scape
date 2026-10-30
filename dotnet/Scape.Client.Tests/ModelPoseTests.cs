using Orblit.VectorMath;
using Scape.Content;

namespace Scape.Client.Tests;

public class ModelPoseTests
{
    // One box per bone, hung from the pivots a person has.
    private static readonly ActorModel Person = new(
        "test.v1",
        [
            new ModelBox(ModelBone.Torso, new Vec3(0, 1.0, 0), new Vec3(0.5, 0.7, 0.3), 0),
            new ModelBox(ModelBone.Head, new Vec3(0, 1.52, 0), new Vec3(0.3, 0.3, 0.3), 0),
            new ModelBox(ModelBone.ArmL, new Vec3(-0.32, 1.0, 0), new Vec3(0.15, 0.7, 0.15), 0),
            new ModelBox(ModelBone.ArmR, new Vec3(0.32, 1.0, 0), new Vec3(0.15, 0.7, 0.15), 0),
            new ModelBox(ModelBone.LegL, new Vec3(-0.14, 0.35, 0), new Vec3(0.2, 0.7, 0.22), 0),
            new ModelBox(ModelBone.LegR, new Vec3(0.14, 0.35, 0), new Vec3(0.2, 0.7, 0.22), 0),
        ],
        new Dictionary<ModelBone, Vec3>
        {
            [ModelBone.Torso] = new(0, 0.7, 0),
            [ModelBone.Head] = new(0, 1.35, 0),
            [ModelBone.ArmL] = new(-0.32, 1.35, 0),
            [ModelBone.ArmR] = new(0.32, 1.35, 0),
            [ModelBone.LegL] = new(-0.14, 0.7, 0),
            [ModelBone.LegR] = new(0.14, 0.7, 0),
        });

    [Fact]
    public void AStillPoseLeavesEveryBoxWhereTheModelPutsIt()
    {
        var placed = ModelPose.Place(Person, new Pose());

        Assert.Equal(Person.Boxes.Count, placed.Count);
        for (var i = 0; i < placed.Count; i++)
        {
            var box = Person.Boxes[i];
            Near(box.At, placed[i].GetTranslation());
            var corner = new Vec3(box.At.X + (box.Size.X / 2), box.At.Y + (box.Size.Y / 2), box.At.Z + (box.Size.Z / 2));
            Near(corner, placed[i].Transformed3(new Vector3(1, 1, 1)));
        }
    }

    [Theory]
    [InlineData(ModelBone.LegL)]
    [InlineData(ModelBone.LegR)]
    [InlineData(ModelBone.ArmL)]
    [InlineData(ModelBone.ArmR)]
    public void ALimbSwungAQuarterTurnPointsForwardFromItsPivotAndNothingElseMoves(ModelBone bone)
    {
        var placed = ModelPose.Place(Person, Swung(bone, Math.PI / 2));

        for (var i = 0; i < placed.Count; i++)
        {
            var box = Person.Boxes[i];
            var pivot = Person.Pivots[box.Bone];
            var expected = box.Bone == bone ? new Vec3(box.At.X, pivot.Y, pivot.Y - box.At.Y) : box.At;
            Near(expected, placed[i].GetTranslation());
        }
    }

    [Fact]
    public void LeaningForwardTipsTheUpperBodyAndLeavesTheLegs()
    {
        var placed = Centres(new Pose { Lean = 0.3 });

        Assert.True(placed[ModelBone.Head].Z > 0.2);
        Assert.True(placed[ModelBone.Torso].Z > 0);
        Assert.True(placed[ModelBone.ArmL].Z > 0);
        Assert.True(placed[ModelBone.ArmR].Z > 0);
        Near(new Vec3(-0.14, 0.35, 0), placed[ModelBone.LegL]);
        Near(new Vec3(0.14, 0.35, 0), placed[ModelBone.LegR]);
    }

    [Fact]
    public void ARaisedArmOnABodyBentFlatPointsAtTheGround()
    {
        var placed = Centres(new Pose { ArmR = Math.PI / 2, Lean = Math.PI / 2 });

        Near(new Vec3(0.32, 0.35, 0.65), placed[ModelBone.ArmR]);
    }

    [Fact]
    public void AnActorFallenFlatHasItsHeadBehindItsFeet()
    {
        var placed = Centres(new Pose { Lie = 1 });

        Near(new Vec3(0, 0, -1.52), placed[ModelBone.Head]);
        Near(new Vec3(-0.14, 0, -0.35), placed[ModelBone.LegL]);
    }

    private static Pose Swung(ModelBone bone, double angle) => bone switch
    {
        ModelBone.LegL => new Pose { LegL = angle },
        ModelBone.LegR => new Pose { LegR = angle },
        ModelBone.ArmL => new Pose { ArmL = angle },
        _ => new Pose { ArmR = angle },
    };

    private static Dictionary<ModelBone, Vector3> Centres(Pose pose)
    {
        var placed = ModelPose.Place(Person, pose);
        return Person.Boxes.Select((box, i) => (box.Bone, placed[i].GetTranslation())).ToDictionary();
    }

    private static void Near(Vec3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 1e-9);
        Assert.Equal(expected.Y, actual.Y, 1e-9);
        Assert.Equal(expected.Z, actual.Z, 1e-9);
    }
}
