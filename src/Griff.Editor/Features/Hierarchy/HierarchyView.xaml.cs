using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editor.Core.Commands;
using Griff.Editor.Themes;

namespace Griff.Editor.Features.Hierarchy;

public partial class HierarchyView : UserControl
{
    private static readonly string[] CreateCommands = ["Entity.CreateEmpty", "Entity.CreateSprite", "Entity.CreateCamera"];
    private static readonly string[] EditCommands = ["Entity.Rename", "Entity.Duplicate", "Entity.Delete"];

    private readonly CommandRegistry _commands;
    private Point _dragStart;
    private EntityNode? _dragCandidate;

    public HierarchyView(CommandRegistry commands)
    {
        InitializeComponent();
        _commands = commands;
        Tree.ContextMenu = BuildMenu(CreateCommands, EditCommands);
    }

    private HierarchyPanel Panel => (HierarchyPanel)DataContext;

    private ContextMenu BuildMenu(params string[][] groups)
    {
        var menu = new ContextMenu();
        foreach (var group in groups)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Separator());

            foreach (var id in group)
            {
                var command = _commands.Get(id);
                menu.Items.Add(new MenuItem
                {
                    Header = command.Text,
                    Command = command,
                    InputGestureText = command.GestureText,
                    Icon = command.Icon is { } icon ? Icons.Create(icon) : null,
                });
            }
        }

        return menu;
    }

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        var menu = BuildMenu(CreateCommands);
        menu.PlacementTarget = AddButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ----- Selection -----

    private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Right-click acts on the row under the mouse; on empty space it targets the scene root.
        Panel.Select(FindItem(e.OriginalSource)?.DataContext as EntityNode);
    }

    // ----- Drag and drop -----

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Tree);
        _dragCandidate = FindItem(e.OriginalSource)?.DataContext as EntityNode;
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is not { IsEditing: false } node)
            return;

        var delta = e.GetPosition(Tree) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragCandidate = null;
        DragDrop.DoDragDrop(Tree, new DataObject(typeof(EntityNode), node), DragDropEffects.Move);
        ClearDropHints();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        ClearDropHints();
        e.Effects = DragDropEffects.None;
        if (e.Data.GetData(typeof(EntityNode)) is EntityNode dragged)
        {
            var (target, position) = HitTest(e);
            if (Panel.CanDrop(dragged, target, position))
            {
                e.Effects = DragDropEffects.Move;
                if (target is not null)
                    target.DropHint = position;
            }
        }

        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        ClearDropHints();
        if (e.Data.GetData(typeof(EntityNode)) is EntityNode dragged)
        {
            var (target, position) = HitTest(e);
            Panel.Drop(dragged, target, position);
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => ClearDropHints();

    /// <summary>Top quarter of a row = before, bottom quarter = after, middle = into. Empty space = root.</summary>
    private static (EntityNode? Target, DropPosition Position) HitTest(DragEventArgs e)
    {
        if (FindItem(e.OriginalSource) is not { DataContext: EntityNode node } item)
            return (null, DropPosition.Into);

        var row = item.Template.FindName("Bd", item) as FrameworkElement ?? item;
        var y = e.GetPosition(row).Y;
        var height = Math.Max(row.ActualHeight, 1);
        var position = y < height * 0.25 ? DropPosition.Before
            : y > height * 0.75 ? DropPosition.After
            : DropPosition.Into;
        return (node, position);
    }

    private void ClearDropHints()
    {
        foreach (var node in Panel.Roots)
            Clear(node);

        static void Clear(EntityNode node)
        {
            node.DropHint = DropPosition.None;
            foreach (var child in node.Children)
                Clear(child);
        }
    }

    // ----- Rename -----

    private void OnRenameVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } editor)
        {
            editor.Dispatcher.InvokeAsync(() =>
            {
                editor.Focus();
                editor.SelectAll();
            });
        }
    }

    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: EntityNode node })
            return;

        if (e.Key == Key.Enter)
        {
            Panel.CommitRename(node);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Panel.CancelRename(node);
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: EntityNode node })
            Panel.CommitRename(node);
    }

    private static TreeViewItem? FindItem(object source)
    {
        for (var current = source as DependencyObject; current is not null; current = Parent(current))
        {
            if (current is TreeViewItem item)
                return item;
            if (current is TreeView)
                return null;
        }

        return null;

        static DependencyObject? Parent(DependencyObject d) =>
            d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
    }
}
