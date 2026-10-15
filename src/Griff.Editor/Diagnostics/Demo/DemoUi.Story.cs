#if DEBUG
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Features.Story;

namespace Griff.Editor.Diagnostics.Demo;

// Story: the Dialogue Editor's parts, and picking and typing in them.
internal sealed partial class DemoUi
{
    /// <summary>The open Dialogue Editor, if any.</summary>
    public static DialogueEditorWindow? DialogueEditor =>
        Application.Current.Windows.OfType<DialogueEditorWindow>().FirstOrDefault(w => w.IsVisible);

    /// <summary>Waits for the Dialogue Editor to open (after Entity > Edit Dialogue).</summary>
    public static async Task<DialogueEditorWindow> WaitForDialogueEditor()
    {
        var window = await WaitFor(() => DialogueEditor, "the Dialogue Editor");
        await Settle(400);
        return window;
    }

    /// <summary>
    /// A part of the open Dialogue Editor: line:3 (a line in the list, from 1), list, speaker, expression, said, row,
    /// directions, kind, who, position, face, background, transition, adddirection, goesto, tags, tagkind, tagname, addtag,
    /// problems, preview, previewtext, add, addchoice, addpart, addgoto, remove, up, down, notice, entity, close; or the
    /// whole window.
    /// </summary>
    public static FrameworkElement DialoguePart(string part)
    {
        var window = DialogueEditor ?? throw new InvalidOperationException("The Dialogue Editor isn't open.");
        var (kind, name) = part.IndexOf(':') is var at and >= 0 ? (part[..at].Trim(), part[(at + 1)..].Trim()) : (part.Trim(), "");
        FrameworkElement Named(string x) => (FrameworkElement)window.FindName(x);
        return kind.ToLowerInvariant() switch
        {
            "" or "window" => (FrameworkElement)window.Content,
            "line" => Descendants<ListBoxItem>(window).FirstOrDefault(i => i.DataContext is LineRow r && r.Number == name && i.IsVisible)
                ?? throw new InvalidOperationException($"No line {name} in the Dialogue Editor."),
            "list" => Named("LineList"),
            "speaker" => Named("SpeakerBox"),
            "expression" => Named("ExpressionBox"),
            "said" => Named("SaidBox"),
            "row" => Named("RowBox"),
            "directions" => Named("DirectionList"),
            "kind" => Named("KindBox"),
            "who" => Named("WhoBox"),
            "position" => Named("PositionBox"),
            "face" => Named("NewExpressionBox"),
            "background" => Named("BackgroundBox"),
            "transition" => Named("TransitionBox"),
            "adddirection" => Named("AddDirectionButton"),
            "goesto" => Named("GoesToBox"),
            "tags" => Named("TagList"),
            "tagkind" => Named("TagKindBox"),
            "tagname" => Named("TagNameBox"),
            "tagpick" => Named("TagNamePick"),
            "addtag" => Named("AddTagButton"),
            "addchoice" => Named("AddChoiceButton"),
            "addpart" => Named("AddPartButton"),
            "addgoto" => Named("AddGoToButton"),
            "problems" => Named("ProblemsText"),
            "preview" => Named("Preview"),
            "previewtext" => Named("PreviewText"),
            "add" => Named("AddLineButton"),
            "remove" => Named("RemoveLineButton"),
            "up" => Named("MoveUpButton"),
            "down" => Named("MoveDownButton"),
            "notice" => Named("NoticeBar"),
            "entity" => Named("EntityName"),
            "close" => Named("CloseButton"),
            _ => throw new FormatException($"Unknown Dialogue Editor part \"{part}\" (line:, list, speaker, expression, said, row, directions, kind, who, position, face, background, transition, adddirection, goesto, tags, tagkind, tagname, addtag, problems, preview, previewtext, add, addchoice, addpart, addgoto, remove, up, down, notice, entity, close)."),
        };
    }

    /// <summary>
    /// Sets one of the Dialogue Editor's fields as someone would: picks from its list with the real mouse, or types into its
    /// box and presses Enter.
    /// </summary>
    public async Task DialogueSet(string field, string value)
    {
        await WaitForDialogueEditor();
        var wanted = value.Trim();
        switch (DialoguePart(field))
        {
            case ComboBox box:
            {
                box.BringIntoView();
                await Settle(80);
                await Click(box);
                bool Matches(ComboBoxItem i) => i.IsVisible && string.Equals(i.Content as string ?? i.DataContext as string, wanted, StringComparison.OrdinalIgnoreCase);
                var item = await WaitFor(() => Find<ComboBoxItem>(Matches), $"{(wanted.Length == 0 ? "the empty choice" : wanted)} in {field}'s list", 3000);
                await Settle(80);
                await Click(item);
                await Settle(150);
                box.IsDropDownOpen = false;
                if (!string.Equals(box.SelectedItem as string, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedItem = box.Items.OfType<string>().First(s => string.Equals(s, wanted, StringComparison.OrdinalIgnoreCase));
                    Log?.Invoke($"FORCED dialogue {field} = {wanted}");
                }

                break;
            }

            case TextBox text:
                await EditBox(text, wanted);
                break;
            default:
                throw new InvalidOperationException($"The Dialogue Editor's {field} isn't something to set.");
        }
    }

    /// <summary>The Dialogue Editor's line numbered <paramref name="number"/> (from 1), clicked.</summary>
    public async Task DialogueLine(int number)
    {
        await WaitForDialogueEditor();
        var item = DialoguePart("line:" + number.ToString(CultureInfo.InvariantCulture));
        item.BringIntoView();
        await Settle(80);
        await Click(item);
        await Settle(150);
    }
}
#endif
