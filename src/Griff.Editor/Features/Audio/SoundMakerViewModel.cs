using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Audio;

namespace Griff.Editor.Features.Audio;

public enum SoundMakerMode
{
    Effect,
    Music,
}

/// <summary>
/// The Sound Maker: makes retro sound effects from presets and sliders, and looping music from a mood, then saves them
/// as WAV files in the project (Sounds/ and Music/). Every change plays straight away, so tweaking is listening.
/// </summary>
public sealed partial class SoundMakerViewModel : ObservableObject
{
    private static readonly Regex ValidName = new(@"^[A-Za-z0-9 _\-]+$");

    private readonly EditorAudio _audio;
    private readonly DispatcherTimer _replay;
    private readonly Random _random = new();
    private SoundMakerMode _mode;
    private SoundRecipe _sound = SoundRecipe.Preset(SoundPreset.Coin, new Random(7));
    private MusicRecipe _music = MusicRecipe.ForMood(MusicMood.Bright);
    private SoundClip? _clip;
    private float[] _peaks = [];
    private string _name = "coin";
    private string? _editing;
    private string? _status;
    private bool _isRendering;
    private int _renderVersion;
    private bool _quiet;

    public SoundMakerViewModel(EditorAudio audio, SoundMakerMode mode)
    {
        _audio = audio;
        _mode = mode;
        _replay = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => Replay(), Dispatcher.CurrentDispatcher);
        _replay.Stop();

        Presets = Enum.GetValues<SoundPreset>().Select(p => new PresetChoice(p, Humanize(p.ToString()), () => ApplyPreset(p))).ToList();
        Moods = Enum.GetValues<MusicMood>().Select(m => new MoodChoice(m, this)).ToList();
        Waves = new[] { Waveform.Square, Waveform.Sawtooth, Waveform.Triangle, Waveform.Sine, Waveform.Noise }
            .Select(w => new WaveChoice(w, this)).ToList();
        Groups =
        [
            new("Envelope",
            [
                Slider("Attack", r => r.Attack, (r, v) => r with { Attack = v }),
                Slider("Sustain", r => r.Sustain, (r, v) => r with { Sustain = v }),
                Slider("Punch", r => r.Punch, (r, v) => r with { Punch = v }),
                Slider("Decay", r => r.Decay, (r, v) => r with { Decay = v }),
            ]),
            new("Pitch",
            [
                Slider("Frequency", r => r.Frequency, (r, v) => r with { Frequency = v }),
                Slider("Min Frequency", r => r.MinFrequency, (r, v) => r with { MinFrequency = v }),
                Slider("Slide", r => r.Slide, (r, v) => r with { Slide = v }, signed: true),
                Slider("Delta Slide", r => r.DeltaSlide, (r, v) => r with { DeltaSlide = v }, signed: true),
                Slider("Vibrato Depth", r => r.VibratoDepth, (r, v) => r with { VibratoDepth = v }),
                Slider("Vibrato Speed", r => r.VibratoSpeed, (r, v) => r with { VibratoSpeed = v }),
                Slider("Change Amount", r => r.ChangeAmount, (r, v) => r with { ChangeAmount = v }, signed: true),
                Slider("Change Speed", r => r.ChangeSpeed, (r, v) => r with { ChangeSpeed = v }),
            ]),
            new("Tone",
            [
                Slider("Square Duty", r => r.SquareDuty, (r, v) => r with { SquareDuty = v }),
                Slider("Duty Sweep", r => r.DutySweep, (r, v) => r with { DutySweep = v }, signed: true),
                Slider("Repeat Speed", r => r.RepeatSpeed, (r, v) => r with { RepeatSpeed = v }),
                Slider("Phaser Offset", r => r.PhaserOffset, (r, v) => r with { PhaserOffset = v }, signed: true),
                Slider("Phaser Sweep", r => r.PhaserSweep, (r, v) => r with { PhaserSweep = v }, signed: true),
            ]),
            new("Filters",
            [
                Slider("Low-pass Cutoff", r => r.LowPassCutoff, (r, v) => r with { LowPassCutoff = v }),
                Slider("Low-pass Sweep", r => r.LowPassSweep, (r, v) => r with { LowPassSweep = v }, signed: true),
                Slider("Resonance", r => r.LowPassResonance, (r, v) => r with { LowPassResonance = v }),
                Slider("High-pass Cutoff", r => r.HighPassCutoff, (r, v) => r with { HighPassCutoff = v }),
                Slider("High-pass Sweep", r => r.HighPassSweep, (r, v) => r with { HighPassSweep = v }, signed: true),
            ]),
            new("Output",
            [
                Slider("Volume", r => r.Volume, (r, v) => r with { Volume = v }),
            ]),
        ];

        PlayCommand = new RelayCommand(Play);
        StopCommand = new RelayCommand(_audio.StopPreview);
        MutateCommand = new RelayCommand(() => SetSound(_sound.Mutate(_random)));
        NewTuneCommand = new RelayCommand(() => SetMusic(_music with { Seed = _random.Next(1, 10000) }));
        SaveCommand = new RelayCommand(() => Save(), () => _audio.AssetsPath is not null);

        if (mode == SoundMakerMode.Music)
            _name = SuggestName("bright");
        else
            _name = SuggestName("coin");
        Render(play: false);
    }

    /// <summary>Called with the asset path of each sound saved, e.g. to set it on the field that asked for it.</summary>
    public Action<string>? Saved { get; set; }

    public SoundMakerMode Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value))
                return;
            _editing = null;
            Name = SuggestName(value == SoundMakerMode.Music ? _music.Mood.ToString().ToLowerInvariant() : "sound");
            OnPropertyChanged(nameof(IsEffect));
            OnPropertyChanged(nameof(IsMusic));
            OnPropertyChanged(nameof(Folder));
            Render(play: false);
        }
    }

    public bool IsEffect
    {
        get => _mode == SoundMakerMode.Effect;
        set
        {
            if (value)
                Mode = SoundMakerMode.Effect;
        }
    }

    public bool IsMusic
    {
        get => _mode == SoundMakerMode.Music;
        set
        {
            if (value)
                Mode = SoundMakerMode.Music;
        }
    }

    public IReadOnlyList<PresetChoice> Presets { get; }

    public IReadOnlyList<MoodChoice> Moods { get; }

    public IReadOnlyList<WaveChoice> Waves { get; }

    public IReadOnlyList<SliderGroup> Groups { get; }

    public SoundRecipe Sound => _sound;

    public MusicRecipe Music => _music;

    // ----- Music settings

    public int Seed
    {
        get => _music.Seed;
        set => SetMusic(_music with { Seed = value });
    }

    public double Tempo
    {
        get => _music.Tempo;
        set => SetMusic(_music with { Tempo = (int)Math.Round(value) });
    }

    public IReadOnlyList<int> BarChoices { get; } = [4, 8, 16];

    public int Bars
    {
        get => _music.Bars;
        set => SetMusic(_music with { Bars = value });
    }

    public bool Melody
    {
        get => _music.Melody;
        set => SetMusic(_music with { Melody = value });
    }

    public bool Bass
    {
        get => _music.Bass;
        set => SetMusic(_music with { Bass = value });
    }

    public bool Chords
    {
        get => _music.Chords;
        set => SetMusic(_music with { Chords = value });
    }

    public bool Drums
    {
        get => _music.Drums;
        set => SetMusic(_music with { Drums = value });
    }

    public IReadOnlyList<Waveform> LeadChoices { get; } = [Waveform.Square, Waveform.Sawtooth, Waveform.Triangle, Waveform.Sine];

    public Waveform Lead
    {
        get => _music.Lead;
        set => SetMusic(_music with { Lead = value });
    }

    public double MusicVolume
    {
        get => _music.Volume;
        set => SetMusic(_music with { Volume = (float)value });
    }

    // ----- Output

    /// <summary>The sound's shape, 0 to 1 per slice, for the waveform display.</summary>
    public float[] Peaks
    {
        get => _peaks;
        private set => SetProperty(ref _peaks, value);
    }

    public string Length => _clip is null ? "" : _clip.Seconds < 2 ? $"{_clip.Seconds * 1000:0} ms" : $"{_clip.Seconds:0.0} s";

    public bool IsRendering
    {
        get => _isRendering;
        private set => SetProperty(ref _isRendering, value);
    }

    /// <summary>The file name to save as, without the folder or ".wav".</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(SavePath));
                OnPropertyChanged(nameof(NameError));
            }
        }
    }

    public string Folder => _editing is { } editing && editing.Contains('/')
        ? editing[..editing.LastIndexOf('/')]
        : IsMusic ? "Music" : "Sounds";

    /// <summary>Where Save puts it, e.g. "Sounds/coin.wav".</summary>
    public string SavePath => $"{Folder}/{Name.Trim()}.wav";

    public string? NameError => string.IsNullOrWhiteSpace(Name) ? "Name it to save it."
        : !ValidName.IsMatch(Name.Trim()) ? "Letters, numbers, spaces, - and _ only."
        : null;

    /// <summary>What just happened: "Saved Sounds/coin.wav".</summary>
    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool PlayOnChange { get; set; } = true;

    public RelayCommand PlayCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand MutateCommand { get; }

    public RelayCommand NewTuneCommand { get; }

    public RelayCommand SaveCommand { get; }

    /// <summary>Starts from a preset: a new sound of that kind.</summary>
    public void ApplyPreset(SoundPreset preset)
    {
        Mode = SoundMakerMode.Effect;
        if (_editing is null)
            Name = SuggestName(preset == SoundPreset.Random ? "sound" : Humanize(preset.ToString()).Replace(" ", "").ToLowerInvariant());
        SetSound(SoundRecipe.Preset(preset, _random));
    }

    /// <summary>Starts a tune in a mood, at the tempo and lead that suit it.</summary>
    public void ApplyMood(MusicMood mood)
    {
        Mode = SoundMakerMode.Music;
        if (_editing is null)
            Name = SuggestName(mood.ToString().ToLowerInvariant());
        SetMusic(MusicRecipe.ForMood(mood, _music.Seed) with { Bars = _music.Bars, Volume = _music.Volume });
    }

    /// <summary>Opens a sound made in the Sound Maker to tweak; saving writes over it.</summary>
    public bool Open(string assetPath)
    {
        if (_audio.Resolve(assetPath) is not { } path)
            return false;
        _quiet = true;
        try
        {
            if (Wav.ReadMusicRecipe(path) is { } music)
            {
                Mode = SoundMakerMode.Music;
                _music = music.Clamped();
            }
            else if (Wav.ReadSoundRecipe(path) is { } sound)
            {
                Mode = SoundMakerMode.Effect;
                _sound = sound.Clamped();
            }
            else
                return false;
        }
        finally
        {
            _quiet = false;
        }

        _editing = assetPath;
        Name = Path.GetFileNameWithoutExtension(assetPath);
        OnPropertyChanged(nameof(Folder));
        OnPropertyChanged(nameof(SavePath));
        Status = $"Editing {assetPath}";
        RefreshSettings();
        Render(play: true);
        return true;
    }

    /// <summary>Saves it as a WAV in the project. Returns the asset path, or null if it can't.</summary>
    public string? Save()
    {
        if (NameError is not null || _audio.AssetsPath is not { } root)
        {
            Status = NameError ?? "Open a project to save sounds into it.";
            return null;
        }

        // Made again rather than taken from the display, which may still be catching up with the latest change.
        var clip = IsMusic ? MusicSynth.Render(_music) : SoundSynth.Render(_sound);
        var assetPath = SavePath;
        var path = Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = IsMusic ? Wav.Encode(clip, music: _music) : Wav.Encode(clip, _sound);
        File.WriteAllBytes(path, bytes);
        _editing = assetPath;
        Status = $"Saved {assetPath}";
        Saved?.Invoke(assetPath);
        return assetPath;
    }

    public void Play()
    {
        if (_clip is not null)
            _audio.Preview(_clip, SavePath);
    }

    internal void SetSound(SoundRecipe recipe)
    {
        _sound = recipe.Clamped();
        RefreshSettings();
        Render(play: PlayOnChange);
    }

    internal void SetMusic(MusicRecipe recipe)
    {
        var clamped = recipe.Clamped();
        if (clamped == _music)
            return;
        _music = clamped;
        RefreshSettings();
        Render(play: PlayOnChange);
    }

    // Effects are quick to make; music takes a moment, so it's made in the background and only the latest is kept.
    private SoundClip? Render(bool play)
    {
        if (_quiet)
            return null;
        _replay.Stop();
        var version = ++_renderVersion;
        if (IsEffect)
        {
            Show(SoundSynth.Render(_sound));
            if (play)
                _replay.Start();
            return _clip;
        }

        var music = _music;
        IsRendering = true;
        Task.Run(() => MusicSynth.Render(music)).ContinueWith(task =>
        {
            if (version != _renderVersion || task.Result is not { } clip)
                return;
            IsRendering = false;
            Show(clip);
            if (play)
                _replay.Start();
        }, TaskScheduler.FromCurrentSynchronizationContext());
        return null;
    }

    private void Show(SoundClip clip)
    {
        _clip = clip;
        Peaks = clip.Peaks(240);
        OnPropertyChanged(nameof(Length));
    }

    // Plays the latest after a pause in changes, so dragging a slider doesn't stutter.
    private void Replay()
    {
        _replay.Stop();
        Play();
    }

    private void RefreshSettings()
    {
        foreach (var slider in Groups.SelectMany(g => g.Sliders))
            slider.Refresh();
        foreach (var wave in Waves)
            wave.Refresh();
        foreach (var mood in Moods)
            mood.Refresh();
        OnPropertyChanged(nameof(Seed));
        OnPropertyChanged(nameof(Tempo));
        OnPropertyChanged(nameof(Bars));
        OnPropertyChanged(nameof(Melody));
        OnPropertyChanged(nameof(Bass));
        OnPropertyChanged(nameof(Chords));
        OnPropertyChanged(nameof(Drums));
        OnPropertyChanged(nameof(Lead));
        OnPropertyChanged(nameof(MusicVolume));
    }

    // "coin", then "coin2", "coin3"... whichever isn't taken in the folder.
    private string SuggestName(string stem)
    {
        if (_audio.AssetsPath is not { } root)
            return stem;
        var folder = Path.Combine(root, IsMusic ? "Music" : "Sounds");
        for (var i = 1; ; i++)
        {
            var name = i == 1 ? stem : $"{stem}{i}";
            if (!File.Exists(Path.Combine(folder, name + ".wav")))
                return name;
        }
    }

    private SliderSetting Slider(string label, Func<SoundRecipe, float> get, Func<SoundRecipe, float, SoundRecipe> set, bool signed = false) =>
        new(label, signed ? -1 : 0, () => get(_sound), v => SetSound(set(_sound, v)));

    private static string Humanize(string name) => Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
}

/// <summary>One setting of a sound effect, as a slider.</summary>
public sealed class SliderSetting(string label, double minimum, Func<float> get, Action<float> set) : ObservableObject
{
    public string Label { get; } = label;

    public double Minimum { get; } = minimum;

    public double Maximum => 1;

    public double Value
    {
        get => get();
        set
        {
            if (Math.Abs(value - get()) > 1e-4)
                set((float)value);
        }
    }

    public string Display => Value.ToString("0.00", CultureInfo.CurrentCulture);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Display));
    }
}

public sealed class SliderGroup(string title, IReadOnlyList<SliderSetting> sliders)
{
    public string Title { get; } = title;

    public IReadOnlyList<SliderSetting> Sliders { get; } = sliders;
}

public sealed class PresetChoice(SoundPreset preset, string label, Action apply)
{
    public SoundPreset Preset { get; } = preset;

    public string Label { get; } = label;

    public RelayCommand Command { get; } = new(apply);
}

/// <summary>A mood button: picking it writes a tune in that mood.</summary>
public sealed class MoodChoice(MusicMood mood, SoundMakerViewModel owner) : ObservableObject
{
    public MusicMood Mood { get; } = mood;

    public string Label => Mood.ToString();

    public bool IsSelected
    {
        get => owner.Music.Mood == Mood;
        set
        {
            if (value && owner.Music.Mood != Mood)
                owner.ApplyMood(Mood);
            OnPropertyChanged();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>A waveform button for sound effects.</summary>
public sealed class WaveChoice(Waveform wave, SoundMakerViewModel owner) : ObservableObject
{
    public Waveform Wave { get; } = wave;

    public string Label => Wave.ToString();

    public bool IsSelected
    {
        get => owner.Sound.Wave == Wave;
        set
        {
            if (value && owner.Sound.Wave != Wave)
                owner.SetSound(owner.Sound with { Wave = Wave });
            OnPropertyChanged();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}
