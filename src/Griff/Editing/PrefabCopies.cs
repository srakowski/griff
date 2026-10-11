using System.Numerics;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>
/// Placed copies of prefabs: entities whose <see cref="Entity.Prefab"/> names a prefab file. Saving a prefab brings
/// every copy up to date, keeping what each copy changed (<see cref="PrefabOverrides"/>), in the open scene as one undo
/// step (<see cref="Update"/>) and in scene files on disk (<see cref="UpdateFile"/>).
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
    /// One undo step bringing every copy of the prefab in <paramref name="scene"/> up to date with its new version,
    /// <paramref name="prefabJson"/> (see <see cref="UpdatePrefabCopyEdit"/>), skipping <paramref name="except"/> (the
    /// entity the prefab was just saved from). Each copy keeps what it changed from the old version,
    /// <paramref name="oldPrefabJson"/> (see <see cref="PrefabOverrides.Merge"/>); without it (or if it can't be read),
    /// copies take the new version whole. Null if there are no copies to update.
    /// </summary>
    /// <exception cref="InvalidDataException">The new version can't be read.</exception>
    public static IEdit? Update(Scene scene, string prefabPath, string prefabJson, string? oldPrefabJson, SceneSerializer serializer, Entity? except = null)
    {
        var copies = In(scene, prefabPath).Where(copy => copy != except).ToList();
        if (copies.Count == 0)
            return null;

        var old = Old(oldPrefabJson, serializer);
        var edits = copies
            .Select(IEdit (copy) =>
            {
                var fresh = serializer.LoadPrefabFromString(prefabJson);
                return new UpdatePrefabCopyEdit(scene, copy, old is null ? fresh : PrefabOverrides.Merge(copy, old, fresh, serializer));
            })
            .ToList();
        return edits.Count == 1 ? edits[0] : new CompositeEdit($"Update {edits.Count} copies of {NameOf(prefabPath)}", edits);
    }

    /// <summary>
    /// Brings the copies of a prefab in the scene file at <paramref name="scenePath"/> up to date with
    /// <paramref name="prefabJson"/> (the prefab file's new text), keeping what each changed from
    /// <paramref name="oldPrefabJson"/> (its old text, if known), and rewriting the file only if it has any.
    /// </summary>
    /// <returns>How many copies were updated.</returns>
    /// <exception cref="InvalidDataException">The scene or the prefab can't be read.</exception>
    public static int UpdateFile(string scenePath, string prefabPath, string prefabJson, string? oldPrefabJson, SceneSerializer serializer)
    {
        var scene = serializer.LoadFromString(File.ReadAllText(scenePath));
        if (Update(scene, prefabPath, prefabJson, oldPrefabJson, serializer) is not { } edit)
            return 0;

        edit.Apply();

        // Write to a temporary file first so a failed save never truncates the real one.
        var temp = scenePath + ".saving";
        File.WriteAllText(temp, serializer.SaveToString(scene));
        File.Move(temp, scenePath, overwrite: true);
        return edit is CompositeEdit composite ? composite.Edits.Count : 1;
    }

    /// <summary>
    /// One undo step linking every copy of the prefab at <paramref name="oldPath"/> in <paramref name="scene"/> (copies
    /// inside other copies too) to <paramref name="newPath"/>, where its file was renamed or moved. Null if there are none.
    /// </summary>
    public static IEdit? Relink(Scene scene, string oldPath, string newPath)
    {
        var copies = Linked(scene, oldPath);
        if (copies.Count == 0)
            return null;
        return new CompositeEdit($"Relink {(copies.Count == 1 ? "a copy" : $"{copies.Count} copies")} of {NameOf(newPath)}",
            [new SetEntitiesPrefabEdit(copies, Normalize(newPath))]);
    }

    // Every copy of the prefab, copies inside other copies too.
    private static List<Entity> Linked(Scene scene, string prefabPath) => scene.Entities.Where(e => IsCopyOf(e, prefabPath)).ToList();

    /// <summary>
    /// Links the copies of the prefab at <paramref name="oldPath"/> in the scene file at <paramref name="scenePath"/> to
    /// <paramref name="newPath"/>, rewriting the file only if it has any.
    /// </summary>
    /// <returns>How many copies were relinked.</returns>
    /// <exception cref="InvalidDataException">The scene can't be read.</exception>
    public static int RelinkFile(string scenePath, string oldPath, string newPath, SceneSerializer serializer)
    {
        var scene = serializer.LoadFromString(File.ReadAllText(scenePath));
        return Relink(scene, scenePath, oldPath, newPath, () => serializer.SaveToString(scene));
    }

    /// <summary>
    /// Links the copies of the prefab at <paramref name="oldPath"/> placed inside the prefab file at
    /// <paramref name="prefabFile"/> to <paramref name="newPath"/>, rewriting the file only if it has any.
    /// </summary>
    /// <returns>How many copies were relinked.</returns>
    /// <exception cref="InvalidDataException">The prefab can't be read.</exception>
    public static int RelinkPrefabFile(string prefabFile, string oldPath, string newPath, SceneSerializer serializer)
    {
        var scene = new Scene();
        var root = serializer.LoadPrefabFromString(File.ReadAllText(prefabFile));
        scene.Add(root);
        return Relink(scene, prefabFile, oldPath, newPath, () => serializer.SavePrefabToString(root));
    }

    // Relinks a file's copies and writes it (through a temporary file, so a failed save never truncates the real one).
    private static int Relink(Scene scene, string file, string oldPath, string newPath, Func<string> text)
    {
        var copies = Linked(scene, oldPath);
        if (copies.Count == 0)
            return 0;
        foreach (var copy in copies)
            copy.Prefab = Normalize(newPath);
        var temp = file + ".saving";
        File.WriteAllText(temp, text());
        File.Move(temp, file, overwrite: true);
        return copies.Count;
    }

    // The prefab as it was, or null if that isn't known or can't be read: then copies just take the new one.
    private static Entity? Old(string? json, SceneSerializer serializer)
    {
        if (json is null)
            return null;
        try
        {
            return serializer.LoadPrefabFromString(json);
        }
        catch (InvalidDataException)
        {
            return null;
        }
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
/// Brings a placed copy of a prefab up to date with a fresh entity made from it (and merged with the copy's overrides):
/// the copy takes the fresh entity's components, scale and children, and keeps its own name, place (position and rotation), parent, enabled and locked state, id and
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
