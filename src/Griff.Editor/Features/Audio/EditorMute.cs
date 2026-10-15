using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Themes;
using Griff.Platform.Windows;

namespace Griff.Editor.Features.Audio;

/// <summary>
/// Whether the editor plays sound at all: the game in play mode, the windows of Play with Players, the Sound Maker and
/// sound previews. Silent, the sound device is shut and nothing is heard, while the game plays on exactly as it would with
/// sound (its own volume and Muted settings, and its saves, are left alone). <see cref="IsMuted"/> is the user's switch
/// (Game > Mute), kept between sessions. An editor started with <c>GRIFF_MUTE=1</c> (as agents start it) is silent for
/// that session whatever the switch says, and the switch changes nothing that's kept.
/// </summary>
public sealed partial class EditorMute : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly Action<bool> _apply;

    public EditorMute(ISettingsStore settings)
        : this(settings, AudioOutput.MutedByEnvironment, silent => AudioOutput.Muted = silent)
    {
    }

    /// <summary>For tests: <paramref name="apply"/> stands in for the sound device.</summary>
    internal EditorMute(ISettingsStore settings, bool mutedByEnvironment, Action<bool> apply)
    {
        _settings = settings;
        _apply = apply;
        MutedByEnvironment = mutedByEnvironment;
        _isMuted = settings.Settings.Mute;
        apply(IsSilent);
    }

    /// <summary>
    /// True when the editor was started with <c>GRIFF_MUTE=1</c>: it plays no sound this session, and Mute is not kept.
    /// </summary>
    public bool MutedByEnvironment { get; }

    /// <summary>The Mute switch (Game > Mute): on, the editor plays no sound. Kept between sessions, unless GRIFF_MUTE is set.</summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (!SetProperty(ref _isMuted, value))
                return;
            _apply(IsSilent);
            OnPropertyChanged(nameof(IsSilent));
            // A session muted by GRIFF_MUTE (an agent's, say) leaves the user's own choice as it was.
            if (!MutedByEnvironment && _settings.Settings.Mute != value)
            {
                _settings.Settings.Mute = value;
                _settings.Save();
            }
        }
    }

    /// <summary>True while nothing the editor plays is heard: Mute is on, or GRIFF_MUTE is set.</summary>
    public bool IsSilent => _isMuted || MutedByEnvironment;

    private bool _isMuted;
}

/// <summary>
/// Mutes all sound the editor plays (the game in play mode, Play with Players, the Sound Maker and previews), or turns it
/// back on: an on/off switch, kept between sessions. The game plays on exactly as it would with sound, and its own volume
/// and Muted settings and its saves are left alone. An editor started with GRIFF_MUTE=1 plays no sound either way, and
/// the switch isn't kept.
/// </summary>
public sealed class MuteCommand(EditorMute mute, IStatusService status) : EditorCommand
{
    public override string Id => "Game.Mute";
    public override string Text => "_Mute";
    public override string? Icon => mute.IsMuted ? Icons.Mute : Icons.Volume;
    public override KeyGesture? Gesture { get; } = new(Key.M, ModifierKeys.Control | ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.Game, Group: 0, Order: 6);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 2, Order: 4);

    public override bool? IsChecked => mute.IsMuted;

    protected override Task ExecuteCoreAsync()
    {
        mute.IsMuted = !mute.IsMuted;
        status.Show(mute.MutedByEnvironment
            ? $"Mute {(mute.IsMuted ? "on" : "off")} for this session; GRIFF_MUTE is set, so the editor stays silent"
            : mute.IsMuted ? "Muted: the editor plays no sound" : "Sound on");
        return Task.CompletedTask;
    }
}
