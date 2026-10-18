using System.Text.Json.Nodes;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// A system that brings games saved by an earlier version of the game up to date as they're loaded: each one added
/// (<see cref="Game.AddSystem"/>, from the title screen's script or wherever games are loaded) is handed every saved game
/// whose <see cref="SavedGame.Version"/> is lower than <see cref="Game.SaveVersion"/>, before it's read.
/// </summary>
/// <example><code>
/// public class SaveUpgrades : GameSystem, ISaveUpgrader
/// {
///     public void UpgradeSave(SaveUpgrade save)
///     {
///         if (save.Version &lt; 2)
///             save.RenameComponent("Miner", "Digger");
///         if (save.Version &lt; 3)
///         {
///             foreach (var belt in save.Components("Belt"))
///                 belt.Rename("Speed", "ItemsPerSecond");
///         }
///     }
/// }
/// </code></example>
public interface ISaveUpgrader
{
    /// <summary>Changes <paramref name="save"/> to suit this version of the game; <see cref="SaveUpgrade.Version"/> says which it was saved by.</summary>
    void UpgradeSave(SaveUpgrade save);
}

/// <summary>
/// A game saved by an earlier version of the game, as an <see cref="ISaveUpgrader"/> is handed it before it's read: its
/// scene and the scenes it remembers as the scene file has them (entities by name, components by their saved type name,
/// values by property name), and its flags, to change as this version needs. What's left as it was is read as usual:
/// values of properties that are gone are ignored, new properties keep their defaults, and a component whose type is
/// gone is kept as it was saved, with a warning.
/// </summary>
public sealed class SaveUpgrade
{
    private readonly JsonObject[] _scenes;

    internal SaveUpgrade(string slot, int version, int currentVersion, JsonObject scene, IReadOnlyDictionary<string, JsonObject> remembered, List<string> flags)
    {
        (Slot, Version, CurrentVersion, Scene, RememberedScenes, Flags) = (slot, version, currentVersion, scene, remembered, flags);
        _scenes = [scene, .. remembered.Values];
    }

    /// <summary>The slot it's saved in.</summary>
    public string Slot { get; }

    /// <summary>The game's <see cref="Game.SaveVersion"/> when it was saved: 0 for one saved before the game had one.</summary>
    public int Version { get; }

    /// <summary>The game's <see cref="Game.SaveVersion"/> now, which it's being brought up to.</summary>
    public int CurrentVersion { get; }

    /// <summary>The flags set when it was saved, by name, to add to, take from or rename.</summary>
    public IList<string> Flags { get; }

    /// <summary>The saved scene's JSON, as a scene file has it, for changes the other members don't cover.</summary>
    public JsonObject Scene { get; }

    /// <summary>The scenes it remembers as they were left (see <see cref="RememberScene"/>), by name, as <see cref="Scene"/> is.</summary>
    public IReadOnlyDictionary<string, JsonObject> RememberedScenes { get; }

    /// <summary>
    /// Every component saved as <paramref name="type"/> (its saved name: a script's class name, or a built-in's such as
    /// "Griff.Health"), in the scene and the scenes it remembers, to read and change by property name.
    /// </summary>
    public IReadOnlyList<SavedData> Components(string type) =>
        [.. AllComponents().Where(c => TypeOf(c) == type).Select(c => new SavedData(c))];

    /// <summary>
    /// Saves every component of type <paramref name="from"/> as <paramref name="to"/> instead: a script class renamed in
    /// code (a <see cref="FormerNameAttribute"/> on the class does this for every scene and save). How many there were.
    /// </summary>
    public int RenameComponent(string from, string to)
    {
        var count = 0;
        foreach (var component in AllComponents().Where(c => TypeOf(c) == from))
        {
            component["type"] = to;
            count++;
        }

        return count;
    }

    /// <summary>Forgets every component of type <paramref name="type"/>. How many there were.</summary>
    public int RemoveComponent(string type)
    {
        var count = 0;
        foreach (var entity in AllEntities())
        {
            if (entity["components"] is JsonArray components)
                count += components.RemoveAll(c => c is JsonObject o && TypeOf(o) == type);
        }

        return count;
    }

    /// <summary>Renames every entity called <paramref name="from"/>. How many there were.</summary>
    public int RenameEntity(string from, string to)
    {
        var count = 0;
        foreach (var entity in AllEntities().Where(e => NameOf(e) == from))
        {
            entity["name"] = to;
            count++;
        }

        return count;
    }

    private IEnumerable<JsonObject> AllComponents() =>
        AllEntities().SelectMany(e => e["components"] is JsonArray components ? components.OfType<JsonObject>() : []);

    private IEnumerable<JsonObject> AllEntities()
    {
        var stack = new Stack<JsonObject>();
        foreach (var scene in _scenes)
            Push(scene["entities"]);
        while (stack.Count > 0)
        {
            var entity = stack.Pop();
            yield return entity;
            Push(entity["children"]);
        }

        void Push(JsonNode? list)
        {
            if (list is JsonArray array)
            {
                for (var i = array.Count - 1; i >= 0; i--)
                {
                    if (array[i] is JsonObject entity)
                        stack.Push(entity);
                }
            }
        }
    }

    private static string? TypeOf(JsonObject component) => component["type"] is JsonValue v && v.TryGetValue<string>(out var type) ? type : null;

    private static string? NameOf(JsonObject entity) => entity["name"] is JsonValue v && v.TryGetValue<string>(out var name) ? name : null;
}
