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
    private static readonly string[] CreateCommands = ["Entity.CreateEmpty", "Entity.CreateSprite", "Entity.CreateText", "Entity.CreateCamera"];
    private static readonly string[] EditCommands = ["Entity.Rename", "Entity.Duplicate", "Entity.Delete", "Entity.Lock", "Entity.Hide"];
    private static readonly string[] ClipboardCommands = ["Entity.Cut", "Entity.Copy", "Entity.Paste"];
    private static readonly string[] PrefabCommands = ["Entity.SaveAsPrefab", "Entity.UnlinkPrefab"];

    private readonly CommandRegistry _commands;
    private Point _dragStart;
    private EntityNode? _dragCandidate;
    private (EntityNode Node, bool Control, bool Shift)? _pressed;

    public HierarchyView(CommandRegistry commands)
    {
        InitializeComponent();
        _commands = commands;
        Tree.ContextMenu = BuildMenu(CreateCommands, EditCommands, ClipboardCommands, PrefabCommands);
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
                    Tag = command,
                });
            }
        }

        // On/off commands (Lock, Hide) show their tick as the menu opens.
        menu.Opened += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
                item.IsChecked = item.Tag is EditorCommand { IsChecked: true };
        };
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
        // Right-click acts on the row under the mouse (on all of the selection, if it's one of them); on empty space
        // it targets the scene root.
        var node = FindItem(e.OriginalSource)?.DataContext as EntityNode;
        if (node is not { IsSelected: true })
            Panel.Select(node);
    }

    // Clicks on a row go to the panel, which knows Ctrl and Shift; the tree itself only selects one row. A row is
    // selected as the button comes up rather than as it goes down, so pressing on one to drag it (onto an Inspector
    // field naming entities) leaves the selection, and the Inspector, as they were.
    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Tree);
        _dragCandidate = FindItem(e.OriginalSource)?.DataContext as EntityNode;
        _pressed = null;
        if (FindItem(e.OriginalSource) is not { DataContext: EntityNode { IsEditing: false } node } || IsInside<ToggleButton>(e.OriginalSource))
            return;

        var modifiers = Keyboard.Modifiers;
        _pressed = (node, modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift));
        e.Handled = true;
    }

    private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_pressed is not var (node, control, shift) || FindItem(e.OriginalSource) is not { } item || item.DataContext != node)
            return;

        _pressed = null;
        Panel.Click(node, control, shift);
        // Focusing a row selects it in the tree, so only focus the primary one.
        if (node.IsPrimary)
            item.Focus();
        else
            Tree.Focus();
        e.Handled = true;
    }

    // H hides the selection in the Scene view (or shows it again), as Entity > Hide does; not while renaming a row.
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.H || Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is TextBox)
            return;

        var hide = _commands.Get("Entity.Hide");
        if (hide.CanExecute())
            _ = hide.ExecuteAsync();
        e.Handled = true;
    }

    // ----- Drag and drop -----

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is not { IsEditing: false } node)
            return;

        var delta = e.GetPosition(Tree) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragCandidate = null;
        _pressed = null;
        // Move within the Hierarchy; Link onto an Inspector field that names entities.
        DragDrop.DoDragDrop(Tree, new DataObject(typeof(EntityNode), node), DragDropEffects.Move | DragDropEffects.Link);
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

    private static bool IsInside<T>(object source) where T : DependencyObject
    {
        for (var current = source as DependencyObject; current is not null and not TreeViewItem; current = VisualParent(current))
        {
            if (current is T)
                return true;
        }

        return false;
    }

    private static DependencyObject? VisualParent(DependencyObject d) =>
        d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    private static TreeViewItem? FindItem(object source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualParent(current))
        {
            if (current is TreeViewItem item)
                return item;
            if (current is TreeView)
                return null;
        }

        return null;
    }
}
