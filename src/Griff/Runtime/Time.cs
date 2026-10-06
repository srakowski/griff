namespace Griff.Runtime;

/// <summary>Frame timing. <see cref="Delta"/> is scaled; multiply speeds by it for frame-rate independence.</summary>
public sealed class Time
{
    /// <summary>Longest step a single frame may take, so a stall (breakpoint, window drag) doesn't teleport things.</summary>
    public const float MaxDelta = 0.1f;

    /// <summary>Seconds since the previous frame, after <see cref="Scale"/>.</summary>
    public float Delta { get; private set; }

    /// <summary>Seconds since the previous frame, ignoring <see cref="Scale"/>.</summary>
    public float UnscaledDelta { get; private set; }

    /// <summary>Scaled seconds since the game started.</summary>
    public double Total { get; private set; }

    public long Frame { get; private set; }

    /// <summary>1 is normal speed, 0.5 is slow motion, 0 pauses gameplay.</summary>
    public float Scale { get; set; } = 1f;

    internal void Advance(double seconds)
    {
        UnscaledDelta = Math.Clamp((float)seconds, 0f, MaxDelta);
        Delta = UnscaledDelta * Scale;
        Total += Delta;
        Frame++;
    }
}
