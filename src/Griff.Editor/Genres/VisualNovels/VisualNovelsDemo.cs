#if DEBUG
using System.Globalization;
using System.Windows;
using Griff.Editor.Diagnostics.Demo;

namespace Griff.Editor.Genres.VisualNovels;

/// <summary>
/// The Visual Novels genre's demo commands, for the Dialogue Editor: dialogue, dialogueline, dialogueset, expectpreview, and
/// its dialogue: parts. See demos/README.md.
/// </summary>
internal sealed class VisualNovelsDemo : DemoGenre
{
    public override async Task<bool> Play(DemoFile demo, string command, string a)
    {
        var ui = demo.Ui;
        switch (command)
        {
            case "dialogue":
                await DemoUi.WaitForDialogueEditor();
                return true;
            case "dialogueline":
                // "dialogueline: 3" clicks the Dialogue Editor's third line.
                await ui.DialogueLine(int.Parse(a, CultureInfo.InvariantCulture));
                return true;
            case "dialogueset":
                // "dialogueset: speaker = Ben" picks from a list; "dialogueset: said = Hello" types into a box.
                var (dialogueField, dialogueValue) = DemoFile.Split2(a, '=');
                await ui.DialogueSet(dialogueField, dialogueValue);
                return true;
            case "expectpreview":
                // The Dialogue Editor's preview of the stage, in words, has this in it.
                await DemoUi.WaitForDialogueEditor();
                if (!await demo.Until(() => DemoUi.DialogueEditor?.Model.PreviewText.Contains(a, StringComparison.OrdinalIgnoreCase) == true, 3000))
                    throw new InvalidOperationException($"The Dialogue Editor's preview says \"{DemoUi.DialogueEditor?.Model.PreviewText}\", not \"{a}\".");
                return true;
            default:
                return false;
        }
    }

    public override FrameworkElement? Part(string kind, string name) => kind == "dialogue" ? DemoUi.DialoguePart(name) : null;
}
#endif
