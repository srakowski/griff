using System.Numerics;
using Griff.Scenes;

namespace Griff.Tests.Scenes;

public enum TestMode
{
    Idle,
    Patrol,
    Chase,
}

/// <summary>Covers every supported property type.</summary>
public class TestComponent : Component
{
    public bool Flag { get; set; }
    public int Count { get; set; }
    public float Speed { get; set; }
    public double Precise { get; set; }
    public string? Label { get; set; }
    public TestMode Mode { get; set; }
    public Vector2 Offset { get; set; }
    public Vector3 Direction { get; set; }
    public Vector4 Tint { get; set; }
    public Quaternion Orientation { get; set; } = Quaternion.Identity;

    [DoNotSerialize]
    public int Ignored { get; set; }

    public int ReadOnly => Count * 2;
}

[ComponentName("Health")]
public class RenamedComponent : Component
{
    public int Value { get; set; } = 100;
}

public class UnsupportedComponent : Component
{
    public List<int> Items { get; set; } = [];
}
