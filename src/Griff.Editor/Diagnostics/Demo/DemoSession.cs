#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// Plays a demo's chapters in order and handles the viewer's transport: a jump back restores the project as it was
/// when that chapter began (from a snapshot taken then), a jump forward hurries through, Exit stops cleanly. Shared by
/// every demo, the guided tour and the <c>.demo</c> scripts alike.
/// </summary>
internal sealed class DemoSession(DemoUi ui, Workspace workspace, ISelection selection, PlayMode play, DemoControl control, Action<string> log)
{
    // Where each chapter's starting state is kept, so the viewer can rewind to it.
    private static readonly string SnapshotRoot = Path.Combine(Path.GetTempPath(), "griff-demo-snapshots");

    /// <summary>Extra cleanup when a chapter is cut short (a demo's own helpers, processes it started).</summary>
    public Action? OnRecover { get; set; }

    /// <summary>
    /// Runs <paramref name="chapters"/>. Chapters before <paramref name="firstSnapshot"/> run before there's a project to
    /// snapshot; going back to one of them starts the demo over in a fresh editor.
    /// </summary>
    /// <returns>True if the viewer asked to start over from the very beginning (the caller restarts the editor).</returns>
    public async Task<bool> Run(IReadOnlyList<(string Title, Func<Task> Run)> chapters, int firstSnapshot)
    {
        control.Chapters = chapters.Select(c => c.Title).ToList();
        if (Directory.Exists(SnapshotRoot))
            Directory.Delete(SnapshotRoot, recursive: true);

        var i = 0;
        int? restore = null;
        while (i < chapters.Count)
        {
            try
            {
                // A jump lands here; another jump while restoring just restores again.
                if (restore is { } target)
                {
                    restore = null;
                    i = target;
                    await Restore(target);
                }

                control.Enter(i);
                if (i >= firstSnapshot && !control.Snapshots.Contains(i))
                    await Snapshot(i);
                await chapters[i].Run();
                i++;
            }
            catch (OperationCanceledException) when (control.ExitRequested)
            {
                log("viewer exited");
                await Recover();
                return false;
            }
            catch (OperationCanceledException) when (control.RewindTo is { } target)
            {
                log($"jump to chapter {target} from {i}");
                control.Rearm();
                await Recover();
                if (target < firstSnapshot)
                    return true;
                restore = target;
            }
        }

        return false;
    }

    /// <summary>Saves the open scene and copies the project, so this chapter can be gone back to.</summary>
    private async Task Snapshot(int chapter)
    {
        if (workspace.Project is not { } project || workspace.Scene is not { } scene || play.IsPlaying)
            return;
        if (scene.IsDirty)
            await workspace.SaveSceneAsync();

        var folder = SnapshotFolder(chapter);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        CopyProject(project.RootPath, folder);
        File.WriteAllText(Path.Combine(folder, ".demo-scene"), project.GetRelativePath(scene.FilePath));
        File.WriteAllText(Path.Combine(folder, ".demo-root"), project.RootPath);
        control.Snapshots.Add(chapter);
        log($"snapshot {chapter}");
    }

    /// <summary>Puts the project back as it was when <paramref name="chapter"/> began, and reopens it.</summary>
    private async Task Restore(int chapter)
    {
        var folder = SnapshotFolder(chapter);
        var root = File.ReadAllText(Path.Combine(folder, ".demo-root"));
        workspace.Scene?.MarkSaved();
        ui.Scripts?.DiscardAll();
        if (play.IsPlaying)
            play.Stop();

        // Everything but the builds goes back to how the snapshot has it.
        foreach (var entry in Directory.Exists(root) ? Directory.EnumerateFileSystemEntries(root) : [])
        {
            if (Path.GetFileName(entry) == "Build")
                continue;
            if (Directory.Exists(entry))
                Directory.Delete(entry, recursive: true);
            else
                File.Delete(entry);
        }

        CopyProject(folder, root);
        var scene = File.ReadAllText(Path.Combine(folder, ".demo-scene"));
        File.Delete(Path.Combine(root, ".demo-scene"));
        File.Delete(Path.Combine(root, ".demo-root"));
        await workspace.OpenProjectAsync(root);
        var scenePath = Path.Combine(root, scene);
        if (workspace.Scene?.FilePath is { } open && !string.Equals(Path.GetFullPath(open), Path.GetFullPath(scenePath), StringComparison.OrdinalIgnoreCase))
            await workspace.OpenSceneAsync(scenePath);
        selection.Select(null);
        await DemoUi.Settle(300);
        log($"restored {chapter}");
    }

    /// <summary>Undoes whatever a chapter cut short was in the middle of: menus, dialogs, play mode, held keys.</summary>
    public async Task Recover()
    {
        OnRecover?.Invoke();
        ui.ScriptTabs?.ClosePopups();
        ui.StopSpeaking();
        ui.Overlay.Reset();
        ui.Input.Boost = 1;
        DemoInput.ReleaseAll();

        foreach (var item in DemoUi.Descendants<MenuItem>(ui.Main.MainMenu))
            item.IsSubmenuOpen = false;
        foreach (var menu in DemoUi.Roots().SelectMany(DemoUi.Descendants<ContextMenu>).ToList())
            menu.IsOpen = false;
        foreach (var combo in DemoUi.Descendants<ComboBox>(ui.Main))
            combo.IsDropDownOpen = false;
        foreach (var window in Application.Current.Windows.OfType<Window>().Where(w => w != ui.Main && w is not DemoOverlay and not DemoControlBar).ToList())
            window.Close();
        // And the editors open in tabs (the Sprite Painter, a table), throwing away what they hadn't saved.
        ui.Documents?.CloseAll(ask: false);

        if (play.IsPlaying)
            play.Stop();

        Keyboard.ClearFocus();
        ui.Main.Activate();
        // Not DemoUi.Settle: after an exit, waits that check the control would stop straight away.
        await Task.Delay(200);
    }

    private static string SnapshotFolder(int chapter) =>
        Path.Combine(SnapshotRoot, chapter.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void CopyProject(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            var top = relative.Split(Path.DirectorySeparatorChar)[0];
            if (top is "Build" or ".griff")
                continue;
            var target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
#endif
