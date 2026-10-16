using System.IO;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Core.Workspace;

/// <summary>Prefab files as the editor uses them: saved from a selection, instantiated by drag-and-drop.</summary>
public static class PrefabFiles
{
    // Prefab files read for looking at (what a placed copy changes from its prefab), per workspace: each with when it was
    // written, read again when the file changes and all let go when scripts reload (a script component's type changes).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IWorkspace, ReadPrefabs> s_read = [];

    private sealed class ReadPrefabs
    {
        public readonly Dictionary<string, (DateTime Written, Entity? Prefab)> Files = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The prefab <paramref name="copy"/> (a placed copy) was made from, as an entity read from its file, or null if it isn't a
    /// copy or its prefab can't be read. It's shared (the Inspector marks overrides from it, a demo's checks read it) and read again
    /// only when the file changes, so it's for looking at: never change it, or put it in a scene.
    /// </summary>
    public static Entity? PrefabOf(IWorkspace workspace, Entity copy) =>
        copy.Prefab is { } link && workspace.Project?.FindPrefab(link) is { } path ? Read(workspace, path) : null;

    /// <summary>The prefab file at <paramref name="path"/> (a full path), read as <see cref="PrefabOf"/> reads one.</summary>
    public static Entity? Read(IWorkspace workspace, string path)
    {
        var read = s_read.GetValue(workspace, w =>
        {
            var files = new ReadPrefabs();
            w.ScriptsReloaded += (_, _) => files.Files.Clear();
            return files;
        });
        var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (read.Files.TryGetValue(path, out var known) && known.Written == written)
            return known.Prefab;

        Entity? prefab;
        try
        {
            prefab = written == DateTime.MinValue ? null : workspace.Serializer.LoadPrefabFromString(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            prefab = null;
        }

        read.Files[path] = (written, prefab);
        return prefab;
    }

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
    /// Renames or moves the prefab at <paramref name="fromAssetPath"/> to <paramref name="toAssetPath"/> (both in
    /// Assets, making folders as needed), keeping every copy of it linked (see <see cref="Relink"/>).
    /// </summary>
    /// <exception cref="IOException">There's a file there already, or the prefab can't be moved.</exception>
    public static PrefabUpdate Move(IWorkspace workspace, string fromAssetPath, string toAssetPath)
    {
        if (workspace.Project is not { } project)
            return new PrefabUpdate(0, [], []);
        var to = FullPath(project, toAssetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(FullPath(project, fromAssetPath), to);
        return Relink(workspace, fromAssetPath, toAssetPath);
    }

    /// <summary>
    /// After the prefab at <paramref name="oldAssetPath"/> is renamed or moved to <paramref name="newAssetPath"/>: links
    /// every copy of it to its new place, in the project's scene and prefab files on disk, and in the open scene and
    /// prefab tabs (one undo step in each; one with no other changes stays saved, as its file is relinked too). The
    /// prefab's own tab, if it's open, follows its file.
    /// </summary>
    public static PrefabUpdate Relink(IWorkspace workspace, string oldAssetPath, string newAssetPath)
    {
        if (workspace.Project is not { } project)
            return new PrefabUpdate(0, [], []);

        var copies = 0;
        var places = new List<string>();
        var problems = new List<string>();
        var serializer = workspace.Serializer;

        foreach (var tab in workspace.Prefabs.Where(t => IsOwnTab(workspace, t, oldAssetPath)).ToList())
            tab.MoveTo(FullPath(project, newAssetPath));

        // Open documents count what they hold now, saved or not.
        var open = new[] { workspace.EditScene }.Concat(workspace.Prefabs).OfType<SceneDocument>().ToList();
        foreach (var document in open)
        {
            var count = document.Scene.Entities.Count(e => PrefabCopies.IsCopyOf(e, oldAssetPath));
            if (PrefabCopies.Relink(document.Scene, oldAssetPath, newAssetPath) is not { } edit)
                continue;
            var saved = !document.IsDirty;
            document.Execute(edit);
            if (saved)
                document.MarkSaved();
            copies += count;
            places.Add(document.Name);
        }

        // Every scene and prefab file on disk, the open ones' too (so their files stay right whether or not they're saved).
        var openFiles = open.Select(d => Path.GetFullPath(d.FilePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in project.FindScenes().Concat(project.FindPrefabs()))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            try
            {
                var count = file.EndsWith(Griff.Projects.Project.PrefabExtension, StringComparison.OrdinalIgnoreCase)
                    ? PrefabCopies.RelinkPrefabFile(file, oldAssetPath, newAssetPath, serializer)
                    : PrefabCopies.RelinkFile(file, oldAssetPath, newAssetPath, serializer);
                if (count > 0 && !openFiles.Contains(Path.GetFullPath(file)))
                {
                    copies += count;
                    places.Add(name);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                problems.Add($"{name}: {ex.Message}");
            }
        }

        return new PrefabUpdate(copies, places, problems);
    }

    private static string FullPath(Griff.Projects.Project project, string assetPath) =>
        Path.GetFullPath(Path.Combine(project.AssetsPath, PrefabCopies.Normalize(assetPath).Replace('/', Path.DirectorySeparatorChar)));

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
    /// the old version, in the scene being edited (one undo step, with the link), in the project's other scene files, and
    /// inside its other prefabs (a chest holding coins), on disk and in their open tabs (one undo step in each). The
    /// prefab's own tab, if it's open, is brought up to date the same way. A prefab that the saved one holds a copy of
    /// isn't updated, as they'd hold each other.
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

        // Copies placed inside other prefabs (coins in a chest), on disk: open ones' files too, so they stay right
        // whether or not their tabs are saved. Not inside a prefab the saved one holds a copy of: they'd hold each other.
        var holds = Holds(json, serializer);
        var openTabs = workspace.Prefabs.Select(t => Path.GetFullPath(t.FilePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var prefabFile in project.FindPrefabs())
        {
            var other = AssetPathOf(project, prefabFile);
            if (string.Equals(other, PrefabCopies.Normalize(assetPath), StringComparison.OrdinalIgnoreCase))
                continue;

            var name = Path.GetFileNameWithoutExtension(prefabFile);
            try
            {
                if (holds.Contains(other))
                {
                    if (PrefabCopies.In(LoadInto(prefabFile, serializer), assetPath).Count > 0)
                        problems.Add($"{name}: it and {PrefabCopies.NameOf(assetPath)} hold copies of each other");
                    continue;
                }

                if (PrefabCopies.UpdatePrefabFile(prefabFile, assetPath, json, oldJson, serializer) is > 0 and var count && !openTabs.Contains(Path.GetFullPath(prefabFile)))
                {
                    copies += count;
                    scenes.Add(name);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                problems.Add($"{name}: {ex.Message}");
            }
        }

        foreach (var tab in workspace.Prefabs)
        {
            if (tab.Scene.Roots.Count != 1)
                continue;
            var root = tab.Scene.Roots[0];
            var own = IsOwnTab(workspace, tab, assetPath);
            var saved = !tab.IsDirty;

            try
            {
                if (own && source?.Scene != tab.Scene)
                {
                    // The prefab's own tab, if it wasn't what saved it, shows the new version, keeping its unsaved changes.
                    var fresh = serializer.LoadPrefabFromString(json);
                    if (oldJson is not null)
                        fresh = PrefabOverrides.Merge(root, serializer.LoadPrefabFromString(oldJson), fresh, serializer);
                    tab.Execute(new UpdatePrefabCopyEdit(tab.Scene, root, fresh));
                }
                else if (!own && !holds.Contains(AssetPathOf(project, tab.FilePath)))
                {
                    // Copies inside another prefab's tab are brought up to date, and something in it the prefab was
                    // saved from becomes a copy of it there.
                    var edits = new List<IEdit>();
                    var here = source?.Scene == tab.Scene && source != root ? source : null;
                    if (here is not null && !PrefabCopies.IsCopyOf(here, assetPath))
                    {
                        // A link its file doesn't have yet.
                        edits.Add(new SetEntitiesPrefabEdit([here], PrefabCopies.Normalize(assetPath)));
                        saved = false;
                    }
                    var count = PrefabCopies.In(tab.Scene, assetPath).Count(c => c != here);
                    if (PrefabCopies.Update(tab.Scene, assetPath, json, oldJson, serializer, except: here) is { } update)
                    {
                        edits.Add(update);
                        copies += count;
                        scenes.Add(tab.Name);
                    }

                    if (edits.Count > 0)
                        tab.Execute(edits.Count == 1 ? edits[0] : new CompositeEdit($"Save {PrefabCopies.NameOf(assetPath)} as Prefab", edits));
                }
                else
                {
                    continue;
                }

                // Its file was brought up to date too, so one with nothing unsaved before still has nothing unsaved.
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

    // The prefabs a prefab's text holds copies of, by their paths in Assets.
    private static HashSet<string> Holds(string json, SceneSerializer serializer)
    {
        try
        {
            return serializer.LoadPrefabFromString(json).SelfAndDescendants()
                .Select(e => e.Prefab).OfType<string>().Select(PrefabCopies.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (InvalidDataException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // A prefab file's entity, in a scene of its own to look through.
    private static Scene LoadInto(string prefabFile, SceneSerializer serializer)
    {
        var scene = new Scene();
        scene.Add(serializer.LoadPrefabFromString(File.ReadAllText(prefabFile)));
        return scene;
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

/// <summary>
/// What saving (or moving) a prefab updated: how many copies, in which scenes (and prefabs), and the ones that couldn't
/// be updated.
/// </summary>
public sealed record PrefabUpdate(int Copies, IReadOnlyList<string> Scenes, IReadOnlyList<string> Problems)
{
    /// <summary>"Updated 3 copies in Level 1 and Level 2.", or empty with none.</summary>
    public string Summary => Describe("Updated");

    /// <summary>"Relinked 3 copies in Level 1 and Level 2." with "Relinked", or empty with none.</summary>
    public string Describe(string verb) => Copies == 0 ? "" :
        $"{verb} {Copies} cop{(Copies == 1 ? "y" : "ies")} in {(Scenes.Count == 1 ? Scenes[0] : string.Join(", ", Scenes.Take(Scenes.Count - 1)) + " and " + Scenes[^1])}.";
}
