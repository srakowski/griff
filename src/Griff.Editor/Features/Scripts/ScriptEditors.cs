using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Scripting;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Scripts;

/// <summary>
/// The scripts open in the built-in editor, as document tabs next to the Scene and Game. Owns the language service
/// that every open script shares, keeps it in step with the files on disk, and saves scripts so they hot-reload.
/// </summary>
public sealed class ScriptEditors : ObservableObject, IDisposable
{
    private readonly IProjectSession _projectSession;
    private readonly IPlaySession _playSession;
    private readonly IScriptSession _scriptSession;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly ILogger<ScriptEditors> _logger;
    private readonly DispatcherTimer _diskDebounce;
    private FileSystemWatcher? _watcher;
    private ScriptDocument? _active;

    public ScriptEditors(IProjectSession projectSession, IPlaySession playSession, IScriptSession scriptSession, IDialogService dialogs, IStatusService status, PlayMode play, ILogger<ScriptEditors> logger)
    {
        _projectSession = projectSession;
        _playSession = playSession;
        _scriptSession = scriptSession;
        _dialogs = dialogs;
        _status = status;
        _logger = logger;
        _diskDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            _diskDebounce!.Stop();
            SyncWithDisk();
        }, Dispatcher.CurrentDispatcher);

        projectSession.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IProjectSession.Project))
                ProjectChanged();
        };
        projectSession.AddCloseGuard(ConfirmCloseAllAsync);
        // Like an IDE's build: playing saves the scripts first, so the game runs what you see.
        play.Started += (_, _) => SaveAll();
        _ = Language.WarmUpAsync();
    }

    /// <summary>Stops watching the scripts on disk (the editor is closing).</summary>
    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _diskDebounce.Stop();
        CloseAll();
        Language.Dispose();
    }

    public ScriptLanguage Language { get; } = new();

    public ObservableCollection<ScriptDocument> Documents { get; } = [];

    /// <summary>The script whose tab is showing, or null when the Scene or Game tab is.</summary>
    public ScriptDocument? Active
    {
        get => _active;
        set => SetProperty(ref _active, value);
    }

    /// <summary>
    /// Whether script tabs show the component beside the code (its properties and their values in the scene). Shared by
    /// every tab: open it in one, it's open in all.
    /// </summary>
    public bool ShowComponent
    {
        get => _showComponent;
        set => SetProperty(ref _showComponent, value);
    }

    private bool _showComponent = true;

    /// <summary>How wide the component beside the code is, in pixels, when it shows (kept from when it was last hidden).</summary>
    public double ComponentPaneWidth
    {
        get => _componentPaneWidth;
        set => SetProperty(ref _componentPaneWidth, Math.Max(220, value));
    }

    private double _componentPaneWidth = 340;

    /// <summary>Raised when a script should be brought to the front.</summary>
    public event EventHandler<ScriptDocument>? ShowRequested;

    /// <summary>Opens a script (or brings its tab forward) and optionally moves to a line.</summary>
    public ScriptDocument? Open(string path, int? line = null, int column = 1)
    {
        var full = Path.GetFullPath(path);
        var document = Documents.FirstOrDefault(d => string.Equals(d.FilePath, full, StringComparison.OrdinalIgnoreCase));
        if (document is null)
        {
            string text;
            try
            {
                text = File.ReadAllText(full);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Couldn't open {Path}: {Message}", full, ex.Message);
                return null;
            }

            document = new ScriptDocument(full, text, Language);
            Documents.Add(document);
        }

        if (line is { } l)
            document.NavigateToLine(l, column);
        ShowRequested?.Invoke(this, document);
        return document;
    }

    /// <summary>Opens the script that declares a component type, at its class.</summary>
    public async Task<ScriptDocument?> OpenComponentAsync(Type type)
    {
        if (await FindComponentAsync(type) is not { } location)
            return null;
        var document = Open(location.Path);
        document?.Navigate(location.Start, location.Length);
        return document;
    }

    /// <summary>Where a script component's class is declared, or null for a built-in component.</summary>
    public Task<ScriptLocation?> FindComponentAsync(Type type) => Language.FindTypeAsync(type.FullName ?? type.Name);

    /// <summary>Saves a script and recompiles, so its changes are live in the editor and the running game.</summary>
    public bool Save(ScriptDocument document)
    {
        if (!document.Write(out var error))
        {
            _dialogs.ShowError("Save failed", error ?? "");
            return false;
        }

        // Compile now, rather than when the file watcher notices, so the result can be shown straight away.
        var result = _scriptSession.ReloadScripts();
        if (result is null)
            return true;
        if (result.Success)
        {
            document.BuildState = ScriptBuildState.Compiled;
            document.Status = result.Unchanged
                ? $"Saved {DateTime.Now:t}"
                : _playSession.IsPlaying
                    ? $"Saved and swapped into the running game {DateTime.Now:t}"
                    : $"Saved and compiled {DateTime.Now:t}";
            _status.Show($"Saved {document.FileName}");
        }
        else
        {
            var errors = result.Diagnostics.Count(d => d.IsError);
            document.BuildState = ScriptBuildState.Failed;
            document.Status = $"Saved. {errors} error{(errors == 1 ? "" : "s")} in the scripts; the last good version keeps running";
        }

        foreach (var other in Documents.Where(d => d != document && !d.IsDirty))
            other.BuildState = document.BuildState;
        return true;
    }

    public void SaveAll()
    {
        foreach (var document in Documents.Where(d => d.IsDirty).ToList())
            Save(document);
    }

    /// <summary>Closes a tab, asking about unsaved changes. Returns false if the user cancelled.</summary>
    public bool Close(ScriptDocument document)
    {
        if (document.IsDirty)
        {
            switch (_dialogs.AskSaveChanges(document.FileName))
            {
                case SaveChangesChoice.Save when !Save(document):
                    return false;
                case SaveChangesChoice.Cancel:
                    return false;
            }
        }

        Documents.Remove(document);
        document.Dispose();
        // Back to what's on disk for a script closed without saving.
        if (File.Exists(document.FilePath))
            Language.SetText(document.FilePath, ReadShared(document.FilePath) ?? "");
        else
            Language.Remove(document.FilePath);
        return true;
    }

    /// <summary>Asks about every unsaved script before the project closes. Returns false if the user cancelled.</summary>
    public Task<bool> ConfirmCloseAllAsync()
    {
        foreach (var document in Documents.Where(d => d.IsDirty).ToList())
        {
            ShowRequested?.Invoke(this, document);
            switch (_dialogs.AskSaveChanges(document.FileName))
            {
                case SaveChangesChoice.Save when !Save(document):
                    return Task.FromResult(false);
                case SaveChangesChoice.Cancel:
                    return Task.FromResult(false);
                case SaveChangesChoice.Discard:
                    document.Text.UndoStack.MarkAsOriginalFile();
                    break;
            }
        }

        return Task.FromResult(true);
    }

    /// <summary>Forgets unsaved edits and closes every script, without asking (for tools like the demo player).</summary>
    internal void DiscardAll()
    {
        foreach (var document in Documents)
            document.Text.UndoStack.MarkAsOriginalFile();
        CloseAll();
        Active = null;
    }

    private void CloseAll()
    {
        foreach (var document in Documents)
            document.Dispose();
        Documents.Clear();
    }

    private void ProjectChanged()
    {
        CloseAll();
        Active = null;
        _watcher?.Dispose();
        _watcher = null;
        if (_projectSession.Project is not { } project)
        {
            Language.Load([]);
            return;
        }

        Language.Load(ScriptCompiler.ReadFolder(project.AssetsPath));
        Directory.CreateDirectory(project.AssetsPath);
        _watcher = new FileSystemWatcher(project.AssetsPath, "*.cs")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
        };
        FileSystemEventHandler changed = (_, _) => _diskDebounce.Dispatcher.InvokeAsync(() =>
        {
            _diskDebounce.Stop();
            _diskDebounce.Start();
        });
        _watcher.Changed += changed;
        _watcher.Created += changed;
        _watcher.Deleted += changed;
        _watcher.Renamed += (s, e) => changed(s, e);
        _watcher.EnableRaisingEvents = true;
    }

    // Scripts changed outside the editor: closed ones are re-read, open ones without edits take the new text.
    private void SyncWithDisk()
    {
        if (_projectSession.Project is not { } project)
            return;
        var open = Documents.ToDictionary(d => d.FilePath, d => d.Text.Text, StringComparer.OrdinalIgnoreCase);
        foreach (var document in Documents)
        {
            if (!document.IsDirty && File.Exists(document.FilePath) && ReadShared(document.FilePath) is { } text)
            {
                document.Reload(text);
                open[document.FilePath] = document.Text.Text;
            }
        }

        Language.Load(ScriptCompiler.ReadFolder(project.AssetsPath).Where(s => !open.ContainsKey(Path.GetFullPath(s.Path))), open);
    }

    private static string? ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
