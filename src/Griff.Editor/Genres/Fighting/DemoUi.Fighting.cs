#if DEBUG
using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Features.Art;
using Griff.Editor.Genres.Fighting;

namespace Griff.Editor.Diagnostics.Demo;

// Fighting: the Move List's parts, and picking and typing in them; the Clip Editor's boxes, and drawing them.
internal sealed partial class DemoUi
{
    /// <summary>The open Move List, if any.</summary>
    public static MoveListView? MoveList =>
        DocumentOfView<MoveListView>();

    /// <summary>Waits for the Move List to open (after Entity > Edit Moves).</summary>
    public static async Task<MoveListView> WaitForMoveList()
    {
        var window = await WaitFor(() => MoveList, "the Move List");
        await Settle(400);
        return window;
    }

    /// <summary>
    /// A part of the open Move List: move:Palm (a move in the list, by name), list, arrows, description, name, motion,
    /// button, air, input, clip, startup, active, recovery, hits, blockstun, chip, cancels, push, sound, message, row,
    /// frames, summary, advantage, problems, add, remove, up, down, table, notice, entity, close; or the whole window.
    /// </summary>
    public static FrameworkElement MovesPart(string part)
    {
        var window = MoveList ?? throw new InvalidOperationException("The Move List isn't open.");
        var (kind, name) = part.IndexOf(':') is var at and >= 0 ? (part[..at].Trim(), part[(at + 1)..].Trim()) : (part.Trim(), "");
        FrameworkElement Named(string x) => (FrameworkElement)window.FindName(x);
        return kind.ToLowerInvariant() switch
        {
            "" or "window" => (FrameworkElement)window.Content,
            "move" => Descendants<ListBoxItem>(window).FirstOrDefault(i => i.DataContext is MoveRow r && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) && i.IsVisible)
                ?? throw new InvalidOperationException($"No move {name} in the Move List."),
            "list" => Named("MoveList"),
            "arrows" => Named("InputArrows"),
            "description" => Named("InputDescription"),
            "name" => Named("NameBox"),
            "motion" => Named("MotionBox"),
            "button" => Named("ButtonBox"),
            "air" => Named("AirBox"),
            "input" => Named("InputBox"),
            "clip" => Named("ClipBox"),
            "startup" => Named("StartupBox"),
            "active" => Named("ActiveBox"),
            "recovery" => Named("RecoveryBox"),
            "hits" => Named("HitsBox"),
            "blockstun" => Named("BlockstunBox"),
            "chip" => Named("ChipBox"),
            "cancels" => Named("CancelsBox"),
            "push" => Named("PushBox"),
            "sound" => Named("SoundBox"),
            "message" => Named("MessageBox"),
            "row" => Named("RowBox"),
            "frames" => Named("FrameBar"),
            "summary" => Named("FrameSummary"),
            "advantage" => Named("Advantage"),
            "problems" => Named("ProblemsText"),
            "add" => Named("AddMoveButton"),
            "remove" => Named("RemoveMoveButton"),
            "up" => Named("MoveUpButton"),
            "down" => Named("MoveDownButton"),
            "table" => Named("MakeTableButton"),
            "notice" => Named("NoticeBar"),
            "entity" => Named("EntityName"),
            "close" => Named("CloseButton"),
            _ => throw new FormatException($"Unknown Move List part \"{part}\" (move:, list, arrows, description, name, motion, button, air, input, clip, startup, active, recovery, hits, blockstun, chip, cancels, push, sound, message, row, frames, summary, advantage, problems, add, remove, up, down, table, notice, entity, close)."),
        };
    }

    /// <summary>
    /// Sets one of the Move List's fields as someone would: picks from its list with the real mouse (a motion by its digits,
    /// "236", or its name), or types into its box and presses Enter.
    /// </summary>
    public async Task MovesSet(string field, string value)
    {
        await WaitForMoveList();
        var wanted = value.Trim();
        switch (MovesPart(field))
        {
            case ComboBox box:
            {
                box.BringIntoView();
                await Settle(80);
                await Click(box);
                bool Fits(object? o) => o switch
                {
                    MotionChoice m => m.Motion == wanted || m.Label.Contains(wanted, StringComparison.OrdinalIgnoreCase),
                    string s => string.Equals(s, wanted, StringComparison.OrdinalIgnoreCase),
                    _ => false,
                };
                var item = await WaitFor(() => Find<ComboBoxItem>(i => i.IsVisible && Fits(i.DataContext)), $"{wanted} in {field}'s list", 3000);
                item.BringIntoView();
                await Settle(80);
                await Click(item);
                await Settle(150);
                box.IsDropDownOpen = false;
                if (!Fits(box.SelectedItem))
                {
                    box.SelectedItem = box.Items.Cast<object>().First(Fits);
                    Log?.Invoke($"FORCED moves {field} = {wanted}");
                }

                break;
            }

            case CheckBox check:
                if (check.IsChecked != bool.Parse(wanted))
                    await Click(check);
                break;
            case TextBox text:
                await EditBox(text, wanted);
                break;
            default:
                throw new InvalidOperationException($"The Move List's {field} isn't something to set.");
        }
    }

    /// <summary>The Move List's move called <paramref name="name"/>, clicked.</summary>
    public async Task MovesPick(string name)
    {
        await WaitForMoveList();
        var item = MovesPart("move:" + name);
        item.BringIntoView();
        await Settle(80);
        await Click(item);
        await Settle(150);
    }

    /// <summary>
    /// A part of the open Clip Editor's boxes: boxes (the panel), tool:Hitbox (or Select, Hurtbox), circles, copyprevious,
    /// boxessummary, boxlist, box:0 (a box by its place), boxsettings, damage, knockbackx, knockbacky, stun, group,
    /// multiplier, removebox, canvas (over the frame); null for a part that isn't the boxes'.
    /// </summary>
    public static FrameworkElement? ClipBoxesPart(string part)
    {
        if (ClipEditor is not { } window)
            return null;
        var (kind, name) = part.IndexOf(':') is var at and >= 0 ? (part[..at].Trim(), part[(at + 1)..].Trim()) : (part.Trim(), "");
        FrameworkElement Named(string x) => Descendants<FrameworkElement>(window).FirstOrDefault(e => e.Name == x)
            ?? throw new InvalidOperationException($"The Clip Editor shows no {x}.");
        return kind.ToLowerInvariant() switch
        {
            "boxes" => Named("BoxesPanel"),
            "tool" => name.ToLowerInvariant() switch
            {
                "select" => Named("SelectTool"),
                "hitbox" => Named("HitboxTool"),
                "hurtbox" => Named("HurtboxTool"),
                _ => throw new FormatException($"No Clip Editor tool \"{name}\" (Select, Hitbox, Hurtbox)."),
            },
            "circles" => Named("CirclesBox"),
            "copyprevious" => Named("CopyPreviousButton"),
            "boxessummary" => Named("BoxesSummary"),
            "boxlist" => Named("BoxList"),
            "box" => Descendants<ListBoxItem>(Named("BoxList")).FirstOrDefault(i => i.DataContext is FrameBoxRow r && r.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) == name)
                ?? throw new InvalidOperationException($"No box {name} on the Clip Editor's frame."),
            "boxsettings" => Named("BoxSettings"),
            "damage" => Named("DamageBox"),
            "knockbackx" => Named("KnockbackXBox"),
            "knockbacky" => Named("KnockbackYBox"),
            "stun" => Named("StunBox"),
            "group" => Named("GroupBox"),
            "multiplier" => Named("MultiplierBox"),
            "removebox" => Named("RemoveBoxButton"),
            "canvas" => Named("BoxCanvas"),
            _ => null,
        };
    }

    /// <summary>The open Clip Editor's boxes.</summary>
    public static HitboxOverlay ClipBoxes(ClipEditorView window) =>
        window.Model.Overlay<HitboxOverlay>() ?? throw new InvalidOperationException("The Clip Editor has no boxes.");
}
#endif
