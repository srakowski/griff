using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editor.Core.Commands;

namespace Griff.Editor.Features.Data;

/// <summary>
/// The Simulations panel's look: a toolbar to pick and run a simulation, its settings down the side, and its results (a
/// grid, the matrix, the sweep or a run, beside a chart). Everything it does goes through the panel
/// (<see cref="SimulationsPanel"/>) and the Data commands.
/// </summary>
public partial class SimulationsPanelView : UserControl
{
    public SimulationsPanelView(CommandRegistry commands)
    {
        InitializeComponent();
        NewButton.Command = commands.Get("Data.NewSimulation");
        RunButton.Command = commands.Get("Data.RunSimulation");
        StopButton.Command = commands.Get("Data.StopSimulation");
        UndoButton.Command = commands.Get("Data.UndoSimulation");
        RedoButton.Command = commands.Get("Data.RedoSimulation");
        ExportButton.Command = commands.Get("Data.ExportResults");
        // Undo and redo the settings while the panel has the keyboard (not while a box is being typed in: it undoes its own text).
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control || e.OriginalSource is TextBox { IsFocused: true })
                return;
            var keyCommand = e.Key == Key.Z ? UndoButton.Command : e.Key == Key.Y ? RedoButton.Command : null;
            if (keyCommand?.CanExecute(null) == true)
            {
                keyCommand.Execute(null);
                e.Handled = true;
            }
        };
        ResultsGrid.AutoGeneratingColumn += OnColumn;
        TabGrid.AutoGeneratingColumn += OnColumn;
    }

    private static readonly Style Numbers = new(typeof(TextBlock)) { Setters = { new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right) } };

    // A column of the results, read by its name whatever it has in it ("Win %", "Damage Dealt"), numbers to the right.
    private static void OnColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        var column = new DataGridTextColumn
        {
            Header = e.PropertyName,
            Binding = new Binding { Path = new PropertyPath($"[{e.PropertyName}]") },
            SortMemberPath = e.PropertyName,
        };
        if (e.PropertyType == typeof(double))
            column.ElementStyle = Numbers;
        e.Column = column;
    }
}

/// <summary>
/// Colours a matrix cell by how one-sided its pair is: the panel's own colour when it's even, more of the accent the more
/// the row's side wins, more of the info blue the more the column's side wins. The colours are the theme's.
/// </summary>
public sealed class LeanBrushConverter : IValueConverter
{
    /// <summary>A pair the row all but always wins, for the key.</summary>
    public static double FirstExample => 0.8;

    /// <summary>A pair the column all but always wins, for the key.</summary>
    public static double SecondExample => -0.8;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var lean = value is double d && double.IsFinite(d) ? Math.Clamp(d, -1, 1) : 0;
        var surface = Colour("Brush.Surface.Panel", Colors.Black);
        var toward = Colour(lean >= 0 ? "Brush.Accent" : "Brush.Status.Info", Colors.Gray);
        var share = (float)(Math.Abs(lean) * 0.75);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
            (byte)(surface.R + (toward.R - surface.R) * share),
            (byte)(surface.G + (toward.G - surface.G) * share),
            (byte)(surface.B + (toward.B - surface.B) * share)));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    private static System.Windows.Media.Color Colour(string key, System.Windows.Media.Color fallback) =>
        Application.Current?.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;
}
