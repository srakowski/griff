#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Griff.Editor.Features.Audio;
using Griff.Editor.Features.Inspector;

namespace Griff.Editor.Diagnostics.Demo;

// Sound: the Sound Maker's parts and sliders, and the Inspector's sound fields.
internal sealed partial class DemoUi
{
    /// <summary>The open Sound Maker, if any.</summary>
    public static SoundMakerWindow? Maker =>
        Application.Current.Windows.OfType<SoundMakerWindow>().FirstOrDefault(w => w.IsVisible);

    /// <summary>Waits for the Sound Maker to open (after Audio > New Sound, or a field's Sound Maker button).</summary>
    public static async Task<SoundMakerWindow> WaitForMaker()
    {
        var window = await WaitFor(() => Maker, "the Sound Maker");
        await Settle(400);
        return window;
    }

    /// <summary>
    /// A part of the open Sound Maker: preset:Coin, wave:Square, mood:Spooky, mode:Effect or mode:Music, slider:Slide (a
    /// setting's row), mutate, newtune, play, stop, save, close, name, waveform, presets, waves, settings, moods, music,
    /// parts, tempo; or the whole window.
    /// </summary>
    public static FrameworkElement MakerPart(string part)
    {
        var window = Maker ?? throw new InvalidOperationException("The Sound Maker isn't open.");
        var (kind, name) = part.IndexOf(':') is var at and >= 0 ? (part[..at].Trim(), part[(at + 1)..].Trim()) : (part.Trim(), "");
        FrameworkElement Named(string x) => (FrameworkElement)window.FindName(x);
        return kind.ToLowerInvariant() switch
        {
            "" or "window" => (FrameworkElement)window.Content,
            "preset" => Descendants<Button>(window).FirstOrDefault(b => b.DataContext is PresetChoice p && p.Label == name && b.IsVisible)
                ?? throw new InvalidOperationException($"No preset {name}."),
            "wave" => Descendants<ToggleButton>(window).FirstOrDefault(b => b.DataContext is WaveChoice w && w.Label == name && b.IsVisible)
                ?? throw new InvalidOperationException($"No wave {name}."),
            "mood" => Descendants<ToggleButton>(window).FirstOrDefault(b => b.DataContext is MoodChoice m && m.Label == name && b.IsVisible)
                ?? throw new InvalidOperationException($"No mood {name}."),
            "mode" => Named(name.Equals("music", StringComparison.OrdinalIgnoreCase) ? "MusicMode" : "EffectMode"),
            "slider" => SliderRow(window, name) ?? throw new InvalidOperationException($"No setting {name}."),
            "mutate" => Named("MutateButton"),
            "newtune" => Named("NewTuneButton"),
            "play" => Named("PlayButton"),
            "stop" => Named("StopButton"),
            "save" => Named("SaveButton"),
            "close" => Named("CloseButton"),
            "name" => Named("NameBox"),
            "waveform" => Named("Waveform"),
            "presets" => Named("PresetPanel"),
            "waves" => Named("WavePanel"),
            "settings" => Named("SettingsScroller"),
            "moods" => Named("MoodPanel"),
            "music" => Named("MusicSettings"),
            "parts" => Named("PartsPanel"),
            "tempo" => Named("TempoSlider"),
            _ => throw new FormatException($"Unknown Sound Maker part \"{part}\" (preset:, wave:, mood:, mode:, slider:, mutate, newtune, play, stop, save, close, name, waveform, presets, waves, settings, moods, music, parts, tempo)."),
        };
    }

    // A sound effect setting's row, or the tempo's.
    private static FrameworkElement? SliderRow(SoundMakerWindow window, string label) =>
        label.Equals("Tempo", StringComparison.OrdinalIgnoreCase)
            ? (FrameworkElement)window.FindName("TempoSlider")
            : Descendants<Grid>(window).FirstOrDefault(g => g.DataContext is SliderSetting s && s.Label.Equals(label, StringComparison.OrdinalIgnoreCase) && g.IsVisible);

    /// <summary>Drags a Sound Maker slider's thumb to <paramref name="value"/>, as someone would.</summary>
    public async Task Slide(string label, double value)
    {
        var window = Maker ?? throw new InvalidOperationException("The Sound Maker isn't open.");
        var row = await WaitFor(() => SliderRow(window, label), $"the {label} setting");
        var slider = row as Slider ?? Descendants<Slider>(row).First();
        slider.BringIntoView();
        await Settle(80);

        var thumb = Descendants<Thumb>(slider).First();
        var track = Descendants<Track>(slider).First();
        var trackRect = ScreenRect(track);
        var thumbWidth = ScreenRect(thumb).Width;
        var fraction = Math.Clamp((value - slider.Minimum) / (slider.Maximum - slider.Minimum), 0, 1);
        var to = new Point(trackRect.Left + thumbWidth / 2 + fraction * (trackRect.Width - thumbWidth), trackRect.Top + trackRect.Height / 2);
        await input.Drag(Center(thumb), [to], 600);
        await Settle(100);

        if (Math.Abs(slider.Value - value) > (slider.Maximum - slider.Minimum) * 0.04)
        {
            slider.Value = value;
            Log?.Invoke($"FORCED slide {label} = {value}");
            await Settle(100);
        }
    }

    /// <summary>One of a sound field's buttons in the Inspector: play, choose or make.</summary>
    public static Button? SoundButton(string component, string label, string which) =>
        InspectorRow(component, label) is { } row
            ? Descendants<Button>(row).FirstOrDefault(b => b.Tag as string == $"sound:{which.ToLowerInvariant()}")
            : null;

    /// <summary>Picks a sound for a field from its list of the project's sounds.</summary>
    public Task PickSound(string component, string label, string sound) => PickFromList(component, label, sound);
}
#endif
