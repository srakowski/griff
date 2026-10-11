using System.Numerics;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>
/// Placed copies of prefabs: entities whose <see cref="Entity.Prefab"/> names a prefab file. Saving a prefab brings
/// every copy up to date, in the open scene as one undo step (<see cref="Update"/>) and in scene files on disk
/// (<see cref="UpdateFile"/>).
/// </summary>
public static class PrefabCopies
{
    /// <summary>A prefab's path in Assets as copies store it: forward slashes ("Prefabs/Coin.prefab").</summary>
    public static string Normalize(string assetPath) => assetPath.Replace('\\', '/').TrimStart('/');

    /// <summary>True if <paramref name="entity"/> is a placed copy of the prefab at <paramref name="prefabPath"/>.</summary>
    public static bool IsCopyOf(Entity entity, string prefabPath) =>
        entity.Prefab is { } prefab && string.Equals(Normalize(prefab), Normalize(prefabPath), StringComparison.OrdinalIgnoreCase);

    /// <summary>The prefab's name, from a copy's link ("Prefabs/Coin.prefab" is "Coin").</summary>
    public static string NameOf(string prefabPath) => Path.GetFileNameWithoutExtension(Normalize(prefabPath));

    /// <summary>
    /// The copies of the prefab at <paramref name="prefabPath"/> in <paramref name="scene"/>, in scene order. A copy
    /// inside another copy of the same prefab is left to that one.
    /// </summary>
    public static IReadOnlyList<Entity> In(Scene scene, string prefabPath)
    {
        var copies = new List<Entity>();
        foreach (var root in scene.Roots)
            Collect(root, prefabPath, copies);
        return copies;
    }

    /// <summary>
    /// One undo step bringing every copy of the prefab in <paramref name="scene"/> up to date with fresh entities from
    /// <paramref name="instantiate"/> (see <see cref="UpdatePrefabCopyEdit"/>), skipping <paramref name="except"/> (the
    /// entity the prefab was just saved from). Null if there are no copies to update.
    /// </summary>
    public static IEdit? Update(Scene scene, string prefabPath, Func<Entity> instantiate, Entity? except = null)
    {
        var edits = In(scene, prefabPath)
            .Where(copy => copy != except)
            .Select(IEdit (copy) => new UpdatePrefabCopyEdit(scene, copy, instantiate()))
            .ToList();
        return edits.Count switch
        {
            0 => null,
            1 => edits[0],
            _ => new CompositeEdit($"Update {edits.Count} copies of {NameOf(prefabPath)}", edits),
        };
    }

    /// <summary>
    /// Brings the copies of a prefab in the scene file at <paramref name="scenePath"/> up to date with
    /// <paramref name="prefabJson"/> (the prefab file's text), rewriting the file only if it has any.
    /// </summary>
    /// <returns>How many copies were updated.</returns>
    /// <exception cref="InvalidDataException">The scene or the prefab can't be read.</exception>
    public static int UpdateFile(string scenePath, string prefabPath, string prefabJson, SceneSerializer serializer)
    {
        var scene = serializer.LoadFromString(File.ReadAllText(scenePath));
        if (Update(scene, prefabPath, () => serializer.LoadPrefabFromString(prefabJson)) is not { } edit)
            return 0;

        edit.Apply();

        // Write to a temporary file first so a failed save never truncates the real one.
        var temp = scenePath + ".saving";
        File.WriteAllText(temp, serializer.SaveToString(scene));
        File.Move(temp, scenePath, overwrite: true);
        return edit is CompositeEdit composite ? composite.Edits.Count : 1;
    }

    private static void Collect(Entity entity, string prefabPath, List<Entity> copies)
    {
        if (IsCopyOf(entity, prefabPath))
        {
            copies.Add(entity);
            return;
        }

        foreach (var child in entity.Children)
            Collect(child, prefabPath, copies);
    }
}

/// <summary>
/// Brings a placed copy of a prefab up to date with a fresh entity made from it: the copy takes the prefab's components,
/// scale and children, and keeps its own name, place (position and rotation), parent, enabled and locked state, id and
/// link, so the selection and the fields that name it still find it.
/// </summary>
public sealed class UpdatePrefabCopyEdit : IEdit
{
    private readonly Scene _scene;
    private readonly List<Component> _oldComponents;
    private readonly List<Component> _newComponents;
    private readonly List<Entity> _oldChildren;
    private readonly List<Entity> _newChildren;
    private readonly Vector3 _oldScale;
    private readonly Vector3 _newScale;

    /// <param name="fresh">A detached entity just made from the prefab; its components and children move to the copy.</param>
    public UpdatePrefabCopyEdit(Scene scene, Entity copy, Entity fresh)
    {
        if (fresh.Scene is not null || fresh.Parent is not null)
            throw new ArgumentException("The fresh entity should be detached.", nameof(fresh));

        _scene = scene;
        Copy = copy;
        _oldComponents = copy.Components.Skip(1).ToList();
        _oldChildren = copy.Children.ToList();
        _oldScale = copy.Transform.Scale;

        // Take the fresh entity apart: its components and children belong to the copy from now on.
        _newComponents = fresh.Components.Skip(1).ToList();
        foreach (var component in _newComponents)
            fresh.RemoveComponent(component);
        _newChildren = fresh.Children.ToList();
        foreach (var child in _newChildren)
            child.Parent = null;
        fresh.ChildList.Clear();
        _newScale = fresh.Transform.Scale;
    }

    public Entity Copy { get; }

    public string Description => $"Update {Copy.Name} from its prefab";

    public void Apply() => Swap(_oldComponents, _newComponents, _oldChildren, _newChildren, _newScale);

    public void Revert() => Swap(_newComponents, _oldComponents, _newChildren, _oldChildren, _oldScale);

    private void Swap(List<Component> fromComponents, List<Component> toComponents, List<Entity> fromChildren, List<Entity> toChildren, Vector3 scale)
    {
        foreach (var component in fromComponents)
            Copy.RemoveComponent(component);
        foreach (var component in toComponents)
            Copy.AddComponent(component);
        Copy.Transform.Scale = scale;

        foreach (var child in fromChildren)
            _scene.Remove(child);
        foreach (var child in toChildren)
            _scene.Add(child, Copy);
    }
}
