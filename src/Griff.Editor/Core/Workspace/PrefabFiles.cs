using System.IO;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Core.Workspace;

/// <summary>Prefab files as the editor uses them: saved from a selection, instantiated by drag-and-drop.</summary>
public static class PrefabFiles
{
    /// <summary>A file name made from an entity name: invalid characters become underscores.</summary>
    public static string SafeName(string entityName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(entityName.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return name.Length == 0 ? "Prefab" : name;
    }

    /// <summary>A file's path in Assets as copies store it ("Prefabs/Coin.prefab").</summary>
    public static string AssetPathOf(Griff.Projects.Project project, string path) =>
        PrefabCopies.Normalize(Path.GetRelativePath(project.AssetsPath, path));

    /// <summary>True if <paramref name="document"/> is the prefab at <paramref name="assetPath"/>'s own tab, which can't hold a copy of itself.</summary>
    public static bool IsOwnTab(IWorkspace workspace, SceneDocument document, string assetPath) =>
        document.IsPrefab && workspace.Project is { } project &&
        string.Equals(AssetPathOf(project, document.FilePath), PrefabCopies.Normalize(assetPath), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A new copy of the prefab at <paramref name="assetPath"/> (relative to Assets), linked to it so saving the prefab
    /// updates it, or null if it can't be read.
    /// </summary>
    public static Entity? Instantiate(IWorkspace workspace, string assetPath, ICollection<string>? warnings = null)
    {
        if (workspace.Project is not { } project)
            return null;

        var path = Path.Combine(project.AssetsPath, assetPath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            var entity = workspace.Serializer.LoadPrefabFromString(File.ReadAllText(path), warnings);
            entity.Prefab = PrefabCopies.Normalize(assetPath);
            return entity;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            warnings?.Add(ex.Message);
            return null;
        }
    }

    /// <summary>
    /// After the prefab at <paramref name="assetPath"/> is saved as <paramref name="json"/> over
    /// <paramref name="oldJson"/> (null if it's new or that isn't known): links <paramref name="source"/> (the entity it
    /// was saved from, if any) to it, and brings every other copy of it up to date, keeping what each copy changed from
    /// the old version, in the scene being edited (one undo step, with the link) and in the project's other scene files.
    /// The prefab's tab, if it's open, is brought up to date the same way (as one undo step there).
    /// </summary>
    public static PrefabUpdate UpdateCopies(IWorkspace workspace, string assetPath, string json, string? oldJson, Entity? source)
    {
        if (workspace.Project is not { } project)
            return new PrefabUpdate(0, [], []);

        var copies = 0;
        var scenes = new List<string>();
        var problems = new List<string>();
        var serializer = workspace.Serializer;

        // The scene being edited (not the running copy while playing), through its undo history.
        if (workspace.EditScene is { } document)
        {
            var edits = new List<IEdit>();
            var here = source?.Scene == document.Scene ? source : null;
            if (here is not null && !PrefabCopies.IsCopyOf(here, assetPath))
                edits.Add(new SetEntitiesPrefabEdit([here], PrefabCopies.Normalize(assetPath)));

            try
            {
                var count = PrefabCopies.In(document.Scene, assetPath).Count(c => c != here);
                if (PrefabCopies.Update(document.Scene, assetPath, json, oldJson, serializer, except: here) is { } update)
                {
                    edits.Add(update);
                    copies += count;
                    scenes.Add(document.Name);
                }
            }
            catch (InvalidDataException ex)
            {
                problems.Add($"{document.Name}: {ex.Message}");
            }

            if (edits.Count > 0)
                document.Execute(edits.Count == 1 ? edits[0] : new CompositeEdit($"Save {PrefabCopies.NameOf(assetPath)} as Prefab", edits));
        }

        // Every other scene, on disk.
        var open = workspace.EditScene is { } edit ? Path.GetFullPath(edit.FilePath) : null;
        foreach (var scenePath in project.FindScenes())
        {
            if (string.Equals(Path.GetFullPath(scenePath), open, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (PrefabCopies.UpdateFile(scenePath, assetPath, json, oldJson, serializer) is > 0 and var count)
                {
                    copies += count;
                    scenes.Add(Path.GetFileNameWithoutExtension(scenePath));
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                problems.Add($"{Path.GetFileNameWithoutExtension(scenePath)}: {ex.Message}");
            }
        }

        foreach (var tab in workspace.Prefabs)
        {
            if (tab.Scene.Roots.Count != 1)
                continue;
            var root = tab.Scene.Roots[0];

            // Something inside a prefab's tab, saved as a prefab of its own, becomes a copy of it there.
            if (source?.Scene == tab.Scene)
            {
                if (source != root && !PrefabCopies.IsCopyOf(source, assetPath))
                    tab.Execute(new SetEntitiesPrefabEdit([source], PrefabCopies.Normalize(assetPath)));
                continue;
            }

            // The prefab's own tab, if it's open and wasn't what saved it, shows the new version, keeping its unsaved
            // changes.
            if (!string.Equals(AssetPathOf(project, tab.FilePath), PrefabCopies.Normalize(assetPath), StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var fresh = serializer.LoadPrefabFromString(json);
                if (oldJson is not null)
                    fresh = PrefabOverrides.Merge(root, serializer.LoadPrefabFromString(oldJson), fresh, serializer);
                var saved = !tab.IsDirty;
                tab.Execute(new UpdatePrefabCopyEdit(tab.Scene, root, fresh));
                if (saved)
                    tab.MarkSaved();
            }
            catch (InvalidDataException ex)
            {
                problems.Add($"{tab.Name}'s tab: {ex.Message}");
            }
        }

        return new PrefabUpdate(copies, scenes, problems);
    }
    /// <summary>The prefab file's text, or null if there's no such file (or it can't be read).</summary>
    public static string? ReadText(IWorkspace workspace, string assetPath)
    {
        if (workspace.Project is not { } project || project.FindPrefab(assetPath) is not { } path)
            return null;
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Applies a field the selected copies changed to their prefabs: sets it in each prefab file to the copy's value,
    /// then brings the prefab's other copies up to date (keeping what each of them changed). The copies themselves
    /// then match their prefab there. <paramref name="components"/> are the field's components, one per entity;
    /// those whose value is the prefab's are skipped.
    /// </summary>
    public static PrefabUpdate ApplyField(IWorkspace workspace, IReadOnlyList<Component> components, PropertyDescriptor property)
    {
        var copies = 0;
        var scenes = new List<string>();
        var problems = new List<string>();
        var serializer = workspace.Serializer;
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in components)
        {
            if (component.Entity is not { } entity || PrefabOverrides.CopyFor(entity) is not { Prefab: { } link } copy || done.Contains(PrefabCopies.Normalize(link)))
                continue;

            var name = PrefabCopies.NameOf(link);
            if (workspace.Project is not { } project || project.FindPrefab(link) is not { } path || ReadText(workspace, link) is not { } oldJson)
            {
                problems.Add($"{name}: the prefab isn't in the project");
                continue;
            }

            try
            {
                var prefab = serializer.LoadPrefabFromString(oldJson);
                if (PrefabOverrides.Counterpart(prefab, copy, entity) is not { } part || PrefabOverrides.Counterpart(part, component) is not { } prefabComponent)
                {
                    problems.Add($"{name}: the prefab has no {component.GetType().Name} there");
                    continue;
                }

                if (!PrefabOverrides.Differs(component, prefabComponent, property))
                    continue;
                property.SetValue(prefabComponent, PrefabOverrides.CopyValue(property.GetValue(component)));
                var json = serializer.SavePrefabToString(prefab);
                File.WriteAllText(path, json);
                done.Add(PrefabCopies.Normalize(link));

                var update = UpdateCopies(workspace, AssetPathOf(project, path), json, oldJson, source: copy);
                copies += update.Copies;
                scenes.AddRange(update.Scenes.Except(scenes));
                problems.AddRange(update.Problems);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                problems.Add($"{name}: {ex.Message}");
            }
        }

        return new PrefabUpdate(copies, scenes, problems);
    }

    /// <summary>
    /// One undo step putting the copies back as their prefab has them: every change each made from it (fields,
    /// components and children added or taken away) is undone, keeping their own name and place. Null if none of them
    /// changes anything (or their prefabs can't be read).
    /// </summary>
    public static IEdit? Revert(IWorkspace workspace, Scene scene, IEnumerable<Entity> copies)
    {
        var edits = new List<UpdatePrefabCopyEdit>();
        foreach (var copy in copies)
        {
            if (copy.Prefab is not { } link || ReadText(workspace, link) is not { } json)
                continue;
            try
            {
                var prefab = workspace.Serializer.LoadPrefabFromString(json);
                if (PrefabOverrides.Find(copy, prefab, workspace.Components).Count > 0)
                    edits.Add(new UpdatePrefabCopyEdit(scene, copy, prefab));
            }
            catch (InvalidDataException)
            {
                // A prefab that can't be read has nothing to revert to.
            }
        }

        return edits.Count switch
        {
            0 => null,
            1 => new CompositeEdit($"Revert {edits[0].Copy.Name} to Prefab", [edits[0]]),
            _ => new CompositeEdit($"Revert {edits.Count} copies to their prefab", [.. edits]),
        };
    }
}

/// <summary>What saving a prefab updated: how many copies, in which scenes, and the scenes that couldn't be updated.</summary>
public sealed record PrefabUpdate(int Copies, IReadOnlyList<string> Scenes, IReadOnlyList<string> Problems)
{
    /// <summary>"Updated 3 copies in Level 1 and Level 2.", or empty with none.</summary>
    public string Summary => Copies == 0 ? "" :
        $"Updated {Copies} cop{(Copies == 1 ? "y" : "ies")} in {(Scenes.Count == 1 ? Scenes[0] : string.Join(", ", Scenes.Take(Scenes.Count - 1)) + " and " + Scenes[^1])}.";
}
