using System.IO;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Griff.Editor.Features.Updates;

/// <summary>
/// Where updates come from and how one is put in place: Velopack, with Griff's GitHub Releases (see
/// <see cref="VelopackUpdater"/>), or a stand-in in tests.
/// </summary>
public interface IUpdater
{
    /// <summary>
    /// The version this Griff was installed as, or null when Griff's installer didn't install it (a build from source, the
    /// portable zip): then it never updates itself.
    /// </summary>
    SemanticVersion? InstalledVersion { get; }

    /// <summary>The channel it was installed from (<see cref="UpdateChannels.Prerelease"/> or <see cref="UpdateChannels.Stable"/>).</summary>
    string? Channel { get; }

    /// <summary>The newest release on <paramref name="channel"/>, if it's newer than this one; null if none is.</summary>
    Task<FoundUpdate?> FindAsync(string channel);

    /// <summary>
    /// Downloads <paramref name="update"/> beside the installed editor, ready to be put in place, saying how far it's got
    /// (0 to 100) to <paramref name="progress"/> as it goes, from any thread.
    /// </summary>
    Task DownloadAsync(FoundUpdate update, Action<int> progress);

    /// <summary>
    /// Starts the updater, which waits for this process to end, puts a downloaded update in place and starts Griff again.
    /// The editor must then end within a minute (the updater gives up after that). Throws if the updater can't be started.
    /// </summary>
    void ApplyOnExitAndRestart(FoundUpdate update);
}

/// <summary>A release newer than this one: its version, and what the <see cref="IUpdater"/> that found it needs to fetch it.</summary>
public sealed record FoundUpdate(SemanticVersion Version, object Source);

/// <summary>
/// The release channels. A pre-release (a tag like v0.1.0-alpha.1) is packed on <see cref="Prerelease"/>, a release on
/// <see cref="Stable"/>. A Griff installed from a pre-release looks on both, so it moves to the first stable release that's
/// newer; one installed from a release only looks on <see cref="Stable"/>.
/// </summary>
public static class UpdateChannels
{
    public const string Prerelease = "prerelease";
    public const string Stable = "stable";

    /// <summary>The channels a Griff installed from <paramref name="installed"/> takes updates from.</summary>
    public static IReadOnlyList<string> For(string? installed) =>
        installed == Prerelease ? [Prerelease, Stable] : [installed ?? Stable];
}

/// <summary>
/// Updates from Griff's GitHub Releases, with Velopack. GRIFF_UPDATE_SOURCE names another place to look instead (a folder
/// that <c>vpk pack</c> wrote, or a URL to one), to try an update without publishing it.
/// </summary>
public sealed class VelopackUpdater : IUpdater
{
    /// <summary>The repository whose releases hold the installer and the update packages.</summary>
    public const string Repository = "https://github.com/srakowski/griff";

    /// <summary>Names a folder or URL to take updates from instead of GitHub (for trying them locally).</summary>
    public const string SourceVariable = "GRIFF_UPDATE_SOURCE";

    // Set only when the editor started through Program.Main, which runs Velopack first (never in tests).
    private readonly IVelopackLocator? _locator = VelopackLocator.IsCurrentSet ? VelopackLocator.Current : null;

    public SemanticVersion? InstalledVersion => _locator?.CurrentlyInstalledVersion;

    public string? Channel => _locator?.Channel;

    public async Task<FoundUpdate?> FindAsync(string channel)
    {
        var manager = new UpdateManager(Source(channel), new UpdateOptions
        {
            ExplicitChannel = channel,
            // A delta on another channel starts from that channel's last release, not this one: take the whole package.
            MaximumDeltasBeforeFallback = channel == Channel ? null : -1,
        }, _locator);
        var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return info is null || info.IsDowngrade ? null : new FoundUpdate(info.TargetFullRelease.Version, new Found(manager, info));
    }

    public Task DownloadAsync(FoundUpdate update, Action<int> progress)
    {
        var (manager, info) = (Found)update.Source;
        return manager.DownloadUpdatesAsync(info, progress);
    }

    public void ApplyOnExitAndRestart(FoundUpdate update)
    {
        var (manager, info) = (Found)update.Source;
        // Update.exe apply --waitPid: it waits (a minute at most) for the editor to end, then shows its progress while it
        // puts the update in place, and starts the new version. The editor ends itself (EditorUpdates.Exit) right after.
        manager.WaitExitThenApplyUpdates(info.TargetFullRelease, silent: false, restart: true);
    }

    private static IUpdateSource Source(string channel) => Environment.GetEnvironmentVariable(SourceVariable) is { Length: > 0 } other
        ? Uri.TryCreate(other, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? new SimpleWebSource(uri)
            : new SimpleFileSource(new DirectoryInfo(other))
        : new GithubSource(Repository, accessToken: null, prerelease: channel == UpdateChannels.Prerelease);

    private sealed record Found(UpdateManager Manager, UpdateInfo Info);
}
