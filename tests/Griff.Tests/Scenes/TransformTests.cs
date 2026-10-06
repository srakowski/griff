using System.Numerics;
using Griff.Scenes;

namespace Griff.Tests.Scenes;

public class TransformTests
{
    private const float Tolerance = 1e-4f;

    [Fact]
    public void WorldPosition_ComposesParentRotationAndTranslation()
    {
        var scene = new Scene();
        var parent = new Entity("Parent");
        var child = new Entity("Child");
        scene.Add(parent);
        scene.Add(child, parent);

        parent.Transform.Position = new Vector3(10, 0, 0);
        parent.Transform.Rotation = new Vector3(0, 0, 90);
        child.Transform.Position = new Vector3(1, 0, 0);

        AssertNear(new Vector3(10, 1, 0), child.Transform.WorldPosition);
    }

    [Fact]
    public void WorldPosition_AppliesParentScale()
    {
        var scene = new Scene();
        var parent = new Entity("Parent");
        var child = new Entity("Child");
        scene.Add(parent);
        scene.Add(child, parent);

        parent.Transform.Scale = new Vector3(2, 2, 1);
        child.Transform.Position = new Vector3(3, 4, 0);

        AssertNear(new Vector3(6, 8, 0), child.Transform.WorldPosition);
    }

    [Theory]
    [InlineData(0, 0, 90)]
    [InlineData(30, 45, 60)]
    [InlineData(-20, 170, -35)]
    public void RotationQuaternion_RoundTripsEulerAngles(float x, float y, float z)
    {
        var transform = new Transform { Rotation = new Vector3(x, y, z) };
        var quaternion = transform.RotationQuaternion;

        transform.RotationQuaternion = quaternion;

        AssertNear(new Vector3(x, y, z), transform.Rotation);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"Expected {expected}, got {actual}");
}
