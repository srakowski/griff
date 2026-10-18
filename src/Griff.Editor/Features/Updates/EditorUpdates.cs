using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Updates;

/// <summary>
/// Keeps an installed Griff up to date. A while after the editor starts (unless <see cref="ChecksAutomatically"/> is off)
/// and whenever Help > Check for Updates is picked, it looks for a newer release, downloads it, then asks whether to restart
/// into it now or on the next start. Help > Check for Updates opens the Updates window, which shows what it's doing as it
/// does it (<see cref="Activity"/>, with a bar while it's busy) and then what came of it; the check after starting says
/// nothing unless there's an update. A Griff that wasn't installed by the installer (a build from source, the portable zip)
/// never looks: it says how to get a newer one instead.
/// </summary>
public sealed class EditorUpdates(IUpdater updater, ISettingsStore settings, IStatusService status, IDialogService dialogs, IProjectSession projectSession, IPlaySession playSession,
    ILogger<EditorUpdates> logger, Core.EditorWindow editorWindow) : ObservableObject
{
    /// <summary>How long after the editor starts it first looks for an update, so starting never waits on it.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    /// <summary>The question asked once an update is downloaded (an agent answers it by this title, true to restart now).</summary>
    public const string ReadyTitle = "Update ready";

    /// <summary>The Updates window's title (agents find it by this name in ui_roots).</summary>
    public const string WindowTitle = "Updates";

    /// <summary>How long the editor has to end after handing over to the updater before it's ended for it: the updater waits a minute at most.</summary>
    public static readonly TimeSpan ExitDeadline = TimeSpan.FromSeconds(20);

    /// <summary>The version an update just restarted the editor into, set by <see cref="Program"/> as it starts.</summary>
    public static string? RestartedInto { get; set; }

    /// <summary>Set once the editor is ending so the updater can put an update in place: <see cref="Program"/> then ends the process outright.</summary>
    public static bool Restarting { get; private set; }

    private Task<(FoundUpdate? Found, string Message)>? _downloading;
    private FoundUpdate? _ready;
    private bool _asking;
    private UpdatesWindow? _window;
    private string? _activity;
    private bool _isBusy;
    private int? _downloadPercent;

    /// <summary>Whether this Griff was installed by Griff's installer, and so can update itself.</summary>
    public bool IsInstalled => updater.InstalledVersion is not null;

    /// <summary>The version installed, or the one this build was made as (null for a build from source).</summary>
    public string? Version => updater.InstalledVersion?.ToString() ?? AppVersion.Release;

    /// <summary>The newer version downloaded and waiting for a restart, if there is one.</summary>
    public string? Ready => _ready?.Version.ToString();

    /// <summary>
    /// What it's doing ("Checking for updates...", "Downloading Griff 0.1.1...") or what came of the last check ("Griff 0.1.0
    /// is up to date.", or why it couldn't check): the Updates window's text. Null before any check.
    /// </summary>
    public string? Activity
    {
        get => _activity;
        private set => SetProperty(ref _activity, value);
    }

    /// <summary>Whether it's checking or downloading now (the Updates window's bar moves while it is).</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>How much of the update is downloaded, 0 to 100, while it downloads; null while that isn't known.</summary>
    public int? DownloadPercent
    {
        get => _downloadPercent;
        private set
        {
            if (SetProperty(ref _downloadPercent, value))
                OnPropertyChanged(nameof(IsPercentUnknown));
        }
    }

    /// <summary>Whether how far it's got isn't known (checking, or a download that hasn't said yet): the bar just moves.</summary>
    public bool IsPercentUnknown => DownloadPercent is null;

    /// <summary>Whether the Updates window is showing.</summary>
    public bool IsWindowOpen => _window is not null;

    /// <summary>Whether it looks for an update by itself after the editor starts (Help > Check for Updates Automatically). Kept.</summary>
    public bool ChecksAutomatically
    {
        get => settings.Settings.CheckForUpdates;
        set
        {
            settings.Settings.CheckForUpdates = value;
            settings.Save();
        }
    }

    /// <summary>Opens the Updates window (or brings it forward), as Help > Check for Updates does; tests put a stand-in here.</summary>
    internal Action? ShowWindow { get; set; }

    /// <summary>
    /// Ends the editor once the user has agreed to restart into an update (and said what to do with unsaved work) and the
    /// updater is waiting for it to end. Closing the main window ends the application; Shutdown makes sure nothing else
    /// holds it open, <see cref="Program"/> ends the process as soon as the application has, and should anything still hold
    /// it after <see cref="ExitDeadline"/>, it's ended then, before the updater gives up waiting.
    /// </summary>
    internal Action Exit { get; set; } = () =>
    {
        Restarting = true;
        _ = Task.Delay(ExitDeadline).ContinueWith(_ => Environment.Exit(0), TaskScheduler.Default);
        editorWindow.CloseReason ??= "restarting into an update";
        if (editorWindow.Main is not null)
            editorWindow.CloseConfirmed();
        Application.Current?.Shutdown();
    };

    /// <summary>Closes the Updates window, if it's showing. A check or download in progress carries on.</summary>
    public void CloseWindow() => _window?.Close();

    /// <summary>
    /// What it does as the editor starts: says so if an update has just been put in place, then (for an installed Griff,
    /// with automatic checks on) waits <paramref name="delay"/> and checks, quietly unless there's an update.
    /// </summary>
    public async Task CheckAfterStartAsync(TimeSpan delay)
    {
        if (RestartedInto is { } updated)
            status.Show($"Updated to Griff {updated}");
        if (!IsInstalled || !ChecksAutomatically)
            return;
        await Task.Delay(delay);
        if (ChecksAutomatically)
            await CheckAsync(userAsked: false);
    }

    /// <summary>
    /// Looks for a newer release, downloads it and asks whether to restart into it; returns what came of it. What the user
    /// asked for (<paramref name="userAsked"/>) opens the Updates window, which shows each step and the outcome, and says
    /// the outcome in the status bar too; the check after starting says nothing unless there's an update. A check asked for
    /// while another is downloading waits for that download, then asks itself (so an agent's call answers it).
    /// </summary>
    public async Task<string> CheckAsync(bool userAsked)
    {
        if (userAsked)
            (ShowWindow ?? OpenWindow)();

        if (!IsInstalled)
            return Finish(userAsked, AppVersion.Release is null
                ? "This is a build of Griff from source: it doesn't update itself. Pull and build again for a newer one."
                : $"This Griff {AppVersion.Release} wasn't installed with the installer, so it doesn't update itself: get a newer one from https://griff.games/releases/.");

        if (_ready is null)
        {
            if (_downloading is not { IsCompleted: false })
                _downloading = DownloadAsync(userAsked);
            var (found, message) = await _downloading;
            if (found is null)
                return Finish(userAsked, message);
            _ready = found;
        }

        if (_asking)
            return $"Griff {_ready.Version} is downloaded, and the editor is asking whether to restart into it now.";
        _asking = true;
        try
        {
            return await OfferRestartAsync(_ready, userAsked);
        }
        finally
        {
            _asking = false;
        }
    }

    // Finds the newest release and downloads it: it, or what to say instead.
    private async Task<(FoundUpdate? Found, string Message)> DownloadAsync(bool userAsked)
    {
        // Velopack says how far it's got from its own thread: the window hears it on the editor's.
        var dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            Busy("Checking for updates...", userAsked);
            var found = await Task.Run(FindAsync);
            if (found is null)
                return (null, $"Griff {Version} is up to date.");

            Busy($"Downloading Griff {found.Version}...", say: true);
            await Task.Run(() => updater.DownloadAsync(found, percent => dispatcher.BeginInvoke(() =>
            {
                if (IsBusy)
                    DownloadPercent = Math.Clamp(percent, 0, 100);
            })));
            return (found, "");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Checking for updates failed");
            return (null, $"Couldn't check for updates: {ex.Message}");
        }
    }

    // The newest release newer than this one on the channels this Griff takes updates from.
    private async Task<FoundUpdate?> FindAsync()
    {
        FoundUpdate? newest = null;
        Exception? failed = null;
        var looked = 0;
        foreach (var channel in UpdateChannels.For(updater.Channel))
        {
            try
            {
                if (await updater.FindAsync(channel) is { } found && (newest is null || found.Version > newest.Version))
                    newest = found;
                looked++;
            }
            catch (Exception ex)
            {
                // A channel with no releases yet (stable, before the first) may have no feed to read.
                logger.LogInformation("No updates read from the {Channel} channel: {Message}", channel, ex.Message);
                failed ??= ex;
            }
        }

        if (looked == 0 && failed is not null)
            throw failed;
        return newest;
    }

    private async Task<string> OfferRestartAsync(FoundUpdate update, bool userAsked)
    {
        // A question in the middle of a game being played can wait for it to stop.
        while (!userAsked && playSession.IsPlaying)
            await Task.Delay(TimeSpan.FromSeconds(2));

        Finish(false, $"Griff {update.Version} is downloaded.");
        var later = $"Griff {update.Version} is downloaded: it's used the next time Griff starts.";
        if (!dialogs.Confirm(ReadyTitle, $"Griff {update.Version} is downloaded. Restart Griff now to use it?\n\nNo: it's used the next time Griff starts."))
            return Finish(true, later);
        // Unsaved changes are asked about as closing the editor asks; cancelling keeps the editor open.
        if (!await projectSession.ConfirmCloseProjectAsync())
            return Finish(true, later);

        Busy($"Restarting into Griff {update.Version}...", say: true);
        try
        {
            updater.ApplyOnExitAndRestart(update);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Starting the updater failed");
            return Finish(true, $"Couldn't restart into Griff {update.Version}: {ex.Message} It's used the next time Griff starts.");
        }

        Exit();
        return $"Restarting into Griff {update.Version}.";
    }

    private void OpenWindow()
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        _window = new UpdatesWindow { DataContext = this, Title = WindowTitle, Owner = editorWindow.Main };
        _window.Closed += (_, _) =>
        {
            _window = null;
            OnPropertyChanged(nameof(IsWindowOpen));
        };
        _window.Show();
        OnPropertyChanged(nameof(IsWindowOpen));
    }

    // Says what it's doing now (in the status bar too when <paramref name="say"/>), with the bar moving.
    private void Busy(string activity, bool say)
    {
        DownloadPercent = null;
        Activity = activity;
        IsBusy = true;
        if (say)
            status.Show(activity);
    }

    // Says what came of it (in the status bar too when <paramref name="say"/>), and gives it back.
    private string Finish(bool say, string message)
    {
        IsBusy = false;
        DownloadPercent = null;
        Activity = message;
        if (say)
            status.Show(message);
        return message;
    }
}
