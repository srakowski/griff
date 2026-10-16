using System.Windows;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Updates;

/// <summary>
/// Keeps an installed Griff up to date. A while after the editor starts (unless <see cref="ChecksAutomatically"/> is off)
/// and whenever Help > Check for Updates is picked, it looks for a newer release, downloads it quietly, then asks whether
/// to restart into it now or on the next start. A Griff that wasn't installed by the installer (a build from source, the
/// portable zip) never looks: it says how to get a newer one instead.
/// </summary>
public sealed class EditorUpdates(IUpdater updater, ISettingsStore settings, IStatusService status, IDialogService dialogs, IProjectSession projectSession, IPlaySession playSession,
    ILogger<EditorUpdates> logger, Core.EditorWindow editorWindow)
{
    /// <summary>How long after the editor starts it first looks for an update, so starting never waits on it.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    /// <summary>The question asked once an update is downloaded (an agent answers it by this title, true to restart now).</summary>
    public const string ReadyTitle = "Update ready";

    /// <summary>The version an update just restarted the editor into, set by <see cref="Program"/> as it starts.</summary>
    public static string? RestartedInto { get; set; }

    private Task<(FoundUpdate? Found, string Message)>? _downloading;
    private FoundUpdate? _ready;
    private bool _asking;

    /// <summary>Whether this Griff was installed by Griff's installer, and so can update itself.</summary>
    public bool IsInstalled => updater.InstalledVersion is not null;

    /// <summary>The version installed, or the one this build was made as (null for a build from source).</summary>
    public string? Version => updater.InstalledVersion?.ToString() ?? AppVersion.Release;

    /// <summary>The newer version downloaded and waiting for a restart, if there is one.</summary>
    public string? Ready => _ready?.Version.ToString();

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

    /// <summary>Shuts the editor once the user has agreed to restart into an update (and said what to do with unsaved work).</summary>
    internal Action Exit { get; set; } = () =>
    {
        if (editorWindow.Main is not null)
            editorWindow.CloseConfirmed();
        else
            Application.Current?.Shutdown();
    };

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
    /// Looks for a newer release, downloads it and asks whether to restart into it; returns what came of it. Only what the user
    /// asked for (<paramref name="userAsked"/>) is said in the status bar when there's no update or the check fails. A check
    /// asked for while another is downloading waits for that download, then asks itself (so an agent's call answers it).
    /// </summary>
    public async Task<string> CheckAsync(bool userAsked)
    {
        if (!IsInstalled)
            return Say(userAsked, AppVersion.Release is null
                ? "This is a build of Griff from source: it doesn't update itself. Pull and build again for a newer one."
                : $"This Griff {AppVersion.Release} wasn't installed with the installer, so it doesn't update itself: get a newer one from https://griff.games/releases/.");

        if (_ready is null)
        {
            if (_downloading is not { IsCompleted: false })
                _downloading = DownloadAsync(userAsked);
            var (found, message) = await _downloading;
            if (found is null)
                return Say(userAsked, message);
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
        try
        {
            if (userAsked)
                status.Show("Checking for updates...");
            var found = await Task.Run(FindAsync);
            if (found is null)
                return (null, $"Griff {Version} is up to date.");
            status.Show($"Downloading Griff {found.Version}...");
            await Task.Run(() => updater.DownloadAsync(found));
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

        var later = $"Griff {update.Version} is downloaded: it's used the next time Griff starts.";
        if (!dialogs.Confirm(ReadyTitle, $"Griff {update.Version} is downloaded. Restart Griff now to use it?\n\nNo: it's used the next time Griff starts."))
            return Say(true, later);
        if (!await projectSession.ConfirmCloseProjectAsync())
            return Say(true, later);

        updater.ApplyOnExitAndRestart(update);
        status.Show($"Restarting into Griff {update.Version}...");
        Exit();
        return $"Restarting into Griff {update.Version}.";
    }

    private string Say(bool show, string message)
    {
        if (show)
            status.Show(message);
        return message;
    }
}
