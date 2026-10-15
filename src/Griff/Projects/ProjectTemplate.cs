using Griff.Audio;
using Griff.Scenes;

namespace Griff.Projects;

/// <summary>
/// A starting point New Project offers: a playable start scene and whatever else it comes with (more scenes, scripts,
/// prefabs, pictures, sounds and music). Templates are found rather than listed (see <see cref="ProjectTemplates.All"/>),
/// so adding one is adding a class.
/// </summary>
public abstract class ProjectTemplate
{
    /// <summary>What code and demo scripts call it, e.g. "Match3". Defaults to the class name without "Template".</summary>
    public virtual string Name => GetType().Name is var name && name.EndsWith("Template", StringComparison.Ordinal) && name.Length > "Template".Length
        ? name[..^"Template".Length]
        : name;

    /// <summary>What New Project shows, e.g. "Match-3". Defaults to <see cref="Name"/>.</summary>
    public virtual string Title => Name;

    /// <summary>What it is and how to play it, shown under the title in New Project.</summary>
    public abstract string Description { get; }

    /// <summary>Where New Project lists it: lower first. Ties go by name.</summary>
    public abstract int Order { get; }

    /// <summary>False for a template that comes without the sound effects and music (Empty, a benchmark).</summary>
    protected virtual bool HasSounds => true;

    /// <summary>The music it comes with, if <see cref="HasSounds"/>.</summary>
    protected virtual MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Bright, seed: 3) with { Volume = 0.7f };

    /// <summary>
    /// Fills in the sound of everything in <paramref name="entity"/> that makes one and has none yet, from the sounds every
    /// template with sounds comes with: Griff's core components' (<see cref="TemplateSounds.Wire"/>), and a module's
    /// template adds its own components'.
    /// </summary>
    protected internal virtual void WireSounds(Entity entity) => TemplateSounds.Wire(entity);

    /// <summary>Builds the start scene.</summary>
    protected abstract Scene BuildStartScene();

    /// <summary>Builds the scenes besides the start scene, keyed by path relative to Assets ("Scenes/Game.scene").</summary>
    protected virtual Dictionary<string, Scene> BuildMoreScenes() => [];

    /// <summary>Builds its prefabs, keyed by path relative to Assets ("Prefabs/Bullet.prefab").</summary>
    protected virtual Dictionary<string, Entity> BuildPrefabs() => [];

    /// <summary>Script files, keyed by path relative to Assets ("Scripts/Ship.cs").</summary>
    public virtual IReadOnlyDictionary<string, string> Scripts() => new Dictionary<string, string>();

    /// <summary>Image files, keyed by path relative to Assets, as PNG bytes.</summary>
    public virtual IReadOnlyDictionary<string, byte[]> Images() => new Dictionary<string, byte[]>();

    /// <summary>Its tables of game data, keyed by path relative to Assets ("Tables/Enemies.table"); see <see cref="Data.Table"/>.</summary>
    public virtual IReadOnlyDictionary<string, Data.Table> Tables() => new Dictionary<string, Data.Table>();

    /// <summary>The start scene. Script components are added as missing components until compiled.</summary>
    public Scene StartScene() => WithSounds(BuildStartScene());

    /// <summary>The scenes besides the start scene, keyed by path relative to Assets.</summary>
    public IReadOnlyDictionary<string, Scene> MoreScenes() => BuildMoreScenes().ToDictionary(p => p.Key, p => WithSounds(p.Value));

    /// <summary>Its prefabs, keyed by path relative to Assets, with sounds on everything in them that makes one.</summary>
    public IReadOnlyDictionary<string, Entity> Prefabs()
    {
        var prefabs = BuildPrefabs();
        if (HasSounds)
        {
            foreach (var entity in prefabs.Values.SelectMany(p => p.SelfAndDescendants()))
                WireSounds(entity);
        }

        return prefabs;
    }

    /// <summary>Sound and music files, keyed by path relative to Assets, as WAV bytes.</summary>
    public IReadOnlyDictionary<string, byte[]> Sounds() => HasSounds ? TemplateSounds.Files(Music) : new Dictionary<string, byte[]>();

    public override string ToString() => Name;

    // Templates that come with sounds have them hooked up to everything that makes a noise, and music playing.
    private Scene WithSounds(Scene scene)
    {
        if (!HasSounds)
            return scene;
        foreach (var entity in scene.Entities)
            WireSounds(entity);
        TemplateSounds.AddMusic(scene);
        return scene;
    }
}
