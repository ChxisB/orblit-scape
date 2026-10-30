using Orblit.VectorMath;
using Scape.Content;

namespace Scape.Client;

/// <summary>
/// Where each box of a model sits in a pose, in the model's own space: feet at
/// the origin, facing +z.
/// </summary>
public static class ModelPose
{
    /// <summary>
    /// One transform per box of <paramref name="model"/>, in order. Each maps
    /// the cube from -1 to 1 onto its box.
    /// </summary>
    public static IReadOnlyList<Matrix4> Place(ActorModel model, Pose pose)
    {
        // The actor tips onto its back about its feet, so the head ends behind them.
        var lie = Matrix4.RotationX(-pose.Lie * Math.PI / 2);
        return [.. model.Boxes.Select(box => lie.Multiplied(Bend(model, box.Bone, pose)).Multiplied(Sized(box)))];
    }

    private static Matrix4 Bend(ActorModel model, ModelBone bone, Pose pose) => bone switch
    {
        ModelBone.LegL => Swing(model, bone, pose.LegL),
        ModelBone.LegR => Swing(model, bone, pose.LegR),
        ModelBone.ArmL => Lean(model, pose).Multiplied(Swing(model, bone, pose.ArmL)),
        ModelBone.ArmR => Lean(model, pose).Multiplied(Swing(model, bone, pose.ArmR)),
        ModelBone.Torso or ModelBone.Head => Lean(model, pose),
        _ => throw new InvalidOperationException($"{bone} is not a bone."),
    };

    // A positive swing turns the bottom of a hanging limb forward, which is a negative turn about x.
    private static Matrix4 Swing(ActorModel model, ModelBone bone, double angle) =>
        About(model.Pivots[bone], Matrix4.RotationX(-angle));

    private static Matrix4 Lean(ActorModel model, Pose pose) =>
        About(model.Pivots[ModelBone.Torso], Matrix4.RotationX(pose.Lean));

    private static Matrix4 About(Vec3 pivot, Matrix4 turn) =>
        Matrix4.TranslationValues(pivot.X, pivot.Y, pivot.Z)
            .Multiplied(turn)
            .Multiplied(Matrix4.TranslationValues(-pivot.X, -pivot.Y, -pivot.Z));

    // The cube is 2 units across, so a box's scale is half its size.
    private static Matrix4 Sized(ModelBox box)
    {
        var placed = Matrix4.TranslationValues(box.At.X, box.At.Y, box.At.Z);
        placed.ScaleByDouble(box.Size.X / 2, box.Size.Y / 2, box.Size.Z / 2, 1);
        return placed;
    }
}
