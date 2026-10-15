using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Audio;

// Audio menu: the Sound Maker, for sound effects and music.
public sealed class AudioModule : IEditorModule
{
    public const string Menu = "Audio";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<EditorAudio>();
        services.AddSingleton<EditorMute>();
        services.AddMenu(Menu, "_Audio", order: 5);
        services
            .AddCommand<NewSoundCommand>()
            .AddCommand<NewMusicCommand>()
            .AddCommand<StopPreviewCommand>()
            .AddCommand<MuteCommand>();
    }
}

/// <summary>Opens the Sound Maker on a new sound effect.</summary>
public sealed class NewSoundCommand(EditorAudio audio, IWorkspace workspace) : EditorCommand
{
    public override string Id => "Audio.NewSound";
    public override string Text => "New _Sound...";
    public override string? Icon => Icons.Audio;
    public override MenuPlacement? Placement { get; } = new(AudioModule.Menu, Group: 0, Order: 0);

    protected override bool CanExecuteCore() => workspace.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        audio.OpenMaker(SoundMakerMode.Effect);
        return Task.CompletedTask;
    }
}

/// <summary>Opens the Sound Maker on a new piece of music.</summary>
public sealed class NewMusicCommand(EditorAudio audio, IWorkspace workspace) : EditorCommand
{
    public override string Id => "Audio.NewMusic";
    public override string Text => "New _Music...";
    public override string? Icon => Icons.Music;
    public override MenuPlacement? Placement { get; } = new(AudioModule.Menu, Group: 0, Order: 1);

    protected override bool CanExecuteCore() => workspace.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        audio.OpenMaker(SoundMakerMode.Music);
        return Task.CompletedTask;
    }
}

/// <summary>Stops a sound being previewed in the editor.</summary>
public sealed class StopPreviewCommand(EditorAudio audio) : EditorCommand
{
    public override string Id => "Audio.StopPreview";
    public override string Text => "S_top Preview";
    public override string? Icon => Icons.Stop;
    public override MenuPlacement? Placement { get; } = new(AudioModule.Menu, Group: 1, Order: 0);

    protected override bool CanExecuteCore() => audio.IsPreviewing;

    protected override Task ExecuteCoreAsync()
    {
        audio.StopPreview();
        return Task.CompletedTask;
    }
}
