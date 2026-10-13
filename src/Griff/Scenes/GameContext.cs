using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// What a component's own code may ask of the game running it, so the scene's data doesn't depend on the runtime. The
/// game passes itself to the components that need one.
/// </summary>
internal interface IGameContext
{
    /// <summary>The scene being played.</summary>
    Scene Scene { get; }

    /// <summary>Adds a new copy of a prefab at <paramref name="position"/> (in the world without a parent).</summary>
    Entity Spawn(string prefab, Vector2 position, Entity? parent = null);

    /// <summary>True on the frames a <see cref="BeatClock"/> (the one named, or the scene's) steps on every few beats.</summary>
    bool OnBeat(string? clock, int every, int offset);
}
