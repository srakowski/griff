using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Updates;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Velopack;

namespace Griff.Editor.Tests;

/// <summary>
/// Help > Check for Updates and the check after starting: an installed Griff finds the newest release on its channels
/// (a pre-release install on both, so it reaches the first newer release), downloads it quietly and asks whether to
/// restart into it now or on the next start; a Griff the installer didn't install never looks; automatic checks can be
/// turned off and stay off. Asking opens the Updates window, which shows the check and the download as they go, then what
/// came of it; restarting asks about unsaved changes first, then hands over to the updater and ends the editor.
/// </summary>
public sealed class UpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // Releases on each channel, by version; what was looked at, downloaded and put in place.
    private sealed class FakeUpdater(string? installed, string? channel) : IUpdater
    {
        public Dictionary<string, string> Newest { get; } = [];
        public HashSet<string> Failing { get; } = [];
        public List<string> Looked { get; } = [];
        public List<string> Downloaded { get; } = [];
        public List<string> Applied { get; } = [];

        // What happened, in order: the update handed to the updater, and the editor ended.
        public List<string> Events { get; } = [];

        // Called as it looks (to see what the editor shows meanwhile); the progress a download reports; a failure to start.
        public Action? Finding { get; set; }
        public int[] Progress { get; set; } = [];
        public TaskCompletionSource? Downloading { get; set; }
        public Exception? ApplyFails { get; set; }

        public SemanticVersion? InstalledVersion => installed is null ? null : SemanticVersion.Parse(installed);

        public string? Channel => channel;

        public Task<FoundUpdate?> FindAsync(string onChannel)
        {
            Looked.Add(onChannel);
            Finding?.Invoke();
            if (Failing.Contains(onChannel))
                throw new InvalidOperationException($"No feed for {onChannel}");
            return Task.FromResult(Newest.TryGetValue(onChannel, out var version) && SemanticVersion.Parse(version) > InstalledVersion!
                ? new FoundUpdate(SemanticVersion.Parse(version), onChannel)
                : null);
        }

        public Task DownloadAsync(FoundUpdate update, Action<int> progress)
        {
            Downloaded.Add(update.Version.ToString());
            foreach (var percent in Progress)
                progress(percent);
            // A download held open until the test says so, as a real one is still going while it reports.
            return Downloading?.Task ?? Task.CompletedTask;
        }

        public void ApplyOnExitAndRestart(FoundUpdate update)
        {
            if (ApplyFails is { } failure)
                throw failure;
            Applied.Add(update.Version.ToString());
            Events.Add("apply");
        }
    }

    private static void Run(FakeUpdater updater, Action<IServiceProvider, EditorUpdates, Func<int>> test) =>
        Run(updater, (sp, updates, exits, windows) => test(sp, updates, exits));

    // The editor's ending and the Updates window opening are counted rather than done.
    private static void Run(FakeUpdater updater, Action<IServiceProvider, EditorUpdates, Func<int>, Func<int>> test) =>
        EditorHost.Run(services => services.AddSingleton<IUpdater>(updater), sp =>
        {
            // As on the editor's UI thread, where a check comes back to after each step.
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var updates = sp.GetRequiredService<EditorUpdates>();
            var (exits, windows) = (0, 0);
            updates.Exit = () =>
            {
                exits++;
                updater.Events.Add("exit");
            };
            updates.ShowWindow = () => windows++;
            test(sp, updates, () => exits, () => windows);
        });

    private static string Check(EditorUpdates updates, string answers)
    {
        using (AgentDialogs.Answering(JsonDocument.Parse(answers).RootElement))
            return EditorHost.Pump(updates.CheckAsync(userAsked: true));
    }

    [Fact]
    public void A_prerelease_install_takes_the_newest_of_both_channels_and_restarts_into_it_when_asked()
    {
        var updater = new FakeUpdater("0.1.0-alpha.1", UpdateChannels.Prerelease);
        updater.Newest[UpdateChannels.Prerelease] = "0.1.0-alpha.2";
        updater.Newest[UpdateChannels.Stable] = "0.1.0";
        Run(updater, (sp, updates, exits) =>
        {
            Assert.True(updates.IsInstalled);
            var outcome = Check(updates, $$"""{"{{EditorUpdates.ReadyTitle}}": true}""");

            Assert.Equal([UpdateChannels.Prerelease, UpdateChannels.Stable], updater.Looked);
            Assert.Equal(["0.1.0"], updater.Downloaded);
            Assert.Equal(["0.1.0"], updater.Applied);
            Assert.Equal(1, exits());
            Assert.Contains("Restarting into Griff 0.1.0", outcome, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Restart_later_keeps_the_download_for_the_next_start_and_asks_again_without_downloading_again()
    {
        var updater = new FakeUpdater("0.1.0-alpha.1", UpdateChannels.Prerelease);
        updater.Newest[UpdateChannels.Prerelease] = "0.1.0-alpha.2";
        // Before the first release, the stable channel has nothing to read.
        updater.Failing.Add(UpdateChannels.Stable);
        Run(updater, (sp, updates, exits) =>
        {
            var later = Check(updates, $$"""{"{{EditorUpdates.ReadyTitle}}": false}""");
            Assert.Contains("used the next time Griff starts", later, StringComparison.Ordinal);
            Assert.Equal("0.1.0-alpha.2", updates.Ready);
            Assert.Empty(updater.Applied);
            Assert.Equal(0, exits());
            Assert.Equal(later, sp.GetRequiredService<IStatusService>().Message);

            Check(updates, $$"""{"{{EditorUpdates.ReadyTitle}}": true}""");
            Assert.Equal(["0.1.0-alpha.2"], updater.Downloaded);
            Assert.Equal(["0.1.0-alpha.2"], updater.Applied);
            Assert.Equal(1, exits());
        });
    }

    [Fact]
    public void A_stable_install_looks_only_at_stable_releases_and_says_when_it_is_up_to_date()
    {
        var updater = new FakeUpdater("0.1.0", UpdateChannels.Stable);
        updater.Newest[UpdateChannels.Prerelease] = "0.2.0-alpha.1";
        Run(updater, (sp, updates, exits) =>
        {
            var outcome = Check(updates, "{}");

            Assert.Equal([UpdateChannels.Stable], updater.Looked);
            Assert.Empty(updater.Downloaded);
            Assert.Equal("Griff 0.1.0 is up to date.", outcome);
            Assert.Equal(outcome, sp.GetRequiredService<IStatusService>().Message);
        });
    }

    [Fact]
    public void A_failed_check_says_so_and_changes_nothing()
    {
        var updater = new FakeUpdater("0.1.0", UpdateChannels.Stable);
        updater.Failing.Add(UpdateChannels.Stable);
        Run(updater, (sp, updates, exits) =>
        {
            var outcome = Check(updates, "{}");
            Assert.StartsWith("Couldn't check for updates", outcome, StringComparison.Ordinal);
            Assert.Empty(updater.Downloaded);
            Assert.Null(updates.Ready);
        });
    }

    [Fact]
    public void A_griff_the_installer_did_not_install_never_looks()
    {
        var updater = new FakeUpdater(null, null);
        Run(updater, (sp, updates, exits) =>
        {
            Assert.False(updates.IsInstalled);
            var command = sp.GetServices<EditorCommand>().Single(c => c.Id == "Help.CheckForUpdates");
            EditorHost.Pump(command.ExecuteAsync());
            EditorHost.Pump(updates.CheckAfterStartAsync(TimeSpan.Zero));

            Assert.Empty(updater.Looked);
            Assert.Contains("doesn't update itself", command.Outcome, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Automatic_checks_run_after_the_start_quietly_and_can_be_turned_off_for_good()
    {
        var updater = new FakeUpdater("0.1.0", UpdateChannels.Stable);
        Run(updater, (sp, updates, exits) =>
        {
            var status = sp.GetRequiredService<IStatusService>();
            Assert.True(updates.ChecksAutomatically);
            EditorHost.Pump(updates.CheckAfterStartAsync(TimeSpan.Zero));
            Assert.Equal([UpdateChannels.Stable], updater.Looked);
            // Nothing new: nothing said.
            Assert.Equal("Ready", status.Message);

            var toggle = sp.GetServices<EditorCommand>().Single(c => c.Id == "Help.CheckForUpdatesAutomatically");
            Assert.True(toggle.IsChecked);
            EditorHost.Pump(toggle.ExecuteAsync());
            Assert.False(toggle.IsChecked);
            Assert.False(sp.GetRequiredService<ISettingsStore>().Settings.CheckForUpdates);

            EditorHost.Pump(updates.CheckAfterStartAsync(TimeSpan.Zero));
            Assert.Single(updater.Looked);
        });
    }

    [Fact]
    public void Checking_shows_the_window_at_once_busy_checking_then_the_outcome()
    {
        var updater = new FakeUpdater("0.1.0", UpdateChannels.Stable);
        Run(updater, (sp, updates, exits, windows) =>
        {
            (int Windows, bool Busy, string? Activity)? whileLooking = null;
            updater.Finding = () => whileLooking = (windows(), updates.IsBusy, updates.Activity);

            var outcome = Check(updates, "{}");

            Assert.Equal((1, true, "Checking for updates..."), whileLooking);
            Assert.Equal("Griff 0.1.0 is up to date.", outcome);
            Assert.Equal(outcome, updates.Activity);
            Assert.False(updates.IsBusy);
            Assert.Equal(1, windows());
        });
    }

    [Fact]
    public void A_download_shows_how_far_it_has_got_then_offers_the_restart()
    {
        var updater = new FakeUpdater("0.1.0", UpdateChannels.Stable) { Progress = [0, 40, 100], Downloading = new() };
        updater.Newest[UpdateChannels.Stable] = "0.1.1";
        Run(updater, (sp, updates, exits, windows) =>
        {
            var seen = new List<string>();
            updates.PropertyChanged += (_, e) =>
            {
                seen.Add(e.PropertyName switch
                {
                    nameof(EditorUpdates.Activity) => $"{updates.Activity}",
                    nameof(EditorUpdates.DownloadPercent) => $"{(updates.DownloadPercent is { } percent ? $"{percent}" : "unknown")}%",
                    _ => "",
                });
                // The download ends once the window has shown it all.
                if (updates.DownloadPercent == 100)
                    updater.Downloading.TrySetResult();
            };

            var later = Check(updates, $$"""{"{{EditorUpdates.ReadyTitle}}": false}""");

            Assert.Equal(
                ["Checking for updates...", "Downloading Griff 0.1.1...", "0%", "40%", "100%", "unknown%", "Griff 0.1.1 is downloaded.", later],
                seen.Where(s => s.Length > 0));
            Assert.Equal("Griff 0.1.1 is downloaded: it's used the next time Griff starts.", later);
            Assert.False(updates.IsBusy);
            Assert.Equal(0, exits());
        });
    }

    [Fact]
    public void A_failed_check_shows_why_in_the_window()
    {
        var updater = new FakeUpdater("0.1.0", UpdateChannels.Stable);
        updater.Failing.Add(UpdateChannels.Stable);
        Run(updater, (sp, updates, exits, windows) =>
        {
            Check(updates, "{}");
            Assert.Equal(1, windows());
            Assert.False(updates.IsBusy);
            Assert.Equal("Couldn't check for updates: No feed for stable", updates.Activity);
        });
    }

    [Fact]
    public void The_check_after_starting_shows_no_window()
    {
        var updater = new FakeUpdater("0.1.0", UpdateChannels.Stable);
        Run(updater, (sp, updates, exits, windows) =>
        {
            EditorHost.Pump(updates.CheckAfterStartAsync(TimeSpan.Zero));
            Assert.Equal(0, windows());
        });
    }

    [Fact]
    public void Restarting_hands_the_update_to_the_updater_then_ends_the_editor()
    {
        var updater = new FakeUpdater("0.1.0", UpdateChannels.Stable);
        updater.Newest[UpdateChannels.Stable] = "0.1.1";
        Run(updater, (sp, updates, exits, windows) =>
        {
            var outcome = Check(updates, $$"""{"{{EditorUpdates.ReadyTitle}}": true}""");
            Assert.Equal(["apply", "exit"], updater.Events);
            Assert.Equal("Restarting into Griff 0.1.1.", outcome);
            Assert.Equal("Restarting into Griff 0.1.1...", updates.Activity);
        });
    }

    [Fact]
    public void Cancelling_the_question_about_unsaved_changes_stops_the_restart()
    {
        var updater = new FakeUpdater("0.1.0", UpdateChannels.Stable);
        updater.Newest[UpdateChannels.Stable] = "0.1.1";
        Run(updater, (sp, updates, exits, windows) =>
        {
            var workspace = sp.GetRequiredService<Workspace>();
            EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
            workspace.Scene!.Execute(new AddEntityEdit(workspace.Scene.Scene, new Entity("Unsaved")));

            var outcome = Check(updates, $$"""{"{{EditorUpdates.ReadyTitle}}": true, "Save changes": "Cancel"}""");

            Assert.Empty(updater.Applied);
            Assert.Equal(0, exits());
            Assert.True(workspace.Scene!.IsDirty);
            Assert.Equal("Griff 0.1.1 is downloaded: it's used the next time Griff starts.", outcome);
            Assert.Equal("0.1.1", updates.Ready);
        });
    }

    [Fact]
    public void An_updater_that_will_not_start_keeps_the_editor_open_and_says_why()
    {
        var updater = new FakeUpdater("0.1.0", UpdateChannels.Stable) { ApplyFails = new FileNotFoundException("Cannot find Update.exe to apply updates.") };
        updater.Newest[UpdateChannels.Stable] = "0.1.1";
        Run(updater, (sp, updates, exits, windows) =>
        {
            var outcome = Check(updates, $$"""{"{{EditorUpdates.ReadyTitle}}": true}""");
            Assert.Equal(0, exits());
            Assert.StartsWith("Couldn't restart into Griff 0.1.1: Cannot find Update.exe", outcome, StringComparison.Ordinal);
            Assert.Equal(outcome, updates.Activity);
            Assert.False(updates.IsBusy);
        });
    }

    [Theory]
    [InlineData("0.1.0-alpha.1+3f2c1a9e", "0.1.0-alpha.1")]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("1.0.0-dev+3f2c1a9e", null)]
    [InlineData("1.0.0-dev", null)]
    [InlineData(null, null)]
    public void The_version_is_the_release_a_build_was_made_as(string? informational, string? version) =>
        Assert.Equal(version, AppVersion.FromInformational(informational));

    [Fact]
    public void A_prerelease_install_takes_both_channels_and_a_release_only_stable() =>
        Assert.Multiple(
            () => Assert.Equal([UpdateChannels.Prerelease, UpdateChannels.Stable], UpdateChannels.For(UpdateChannels.Prerelease)),
            () => Assert.Equal([UpdateChannels.Stable], UpdateChannels.For(UpdateChannels.Stable)));
}
