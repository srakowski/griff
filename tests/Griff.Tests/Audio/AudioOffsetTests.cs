using System.Numerics;
using Griff.Audio;
using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Audio;

/// <summary>
/// The player's audio and visual offsets (how late they hear the sound and see the screen): kept with their settings,
/// allowed for by beat clocks, and measured by a Calibration they tap along to.
/// </summary>
public class AudioOffsetTests
{
    private const float Step = 1 / 60f;

    [Fact]
    public void TheOffsets_AreThePlayersSettings_KeptInTheirSaveData_AndKeptInRange()
    {
        var game = new Game(new Scene(), new InputState());
        Assert.Equal(0, game.AudioOffset);

        game.AudioOffset = 180;
        game.VisualOffset = -40;
        Assert.Equal(180, game.SaveData.GetFloat(Game.AudioOffsetSaveKey));
        Assert.Equal(-40, game.SaveData.GetFloat(Game.VisualOffsetSaveKey));

        // The next time the game starts, they're back.
        var next = new Game(new Scene(), new InputState()) { SaveData = game.SaveData };
        Assert.Equal((180f, -40f), (next.AudioOffset, next.VisualOffset));

        next.AudioOffset = 5000;
        next.VisualOffset = float.NaN;
        Assert.Equal((Game.MaxOffset, 0f), (next.AudioOffset, next.VisualOffset));
    }

    [Fact]
    public void ASlider_BoundToTheAudioOffset_SetsItInMilliseconds_AndShowsIt()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        scene.Add(camera);
        var entity = new Entity("Offset");
        var slider = entity.AddComponent<Slider>();
        (slider.Min, slider.Max, slider.Step, slider.Setting) = (-100, 400, 5, GameSetting.AudioOffset);
        scene.Add(entity);
        var readout = new Entity("Readout");
        var text = readout.AddComponent<Text>();
        (text.Value, text.Output) = ("{0:0} ms", "Value");
        scene.Add(readout, entity);
        var input = new InputState();
        var game = new Game(scene, input) { AudioOffset = 120 };

        Tick(game, input);
        Assert.Equal(120, slider.Value);
        Assert.Equal("120 ms", text.Shown);

        // The keys move it a step at a time.
        game.Focus(slider);
        Press(game, input, Key.Right);
        Assert.Equal(125, game.AudioOffset);
        Assert.Equal(125, game.SaveData.GetFloat(Game.AudioOffsetSaveKey));
        Tick(game, input);
        Assert.Equal("125 ms", text.Shown);
    }

    [Fact]
    public void ABeatClock_CountsTheBeatsAsThePlayerHearsThem_LaterByTheirAudioOffset()
    {
        var (game, audio, clock, beats) = Setup();
        game.AudioOffset = 200;

        Run(game, 1.3f);

        // 120 beats a minute: heard at 0.2, 0.7 and 1.2 seconds rather than 0, 0.5 and 1.
        Assert.Equal([0, 1, 2], beats.Select(b => b.Beat));
        foreach (var (beat, time) in beats)
            Assert.InRange(time - (beat * 0.5 + 0.2), -1e-4, Step + 1e-4);
        Assert.Equal(audio.MusicTime - Step - 0.2, clock.Position, 3);

        // A press as beat 2 is heard is on time.
        Assert.InRange(clock.SecondsFrom(2), 0.08f, 0.12f);
        Assert.Equal(clock.SecondsFromBeat, clock.SecondsFrom(2), 4);
    }

    [Fact]
    public void ABeatClock_WhoseOffsetIsMadeEarlier_LandsNoBeatTwice()
    {
        var (game, _, clock, beats) = Setup();
        Run(game, 1.1f);
        Assert.Equal([0, 1, 2], beats.Select(b => b.Beat));

        // Taken back 0.3 seconds: beat 2 comes round again, but it has landed already.
        game.AudioOffset = 300;
        game.Tick(Step);
        Assert.True(clock.Beats < 2);
        Run(game, 0.6f);
        Assert.Equal([0, 1, 2], beats.Select(b => b.Beat));
        Run(game, 0.2f);
        Assert.Equal([0, 1, 2, 3], beats.Select(b => b.Beat));
    }

    [Fact]
    public void WhatsDrawnToTheBeat_IsAheadByTheVisualOffset_AndOnlyThat()
    {
        var (game, _, clock, beats) = Setup();
        var speaker = new Entity("Speaker");
        speaker.AddComponent<Sprite>();
        var pulse = speaker.AddComponent<BeatPulse>();
        pulse.Size = 0.5f;
        game.Scene.Add(speaker);
        game.VisualOffset = 100;

        // A tenth of a second before beat 1 is heard, it's drawn bopping, to be seen as it's heard.
        Run(game, 0.44f);
        Assert.Equal([0], beats.Select(b => b.Beat));
        Assert.InRange(clock.ShownBeats - clock.Beats, 0.19f, 0.21f);
        Assert.InRange(pulse.Scale, 1.4f, 1.5f);
    }

    [Fact]
    public void TheMetronome_ClicksOnTheBeat_AsMusic_AtTheSoundVolume()
    {
        var bar = Metronome.Bar(100, 4);
        Assert.Equal(2.4, bar.Seconds, 3);
        Assert.Same(bar, Metronome.Bar(100, 4));
        for (var beat = 0; beat < 4; beat++)
        {
            var start = (int)Math.Round(beat * 0.6 * Mixer.SampleRate);
            Assert.True(Math.Abs(bar.Samples[start]) > 0.5f, $"beat {beat} doesn't click at once");
            if (beat > 0)
                Assert.Equal(0, bar.Samples[start - 10]);
        }

        var now = 0.0;
        var mixer = new Mixer { Clock = () => now };
        var audio = new MixerAudio(mixer, new NoSounds()) { MusicVolume = 0, SoundVolume = 0.5f };
        audio.PlayMetronome(100, 4, 0.8f);
        Assert.Equal(IAudio.MetronomeMusic, audio.Music);
        Assert.Equal(100, audio.MusicTempo);
        Assert.Equal(1, mixer.Playing);
        var output = new float[2048];
        mixer.Mix(output);
        Assert.InRange(output.Max(Math.Abs), 0.25f, 0.29f);

        // Silent audio keeps its time, as for any music.
        var silent = new SilentAudio { MusicTempo = 90 };
        silent.PlayMetronome(100);
        silent.Advance(0.5f);
        Assert.Equal((IAudio.MetronomeMusic, 100f, 0.5), (silent.Music, silent.MusicTempo, silent.MusicTime));
        silent.StopMusic();
        Assert.Equal(90, silent.MusicTempo);
    }

    [Fact]
    public void ACalibration_TimesTapsAlongToItsClicks_AndOffersHowLateTheyCame()
    {
        var (game, input, audio, calibration) = Calibrating();
        var apply = game.Find("Apply")!;
        var started = 0;
        calibration.Finished += _ => started++;

        // It starts as it's shown, the clicks in place of the music, its buttons out of the way.
        Tick(game, input);
        Assert.Equal(CalibrationState.Listening, calibration.State);
        Assert.Equal(IAudio.MetronomeMusic, audio.Music);
        Assert.Equal(100, audio.MusicTempo);
        Assert.False(apply.IsEnabled);
        Assert.Contains("Listen to the clicks", calibration.Prompt);

        TapAlong(game, input, audio, late: 0.15, Key.Space);

        Assert.Equal(1, started);
        Assert.Equal(CalibrationState.Measured, calibration.State);
        Assert.Equal(8, calibration.Taps);
        Assert.InRange(calibration.Result, 145, 155);
        Assert.InRange(calibration.Spread, 0, 5);
        Assert.Contains("ms after the clicks", calibration.Prompt);
        Assert.Equal("Music/theme.wav", audio.Music);
        Assert.True(apply.IsEnabled);
        Assert.Equal(0, game.AudioOffset);

        // Apply makes it the player's offset, kept with their settings.
        game.Do(calibration.Entity!, "Apply");
        Assert.Equal(calibration.Result, game.AudioOffset);
        Assert.Equal(calibration.Result, game.SaveData.GetFloat(Game.AudioOffsetSaveKey));
        Assert.True(calibration.Applied);
        Tick(game, input);
        Assert.Equal(calibration.Result, calibration.Current);
        Assert.StartsWith("Your audio offset is now", calibration.Prompt);
    }

    [Fact]
    public void ACalibration_LeavesOutTheOddTaps_AndTakesClicksAndTheGamepad()
    {
        var (game, input, audio, calibration) = Calibrating();
        Tick(game, input);

        // Two taps far off the rest (a late start, a double tap) don't move the result.
        var odd = new Dictionary<int, double> { [5] = 0.4, [2] = -0.1 };
        TapAlong(game, input, audio, late: 0.08, Key.None, odd, gamepad: true);

        Assert.Equal(CalibrationState.Measured, calibration.State);
        Assert.Equal(6, calibration.Taps);
        Assert.InRange(calibration.Result, 75, 85);

        // Again, with the mouse (or a finger).
        game.Do(calibration.Entity!, "Start");
        Tick(game, input);
        Assert.Equal(CalibrationState.Listening, calibration.State);
        TapAlong(game, input, audio, late: 0.25, Key.None, mouse: true);
        Assert.InRange(calibration.Result, 245, 255);
    }

    [Fact]
    public void ACalibration_WithTooFewSteadyTaps_SaysToTryAgain()
    {
        var (game, input, audio, calibration) = Calibrating();
        Tick(game, input);

        TapAlong(game, input, audio, late: 0.1, Key.Space, skipFrom: 3);

        Assert.Equal(CalibrationState.Failed, calibration.State);
        Assert.Contains("Try again", calibration.Prompt);
        game.Do(calibration.Entity!, "Apply");
        Assert.Equal(0, game.AudioOffset);
    }

    [Fact]
    public void ACalibration_UnderAnotherPanel_CountsNoTaps_AndStopping_BringsTheMusicBack()
    {
        var (game, input, audio, calibration) = Calibrating();
        var over = new Entity("Over");
        over.AddComponent<Panel>().PauseGame = false;
        game.Scene.Add(over);
        Tick(game, input);
        game.OpenPanel("Over");
        TapAlong(game, input, audio, late: 0.1, Key.Space);
        Assert.Equal((CalibrationState.Failed, 0), (calibration.State, calibration.Taps));

        game.ClosePanel();
        game.Do(calibration.Entity!, "Start");
        Run(game, 1, input);
        Assert.True(calibration.IsRunning);
        game.Do(calibration.Entity!, "Stop");
        Tick(game, input);
        Assert.Equal(CalibrationState.Waiting, calibration.State);
        Assert.Equal("Music/theme.wav", audio.Music);
        Assert.True(game.Find("Apply")!.IsEnabled);
    }

    [Fact]
    public void ACalibration_MeasuringWhatsSeen_FlashesInSilence_AndSetsTheVisualOffset()
    {
        var (game, input, audio, calibration) = Calibrating(c => c.Measures = CalibrationKind.Visual);
        Tick(game, input);
        Assert.Contains("Watch the light", calibration.Prompt);

        TapAlong(game, input, audio, late: 0.05, Key.Space);
        Assert.InRange(calibration.Result, 45, 55);
        game.Do(calibration.Entity!, "Apply");
        Assert.Equal(calibration.Result, game.VisualOffset);
        Assert.Equal(0, game.AudioOffset);
    }

    [Fact]
    public void ACalibrationScreen_OnAPanel_StartsAsItOpens_AndStopsAsItCloses()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 6;
        scene.Add(camera);
        var music = new Entity("Music");
        music.AddComponent<BackgroundMusic>().Music = "Music/theme.wav";
        scene.Add(music);
        var panel = new Entity(CalibrationScreen.Name);
        CalibrationScreen.Configure(panel);
        scene.Add(panel);
        foreach (var child in CalibrationScreen.Children())
            scene.Add(child, panel);
        var input = new InputState();
        var audio = new SilentAudio();
        var game = new Game(scene, input) { Audio = audio, AudioOffset = 40 };
        var calibration = panel.GetComponent<Calibration>()!;

        Run(game, 0.5f, input);
        Assert.False(panel.IsEnabled);
        Assert.Equal(CalibrationState.Waiting, calibration.State);
        Assert.Equal(40, calibration.Current);

        game.OpenPanel(CalibrationScreen.Name);
        Run(game, 0.2f, input);
        Assert.True(calibration.IsRunning);
        Assert.Equal(IAudio.MetronomeMusic, audio.Music);
        Assert.False(game.Find("Back")!.IsEnabled);
        Assert.False(game.Find("Offset")!.IsEnabled);
        // The prompt names the tap's key.
        Assert.Contains("Space, a click or a touch", game.Find("Prompt")!.GetComponent<Text>()!.Shown);

        // Esc closes it, leaving the music as it was.
        Press(game, input, Key.Escape);
        Assert.False(panel.IsEnabled);
        Assert.Equal(CalibrationState.Waiting, calibration.State);
        Assert.Equal("Music/theme.wav", audio.Music);
        Assert.True(game.Find("Back")!.IsEnabled);
    }

    [Fact]
    public void InANetworkGame_EachPlayersOffset_IsTheirOwn_AndChangesNothingOnAnyoneElsesMachine()
    {
        static Scene Scene()
        {
            var scene = new Scene();
            var settings = new Entity("Game");
            settings.AddComponent<Multiplayer>();
            scene.Add(settings);
            var music = new Entity("Music");
            music.AddComponent<BackgroundMusic>().Music = "Music/theme.wav";
            music.AddComponent<BeatClock>();
            scene.Add(music);
            var entity = new Entity("Calibrate");
            var calibration = entity.AddComponent<Calibration>();
            (calibration.Tempo, calibration.Beats, calibration.StartOnShow) = (100, 8, false);
            scene.Add(entity);
            var start = new Entity("Start");
            var button = start.AddComponent<Button>();
            (button.Action, button.Do, button.Targets, button.Shortcut) = (ButtonAction.Do, "Start", "Calibrate", Key.G);
            scene.Add(start, entity);
            return scene;
        }

        var world = new Griff.Tests.Networking.NetWorld(Scene, setUp: g => g.Audio = new SilentAudio());
        var (host, client) = (world.Host, world.Client);
        var hostBeats = new List<int>();
        host.On("Beat", data => hostBeats.Add((int)data!));
        client.AudioOffset = 250;
        world.Run(1.05);

        // The host's beats (and what steps to them) go by the host's own hearing; the player's clock counts theirs, later.
        var hostClock = host.Scene.All<BeatClock>()[0];
        var clientClock = client.Scene.All<BeatClock>()[0];
        Assert.InRange(hostClock.Beats - clientClock.Beats, 0.45f, 0.55f);
        Assert.Equal(0, host.AudioOffset);

        // A player's calibration button starts their own calibration, there and then: nobody else's.
        var keys = Griff.Tests.Networking.NetWorld.Keys(client);
        keys.SetKey(Key.G, true);
        world.Run(Griff.Tests.Networking.NetWorld.Frame);
        keys.SetKey(Key.G, false);
        world.Run(0.1);
        Assert.True(client.Scene.All<Calibration>()[0].IsRunning);
        Assert.False(host.Scene.All<Calibration>()[0].IsRunning);
        Assert.Equal(IAudio.MetronomeMusic, client.Audio.Music);
        Assert.Equal("Music/theme.wav", host.Audio.Music);
    }

    [Fact]
    public void ACalibration_InTheBrowser_MeasuresOnlyTheDelayWebAudioDoesntReport()
    {
        // The web player's output: handed sound ahead of time, saying it holds what's queued plus 40 ms of its own.
        var now = 0.0;
        var output = new BufferedOutput(new Mixer(), () => now);
        var scene = new Scene();
        var entity = new Entity("Calibrate");
        var calibration = entity.AddComponent<Calibration>();
        (calibration.Tempo, calibration.Beats) = (100, 8);
        scene.Add(entity);
        var input = new InputState();
        var game = new Game(scene, input) { Audio = new MixerAudio(output.Mixer, new NoSounds()) };
        var queued = 0.0;

        // Bluetooth headphones 120 ms later still: the player taps as they hear each click, then.
        var next = 4;
        for (var frame = 0; frame < 60 * 10 && (frame < 5 || calibration.IsRunning); frame++)
        {
            queued = Math.Max(0, queued - Step);
            queued += output.Pump(queued, latency: 0.04).Length / 2.0 / Mixer.SampleRate;
            var tap = next < 12 && game.Audio.MusicTime - 0.12 >= next * 0.6;
            if (tap)
            {
                next++;
                input.SetKey(Key.Space, true);
            }

            Tick(game, input);
            input.SetKey(Key.Space, false);
            now += Step;
        }

        Assert.Equal(CalibrationState.Measured, calibration.State);
        Assert.InRange(calibration.Result, 115, 135);
    }

    // A game with music, and a calibration of 8 beats at 100 a minute (36 steps a beat) after a count-in of 4.
    private static (Game Game, InputState Input, SilentAudio Audio, Calibration Calibration) Calibrating(Action<Calibration>? setup = null)
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        scene.Add(camera);
        var music = new Entity("Music");
        music.AddComponent<BackgroundMusic>().Music = "Music/theme.wav";
        scene.Add(music);
        var entity = new Entity("Calibrate");
        var calibration = entity.AddComponent<Calibration>();
        (calibration.Tempo, calibration.Beats) = (100, 8);
        setup?.Invoke(calibration);
        scene.Add(entity);
        var apply = new Entity("Apply");
        var button = apply.AddComponent<Button>();
        (button.Action, button.Do, button.Targets) = (ButtonAction.Do, "Apply", "Calibrate");
        scene.Add(apply, entity);

        var input = new InputState();
        var audio = new SilentAudio();
        var game = new Game(scene, input) { Audio = audio };
        Tick(game, input);
        return (game, input, audio, calibration);
    }

    // Taps along to the 8 beats after the count-in, each this late (or as odd says), until the calibration is done.
    private static void TapAlong(Game game, InputState input, SilentAudio audio, double late, Key key, Dictionary<int, double>? odd = null,
        bool gamepad = false, bool mouse = false, int skipFrom = int.MaxValue)
    {
        var next = 4;
        for (var i = 0; i < 60 * 12 && game.Scene.All<Calibration>()[0].IsRunning; i++)
        {
            var at = next * 0.6 + (odd?.GetValueOrDefault(next - 4, late) ?? late);
            if (next < 12 && audio.MusicTime >= at - 1e-6)
            {
                next++;
                if (next - 5 >= skipFrom)
                {
                    Tick(game, input);
                    continue;
                }

                if (key != Key.None)
                    input.SetKey(key, true);
                if (gamepad)
                    input.SetGamepad([GamepadButton.A], Vector2.Zero);
                if (mouse)
                    input.SetMouseButton(MouseButton.Left, true);
                Tick(game, input);
                if (key != Key.None)
                    input.SetKey(key, false);
                if (gamepad)
                    input.SetGamepad([], Vector2.Zero);
                if (mouse)
                    input.SetMouseButton(MouseButton.Left, false);
                continue;
            }

            Tick(game, input);
        }
    }

    private static (Game Game, SilentAudio Audio, BeatClock Clock, List<(int Beat, double Time)> Beats) Setup()
    {
        var scene = new Scene();
        var entity = new Entity("Music");
        entity.AddComponent<BackgroundMusic>().Music = "Music/theme.wav";
        var clock = entity.AddComponent<BeatClock>();
        scene.Add(entity);
        var audio = new SilentAudio();
        var game = new Game(scene, new InputState()) { Audio = audio };
        var beats = new List<(int, double)>();
        game.On("Beat", data => beats.Add(((int)data!, audio.MusicTime)));
        return (game, audio, clock, beats);
    }

    private static void Tick(Game game, InputState input)
    {
        game.Tick(Step);
        input.EndFrame();
    }

    private static void Press(Game game, InputState input, Key key)
    {
        input.SetKey(key, true);
        Tick(game, input);
        input.SetKey(key, false);
        Tick(game, input);
    }

    private static void Run(Game game, float seconds, InputState? input = null)
    {
        for (var t = 0f; t < seconds - 1e-4f; t += Step)
        {
            game.Tick(Step);
            input?.EndFrame();
        }
    }

    private sealed class NoSounds : ISoundLibrary
    {
        public SoundClip? Load(string sound) => null;
    }
}
