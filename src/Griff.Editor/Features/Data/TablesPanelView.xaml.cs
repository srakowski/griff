using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editor.Core.Commands;

namespace Griff.Editor.Features.Data;

/// <summary>
/// The Tables panel's look: a toolbar, the picked column's settings and a spreadsheet grid. What the grid does goes
/// through the panel (<see cref="TablesPanel"/>) and the Data commands; the grid only shows the table and reports which
/// cells are picked.
/// </summary>
public partial class TablesPanelView : UserControl
{
    private readonly CommandRegistry _commands;
    private TablesPanel? _panel;
    private bool _showing;
    // Which of the table's columns each of the grid's is (-1 for the rows' names).
    private readonly Dictionary<DataGridColumn, int> _columnIndexes = [];

    public TablesPanelView(CommandRegistry commands)
    {
        InitializeComponent();
        _commands = commands;
        NewTableButton.Command = commands.Get("Data.NewTable");
        AddRowButton.Command = commands.Get("Data.AddRow");
        AddColumnButton.Command = commands.Get("Data.AddColumn");
        DeleteRowsButton.Command = commands.Get("Data.DeleteRows");
        DeleteColumnButton.Command = commands.Get("Data.DeleteColumn");
        MoveUpButton.Command = commands.Get("Data.MoveRowsUp");
        MoveDownButton.Command = commands.Get("Data.MoveRowsDown");
        MoveLeftButton.Command = commands.Get("Data.MoveColumnLeft");
        MoveRightButton.Command = commands.Get("Data.MoveColumnRight");
        SortUpButton.Command = commands.Get("Data.SortAscending");
        SortDownButton.Command = commands.Get("Data.SortDescending");
        FillDownButton.Command = commands.Get("Data.FillDown");
        UndoButton.Command = commands.Get("Data.Undo");
        RedoButton.Command = commands.Get("Data.Redo");
        ImportButton.Command = commands.Get("Data.ImportCsv");
        ExportButton.Command = commands.Get("Data.ExportCsv");
        GenerateButton.Command = commands.Get("Data.GenerateCurve");

        DataContextChanged += (_, _) => Attach(DataContext as TablesPanel);
        Grid.SelectedCellsChanged += (_, _) => ReportSelection();
        Grid.CurrentCellChanged += (_, _) => ReportSelection();
        Grid.PreviewKeyDown += OnGridKey;
    }

    private void Attach(TablesPanel? panel)
    {
        if (_panel is not null)
        {
            _panel.ColumnsChanged -= OnColumnsChanged;
            _panel.SelectionChanged -= OnPanelSelection;
            _panel.Refreshed -= OnPanelSelection;
        }

        _panel = panel;
        if (panel is null)
            return;
        panel.ColumnsChanged += OnColumnsChanged;
        panel.SelectionChanged += OnPanelSelection;
        panel.Refreshed += OnPanelSelection;
        BuildColumns();
    }

    private void OnColumnsChanged(object? sender, EventArgs e) => BuildColumns();

    private void OnPanelSelection(object? sender, EventArgs e) => ShowSelection();

    // ----- Columns

    private void BuildColumns()
    {
        if (_panel is null)
            return;
        _showing = true;
        try
        {
            Grid.Columns.Clear();
            _columnIndexes.Clear();

            var names = new DataGridTextColumn
            {
                Header = Header("Name", "the row's name", ""),
                Binding = new Binding(nameof(TableRowView.Name)),
                Width = new DataGridLength(140),
                ElementStyle = CellText(numeric: false, bold: true, cell: null),
                EditingElementStyle = (Style)FindResource("Style.TextBox.Cell"),
            };
            Grid.Columns.Add(names);
            _columnIndexes[names] = -1;

            foreach (var column in _panel.Columns)
            {
                var path = $"{nameof(TableRowView.Cells)}[{column.Index}]";
                DataGridColumn built;
                if (column.HasFormula)
                {
                    // Worked out: shown, not typed into (the formula is changed in the column bar).
                    built = new DataGridTextColumn
                    {
                        Binding = new Binding(path + "." + nameof(TableCellView.Text)) { Mode = BindingMode.OneWay },
                        ElementStyle = CellText(numeric: column.IsNumeric || column.IsYesNo, bold: false, cell: path, workedOut: true),
                        IsReadOnly = true,
                    };
                }
                else if (column.IsYesNo)
                {
                    built = new DataGridCheckBoxColumn { Binding = new Binding(path + "." + nameof(TableCellView.Checked)) };
                }
                else if (column.HasOptions)
                {
                    var shown = new FrameworkElementFactory(typeof(TextBlock));
                    shown.SetBinding(TextBlock.TextProperty, new Binding(path + "." + nameof(TableCellView.Text)));
                    shown.SetValue(StyleProperty, CellText(numeric: false, bold: false, cell: path));
                    var picker = new FrameworkElementFactory(typeof(ComboBox));
                    picker.SetValue(ItemsControl.ItemsSourceProperty, column.Options);
                    picker.SetBinding(Selector.SelectedItemProperty, new Binding(path + "." + nameof(TableCellView.Text)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                    picker.SetValue(MinHeightProperty, 0.0);
                    picker.SetValue(BorderThicknessProperty, new Thickness(0));
                    built = new DataGridTemplateColumn
                    {
                        CellTemplate = new DataTemplate { VisualTree = shown },
                        CellEditingTemplate = new DataTemplate { VisualTree = picker },
                    };
                }
                else
                {
                    built = new DataGridTextColumn
                    {
                        Binding = new Binding(path + "." + nameof(TableCellView.Text)),
                        ElementStyle = CellText(numeric: column.IsNumeric, bold: false, cell: path),
                        EditingElementStyle = EditingText(column.IsNumeric),
                    };
                }

                built.Header = Header(column.Name, column.Caption, column.Name);
                built.Width = new DataGridLength(column.IsYesNo ? 80 : column.HasOptions ? 140 : 110);
                Grid.Columns.Add(built);
                _columnIndexes[built] = column.Index;
            }
        }
        finally
        {
            _showing = false;
        }

        ShowSelection();
    }

    // A column's heading: its name over its type. Clicking it picks the column; right-clicking it offers what to do to it.
    private StackPanel Header(string name, string caption, string key)
    {
        var header = new StackPanel { Tag = key.Length == 0 ? "tables:names" : "tables:header:" + key, Background = Brushes.Transparent };
        header.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Brush.Text.Primary") });
        header.Children.Add(new TextBlock { Text = caption, FontSize = (double)FindResource("FontSize.Caption"), Foreground = (Brush)FindResource("Brush.Text.Tertiary") });
        // Before the heading takes the mouse (it captures it, to drag or resize the column).
        header.PreviewMouseLeftButtonDown += (_, _) => PickColumn(key);
        header.ContextMenu = ColumnMenu(key);
        header.ContextMenuOpening += (_, _) => PickColumn(key);
        header.ToolTip = key.Length == 0
            ? "The rows' names: what scripts and Table Values find a row by. Renaming one keeps the old name working."
            : $"{name}: {caption}. Click to pick the column; right-click for what to do to it.";
        return header;
    }

    private void PickColumn(string column)
    {
        if (_panel?.HasTable == true)
            _panel.SelectColumn(column);
    }

    private ContextMenu ColumnMenu(string column)
    {
        var menu = new ContextMenu();
        void Add(string id)
        {
            var command = _commands.Get(id);
            menu.Items.Add(new MenuItem { Header = command.Text, Command = command, Icon = command.Icon is { } icon ? Themes.Icons.Create(icon) : null });
        }

        Add("Data.SortAscending");
        Add("Data.SortDescending");
        if (column.Length > 0)
        {
            menu.Items.Add(new Separator());
            var types = new MenuItem { Header = "_Type" };
            foreach (var type in Griff.Data.TableColumn.TypeNames)
            {
                var item = new MenuItem { Header = type, IsCheckable = true, IsChecked = _panel?.Columns.FirstOrDefault(c => c.Name == column)?.Type == type };
                item.Click += (_, _) => _panel?.ChangeColumn(column, type: type);
                types.Items.Add(item);
            }

            menu.Items.Add(types);
            Add("Data.MoveColumnLeft");
            Add("Data.MoveColumnRight");
            Add("Data.AddColumn");
            Add("Data.DeleteColumn");
        }

        return menu;
    }

    private Style CellText(bool numeric, bool bold, string? cell, bool workedOut = false)
    {
        var style = new Style(typeof(TextBlock), (Style)FindResource("Style.TextBlock.Cell"));
        // A formula's cells read a shade quieter than the values typed in, as worked out.
        if (workedOut)
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, FindResource("Brush.Text.Secondary")));
        if (numeric)
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        if (bold)
            style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        if (cell is not null)
        {
            style.Setters.Add(new Setter(ToolTipProperty, new Binding(cell + "." + nameof(TableCellView.Tip))));
            var flagged = new DataTrigger { Binding = new Binding(cell + "." + nameof(TableCellView.IsFlagged)), Value = true };
            flagged.Setters.Add(new Setter(TextBlock.ForegroundProperty, FindResource("Brush.Status.Warning")));
            style.Triggers.Add(flagged);
        }

        return style;
    }

    private Style EditingText(bool numeric)
    {
        var style = new Style(typeof(TextBox), (Style)FindResource("Style.TextBox.Cell"));
        if (numeric)
            style.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Right));
        return style;
    }

    // ----- Picked cells

    private void ReportSelection()
    {
        if (_panel is null || _showing || _panel.IsRefreshing)
            return;
        var cells = Grid.SelectedCells
            .Where(c => c.Item is TableRowView && c.Column is not null && _columnIndexes.ContainsKey(c.Column))
            .Select(c => (((TableRowView)c.Item).Name, _columnIndexes[c.Column]))
            .ToList();
        (string, int)? current = Grid.CurrentCell is { Item: TableRowView row, Column: { } column } && _columnIndexes.TryGetValue(column, out var index)
            ? (row.Name, index)
            : null;
        _panel.SetSelection(cells, current);
    }

    // Shows the panel's picked cells in the grid: after an agent picks some, a paste, or the table changing.
    private void ShowSelection()
    {
        if (_panel is null)
            return;
        var hadFocus = Grid.IsKeyboardFocusWithin;
        _showing = true;
        try
        {
            Grid.SelectedCells.Clear();
            DataGridCellInfo? first = null;
            foreach (var (rowName, columnIndex) in _panel.Selection)
            {
                var row = _panel.Rows.FirstOrDefault(r => string.Equals(r.Name, rowName, StringComparison.OrdinalIgnoreCase));
                var column = _columnIndexes.FirstOrDefault(p => p.Value == columnIndex).Key;
                if (row is null || column is null)
                    continue;
                var info = new DataGridCellInfo(row, column);
                Grid.SelectedCells.Add(info);
                first ??= info;
            }

            if (first is { } current)
            {
                Grid.CurrentCell = current;
                Grid.ScrollIntoView(current.Item, current.Column);
                if (hadFocus)
                    FocusCell(current);
            }
        }
        finally
        {
            _showing = false;
        }
    }

    private void FocusCell(DataGridCellInfo info)
    {
        Grid.UpdateLayout();
        if (info.Column.GetCellContent(info.Item)?.Parent is DataGridCell cell)
            cell.Focus();
        else
            Grid.Focus();
    }

    // ----- Keys: a spreadsheet's, through the Data commands (while a cell is being typed into, its box has them)

    private void OnGridKey(object sender, KeyEventArgs e)
    {
        var typing = Keyboard.FocusedElement is TextBox or ComboBox;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        string? id = (modifiers, key) switch
        {
            (ModifierKeys.Control, Key.C) when !typing => "Data.CopyCells",
            (ModifierKeys.Control, Key.X) when !typing => "Data.CutCells",
            (ModifierKeys.Control, Key.V) when !typing => "Data.PasteCells",
            (ModifierKeys.Control, Key.D) when !typing => "Data.FillDown",
            (ModifierKeys.Control, Key.Z) when !typing => "Data.Undo",
            (ModifierKeys.Control, Key.Y) when !typing => "Data.Redo",
            (ModifierKeys.None, Key.Delete) when !typing => "Data.ClearCells",
            (ModifierKeys.Control, Key.Enter) => "Data.AddRow",
            (ModifierKeys.Alt, Key.Up) => "Data.MoveRowsUp",
            (ModifierKeys.Alt, Key.Down) => "Data.MoveRowsDown",
            (ModifierKeys.Alt, Key.Left) => "Data.MoveColumnLeft",
            (ModifierKeys.Alt, Key.Right) => "Data.MoveColumnRight",
            _ => null,
        };
        if (id is null)
            return;
        e.Handled = true;
        if (typing)
            Grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        var command = _commands.Get(id);
        _ = command.ExecuteAsync();
    }
}
