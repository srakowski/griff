using System.IO;
using System.Runtime.CompilerServices;
using Griff.Audio;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Settings;
#if DEBUG
using Griff.Editor.Diagnostics.Demo;
#endif
using Griff.Editor.Features.Audio;
using Griff.Editor.Themes;
using Griff.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Griff.Editor.Tests;

/// <summary>
/// Game > Mute, GRIFF_MUTE and the sound device: muted, nothing is heard (the device is never opened, or is closed) while
/// sound plays on at the same pace; the user's own choice is kept between sessions, and a session muted by GRIFF_MUTE
/// (an agent's) never changes it.
/// </summary>
public sealed class MuteTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "griff-mute-" + Guid.NewGuid().ToString("N")[..8]);

    // The editor's tests make no sound: GRIFF_MUTE=1 for the whole run, as an agent sets it (and players they start inherit it).
    [ModuleInitializer]
    internal static void Silence() => Environment.SetEnvironmentVariable(AudioOutput.MuteVariable, "1");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    private JsonSettingsStore Store() => new(NullLogger<JsonSettingsStore>.Instance, SettingsPath);

    // A sound device that's only counted.
    private sealed class FakeDevices
    {
        public int Opened, Closed;

        public SoundDevice? Open(SoundOutput output)
        {
            Opened++;
            return new SoundDevice(new Closer(this), 0.05);
        }

        private sealed class Closer(FakeDevices devices) : IDisposable
        {
            public void Dispose() => devices.Closed++;
        }
    }

    private static SoundClip Beep(double seconds) => new(new float[(int)(seconds * Mixer.SampleRate)], 1, Mixer.SampleRate);

    [Fact]
    public void Tests_run_muted_by_the_environment()
    {
        Assert.True(AudioOutput.MutedByEnvironment);
        Assert.True(AudioOutput.Muted);
        Assert.True(AudioOutput.IsOn("1"));
        Assert.True(AudioOutput.IsOn("true"));
        Assert.False(AudioOutput.IsOn("0"));
        Assert.False(AudioOutput.IsOn(" off "));
        Assert.False(AudioOutput.IsOn(""));
        Assert.False(AudioOutput.IsOn(null));
    }

    [Fact]
    public void Muted_from_the_start_the_device_is_never_opened_and_sounds_still_play_through()
    {
        var devices = new FakeDevices();
        using var output = new SoundOutput(devices.Open, muted: true) { KeepsTime = false };
        output.Start();
        var voice = output.Mixer.Play(Beep(0.1).Open());

        output.Pump(0.05);
        Assert.True(voice.IsPlaying);
        Assert.InRange(voice.Heard, 0.04, 0.1);
        output.Pump(0.1);
        Assert.False(voice.IsPlaying);
        Assert.Equal(0, output.Mixer.Playing);
        Assert.Equal(0, devices.Opened);
        Assert.False(output.IsDeviceOpen);
    }

    [Fact]
    public void Muted_sound_keeps_real_time_on_its_own()
    {
        using var output = new SoundOutput(_ => throw new InvalidOperationException("The device was opened."), muted: true);
        output.Start();
        var voice = output.Mixer.Play(Beep(0.1).Open());
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (voice.IsPlaying && waited.Elapsed < TimeSpan.FromSeconds(5))
            Thread.Sleep(10);
        Assert.False(voice.IsPlaying);
        Assert.InRange(waited.Elapsed.TotalSeconds, 0.05, 5);
    }

    [Fact]
    public void Muting_closes_the_device_and_unmuting_opens_it_again()
    {
        var devices = new FakeDevices();
        using var output = new SoundOutput(devices.Open);
        Assert.Equal(0, devices.Opened);
        output.Start();
        Assert.Equal(1, devices.Opened);
        Assert.True(output.IsDeviceOpen);
        Assert.Equal(0.05, output.Mixer.OutputLatency);

        output.Muted = true;
        Assert.Equal(1, devices.Closed);
        Assert.False(output.IsDeviceOpen);
        Assert.Equal(0, output.Mixer.OutputLatency);

        output.Muted = false;
        Assert.Equal(2, devices.Opened);
        Assert.True(output.IsDeviceOpen);
    }

    [Fact]
    public void Muting_before_the_first_sound_never_opens_the_device()
    {
        var devices = new FakeDevices();
        using var output = new SoundOutput(devices.Open) { Muted = true };
        output.Start();
        output.Muted = true;
        Assert.Equal(0, devices.Opened);
    }

    [Fact]
    public void Game_Mute_is_kept_between_sessions()
    {
        EditorHost.Run(Real(Store), services =>
        {
            var command = services.GetRequiredService<CommandRegistry>().Get("Game.Mute");
            Assert.False(command.IsChecked);
            Assert.Equal(Icons.Volume, command.Icon);
            command.ExecuteAsync().GetAwaiter().GetResult();
            Assert.True(command.IsChecked);
            Assert.True(services.GetRequiredService<EditorMute>().IsMuted);
            Assert.Equal(Icons.Mute, command.Icon);
            Assert.True(services.GetRequiredService<ISettingsStore>().Settings.Mute);
        });

        // A new editor, on the same settings file: still muted.
        EditorHost.Run(Real(Store), services =>
        {
            Assert.True(services.GetRequiredService<ISettingsStore>().Settings.Mute);
            Assert.True(services.GetRequiredService<EditorMute>().IsMuted);
            Assert.True(services.GetRequiredService<CommandRegistry>().Get("Game.Mute").IsChecked);
        });
    }

    [Fact]
    public void Muted_by_GRIFF_MUTE_the_saved_choice_is_left_alone()
    {
        var store = Store();
        var applied = new List<bool>();
        var mute = new EditorMute(store, mutedByEnvironment: true, applied.Add);
        Assert.False(mute.IsMuted);
        Assert.True(mute.IsSilent);
        Assert.Equal([true], applied);

        // Toggled over MCP, say: the switch changes for this session, and the editor stays silent.
        mute.IsMuted = true;
        mute.IsMuted = false;
        Assert.Equal([true, true, true], applied);
        Assert.True(mute.IsSilent);
        Assert.False(store.Settings.Mute);
        Assert.False(File.Exists(SettingsPath));

        // The session ends, saving its other settings: the choice on file is still the user's.
        store.Settings.Transform3D = true;
        store.Save();
        Assert.False(Store().Settings.Mute);
    }

    [Fact]
    public void Muted_by_GRIFF_MUTE_a_user_who_muted_stays_muted_after()
    {
        var user = Store();
        new EditorMute(user, mutedByEnvironment: false, _ => { }).IsMuted = true;
        Assert.True(Store().Settings.Mute);

        // An agent's session turns Mute off for itself; the user's choice stays.
        var agent = new EditorMute(Store(), mutedByEnvironment: true, _ => { });
        Assert.True(agent.IsMuted);
        agent.IsMuted = false;
        Assert.True(agent.IsSilent);
        Assert.True(Store().Settings.Mute);
    }

    [Fact]
    public void An_editor_started_earlier_does_not_undo_a_mute_chosen_since()
    {
        var earlier = Store();
        var user = Store();
        new EditorMute(user, mutedByEnvironment: false, _ => { }).IsMuted = true;

        // The earlier editor saves something else (its layout, say) and closes.
        earlier.Settings.Transform3D = true;
        earlier.Save();

        var now = Store().Settings;
        Assert.True(now.Mute);
        Assert.True(now.Transform3D);
    }

#if DEBUG
    [Fact]
    public void The_narration_choice_is_kept_between_demos_unless_muted_for_one_run()
    {
        var path = Path.Combine(_folder, "demo.json");
        using (var control = new DemoControl())
        {
            DemoPreferences.Attach(control, path, mutedForRun: false);
            Assert.False(control.NarrationMuted);
            control.ToggleNarration();
        }

        using (var control = new DemoControl())
        {
            DemoPreferences.Attach(control, path, mutedForRun: false);
            Assert.True(control.NarrationMuted);
            control.ToggleNarration();
        }

        Assert.False(DemoPreferences.Load(path).MuteNarration);

        // GRIFF_MUTE_NARRATION=1 (demo.ps1 -Mute): muted for this run, and nothing is written.
        using (var control = new DemoControl())
        {
            DemoPreferences.Attach(control, path, mutedForRun: true);
            Assert.True(control.NarrationMuted);
            control.ToggleNarration();
            control.ToggleNarration();
        }

        Assert.False(DemoPreferences.Load(path).MuteNarration);
    }
#endif

    // The editor's services on a settings file in the test's folder, with the mute standing apart from the real device.
    private static Action<IServiceCollection> Real(Func<JsonSettingsStore> store) => services =>
    {
        services.AddSingleton<ISettingsStore>(_ => store());
        services.AddSingleton(s => new EditorMute(s.GetRequiredService<ISettingsStore>(), mutedByEnvironment: false, _ => { }));
    };
}
