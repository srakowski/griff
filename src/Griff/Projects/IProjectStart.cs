using Griff.Data;
using Griff.Scenes;

namespace Griff.Projects;

/// <summary>
/// What a new project starts with (<see cref="Project.CreateAsync"/>): its start scene and the files beside it, each keyed
/// by its path relative to Assets. New Project's templates are these (Griff.Templates); without one, a project starts with
/// a scene with a camera.
/// </summary>
public interface IProjectStart
{
    /// <summary>The start scene.</summary>
    Scene StartScene();

    /// <summary>The scenes besides the start scene ("Scenes/Game.scene").</summary>
    IReadOnlyDictionary<string, Scene> MoreScenes();

    /// <summary>Its prefabs ("Prefabs/Bullet.prefab").</summary>
    IReadOnlyDictionary<string, Entity> Prefabs();

    /// <summary>Script files ("Scripts/Ship.cs").</summary>
    IReadOnlyDictionary<string, string> Scripts();

    /// <summary>Image files, as PNG bytes.</summary>
    IReadOnlyDictionary<string, byte[]> Images();

    /// <summary>Sound and music files, as WAV bytes.</summary>
    IReadOnlyDictionary<string, byte[]> Sounds();

    /// <summary>Its tables of game data ("Tables/Enemies.table").</summary>
    IReadOnlyDictionary<string, Table> Tables();

    /// <summary>Its balance simulations ("Simulations/Balance.sim").</summary>
    IReadOnlyDictionary<string, Simulation> Simulations();
}
