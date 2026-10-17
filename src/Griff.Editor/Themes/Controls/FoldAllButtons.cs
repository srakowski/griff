using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Griff.Editor.Themes.Controls;

/// <summary>
/// Expand All and Collapse All, side by side on a panel's toolbar: the same two buttons (a double chevron down, a double
/// chevron up) on every panel that shows a tree or folds, each running the command it's given (the panel's
/// Expand All and Collapse All commands). The buttons are tagged "expandall" and "collapseall".
/// </summary>
public class FoldAllButtons : StackPanel
{
    public static readonly DependencyProperty ExpandCommandProperty = DependencyProperty.Register(
        nameof(ExpandCommand), typeof(ICommand), typeof(FoldAllButtons));

    public static readonly DependencyProperty CollapseCommandProperty = DependencyProperty.Register(
        nameof(CollapseCommand), typeof(ICommand), typeof(FoldAllButtons));

    public static readonly DependencyProperty ExpandTipProperty = DependencyProperty.Register(
        nameof(ExpandTip), typeof(string), typeof(FoldAllButtons), new PropertyMetadata("Expand All"));

    public static readonly DependencyProperty CollapseTipProperty = DependencyProperty.Register(
        nameof(CollapseTip), typeof(string), typeof(FoldAllButtons), new PropertyMetadata("Collapse All"));

    public FoldAllButtons()
    {
        Orientation = Orientation.Horizontal;
        Children.Add(MakeButton("expandall", Icons.ChevronDown, ExpandCommandProperty, ExpandTipProperty));
        Children.Add(MakeButton("collapseall", Icons.ChevronUp, CollapseCommandProperty, CollapseTipProperty));
    }

    /// <summary>What Expand All runs: the panel's command that opens everything in it.</summary>
    public ICommand? ExpandCommand
    {
        get => (ICommand?)GetValue(ExpandCommandProperty);
        set => SetValue(ExpandCommandProperty, value);
    }

    /// <summary>What Collapse All runs: the panel's command that folds everything in it shut.</summary>
    public ICommand? CollapseCommand
    {
        get => (ICommand?)GetValue(CollapseCommandProperty);
        set => SetValue(CollapseCommandProperty, value);
    }

    /// <summary>Expand All's tooltip, saying what it opens ("Expand All: open every folder").</summary>
    public string ExpandTip
    {
        get => (string)GetValue(ExpandTipProperty);
        set => SetValue(ExpandTipProperty, value);
    }

    /// <summary>Collapse All's tooltip, saying what it folds shut.</summary>
    public string CollapseTip
    {
        get => (string)GetValue(CollapseTipProperty);
        set => SetValue(CollapseTipProperty, value);
    }

    // A toolbar button showing the chevron twice, one above the other.
    private Button MakeButton(string tag, string chevron, DependencyProperty command, DependencyProperty tip)
    {
        var glyph = new Grid();
        foreach (var margin in new[] { new Thickness(0, 0, 0, 6), new Thickness(0, 6, 0, 0) })
        {
            var icon = Icons.Create(chevron, 9);
            icon.Margin = margin;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            glyph.Children.Add(icon);
        }

        var button = new Button { Tag = tag, Content = glyph };
        button.SetResourceReference(StyleProperty, "Style.Button.Toolbar");
        button.SetBinding(System.Windows.Controls.Primitives.ButtonBase.CommandProperty, new Binding(command.Name) { Source = this });
        button.SetBinding(ToolTipProperty, new Binding(tip.Name) { Source = this });
        return button;
    }
}
