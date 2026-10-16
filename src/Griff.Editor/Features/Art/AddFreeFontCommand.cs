using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Fonts;
using Griff.Scenes;

namespace Griff.Editor.Features.Art;

/// <summary>
/// Adds one of the free fonts that ship with Griff for games to the project: Press Start 2P (pixel), Atkinson
/// Hyperlegible Bold (a clean sans), Crimson Text SemiBold (a serif) or JetBrains Mono Bold (a mono), all under the SIL
/// Open Font License. The font and its license are copied into Assets/Fonts, so the game ships them, and the selected
/// entities' Text is set in it (one undo step); for an agent, "entity" names which entity's Text to set (without it, the
/// editor's selection is used, and none selected just adds the font). Pick the font by name, e.g. "Press Start 2P"; a Font then names it by its
/// path ("Fonts/PressStart2P-Regular.ttf"). The Inspector's font picker does the same when one is picked there.
/// </summary>
public sealed class AddFreeFontCommand(IProjectSession projectSession, ISceneDocuments sceneDocuments, ISelection selection, IDialogService dialogs, IStatusService status) : EditorCommand
{
    public override string Id => "Art.AddFreeFont";
    public override string Text => "Add Free _Font";
    public override string? Icon => Icons.Font;
    public override MenuPlacement? Placement { get; } = new(ArtModule.Menu, Group: 3, Order: 0);
    public override IReadOnlyList<string>? Choices => [.. FreeFonts.All.Select(f => f.Name)];

    protected override bool CanExecuteCore() => projectSession.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        var font = dialogs.Prompt("Add Free Font", "Font (" + string.Join(", ", FreeFonts.All.Select(f => f.Name)) + ")", FreeFonts.Pixel.Name,
            text => FreeFonts.Find(text) is null ? "Name one of Griff's free fonts." : null);
        return font is null ? Task.CompletedTask : ChooseCoreAsync(font);
    }

    protected override Task ChooseCoreAsync(string choice)
    {
        var font = FreeFonts.Find(choice)
                   ?? throw new ArgumentException($"There's no free font called \"{choice}\"; there are {string.Join(", ", FreeFonts.All.Select(f => f.Name))}.", nameof(choice));
        if (projectSession.Project is not { } project)
            return Task.CompletedTask;
        var path = font.AddTo(project.AssetsPath);

        var texts = selection.Entities.Select(e => e.GetComponent<Text>()).OfType<Text>().ToList();
        var property = projectSession.Components.Get(typeof(Text)).Properties.First(p => p.Name == nameof(Griff.Scenes.Text.Font));
        if (texts.Count > 0 && sceneDocuments.Scene is { } document &&
            SetPropertiesEdit.For(texts, property, [.. texts.Select(_ => (object?)path)]) is { } edit)
        {
            document.Execute(edit);
        }

        status.Show(texts.Count > 0
            ? $"{font.Name} is in Assets/{path} with its license, and the selected text is set in it"
            : $"{font.Name} is in Assets/{path} with its license: pick it as a Text's Font");
        return Task.CompletedTask;
    }
}
