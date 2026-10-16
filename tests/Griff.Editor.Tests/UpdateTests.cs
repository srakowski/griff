using System.Text.Json;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Updates;
using Microsoft.Extensions.DependencyInjection;
using Velopack;

namespace Griff.Editor.Tests;

/// <summary>
/// Help > Check for Updates and the check after starting: an installed Griff finds the newest release on its channels
/// (a pre-release install on both, so it reaches the first newer release), downloads it quietly and asks whether to
/// restart into it now or on the next start; a Griff the installer didn't install never looks; automatic checks can be
/// turned off and stay off.
/// </summary>
public sealed class UpdateTests
{
    // Releases on each channel, by version; what was looked at, downloaded and put in place.
    private sealed class FakeUpdater(string? installed, string? channel) : IUpdater
    {
        public Dictionary<string, string> Newest { get; } = [];
        public HashSet<string> Failing { get; } = [];
        public List<string> Looked { get; } = [];
        public List<string> Downloaded { get; } = [];
        public List<string> Applied { get; } = [];

        public SemanticVersion? InstalledVersion => installed is null ? null : SemanticVersion.Parse(installed);

        public string? Channel => channel;

        public Task<FoundUpdate?> FindAsync(string onChannel)
        {
            Looked.Add(onChannel);
            if (Failing.Contains(onChannel))
                throw new InvalidOperationException($"No feed for {onChannel}");
            return Task.FromResult(Newest.TryGetValue(onChannel, out var version) && SemanticVersion.Parse(version) > InstalledVersion!
                ? new FoundUpdate(SemanticVersion.Parse(version), onChannel)
                : null);
        }

        public Task DownloadAsync(FoundUpdate update)
        {
            Downloaded.Add(update.Version.ToString());
            return Task.CompletedTask;
        }

        public void ApplyOnExitAndRestart(FoundUpdate update) => Applied.Add(update.Version.ToString());
    }

    private static void Run(FakeUpdater updater, Action<IServiceProvider, EditorUpdates, Func<int>> test) =>
        EditorHost.Run(services => services.AddSingleton<IUpdater>(updater), sp =>
        {
            var updates = sp.GetRequiredService<EditorUpdates>();
            var exits = 0;
            updates.Exit = () => exits++;
            test(sp, updates, () => exits);
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
