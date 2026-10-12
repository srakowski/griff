using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Griff.Editor.Themes.Controls;

/// <summary>
/// Numeric input. Type a value (Enter or focus loss commits, Esc reverts), drag the label sideways to scrub, or nudge it
/// with Up and Down or the wheel while it's being typed in: Shift for fine steps, Ctrl for coarse. Steps grow with the
/// value, so 70 moves by about as much per pixel, relatively, as 0.7 does. Another element (a field's name beside it) can
/// scrub it too, with <see cref="DragsFieldProperty"/>. <see cref="EditCompletedEvent"/> fires once per typed commit,
/// finished drag or run of nudges, so callers can close an undo step.
/// </summary>
[TemplatePart(Name = "PART_Label", Type = typeof(FrameworkElement))]
[TemplatePart(Name = "PART_Input", Type = typeof(TextBox))]
public class NumberField : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumberField),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((NumberField)d).UpdateText()));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(NumberField));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(NumberField));

    public static readonly DependencyProperty IsIntegerProperty = DependencyProperty.Register(
        nameof(IsInteger), typeof(bool), typeof(NumberField), new PropertyMetadata(false, (d, _) => ((NumberField)d).UpdateText()));

    /// <summary>Shows a dash instead of the value, for several selected entities whose values differ.</summary>
    public static readonly DependencyProperty IsMixedProperty = DependencyProperty.Register(
        nameof(IsMixed), typeof(bool), typeof(NumberField), new PropertyMetadata(false, (d, _) => ((NumberField)d).UpdateText()));

    /// <summary>The least change in value per pixel of drag, near 0; further from 0 it grows with the value.</summary>
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(NumberField), new PropertyMetadata(0.05));

    /// <summary>
    /// Set on an element beside a number field (the name of the field, in the Inspector) to drag the field by it as by
    /// its own label. It drags the one number field found nearby: none or several (a vector's axes) and it does nothing.
    /// </summary>
    public static readonly DependencyProperty DragsFieldProperty = DependencyProperty.RegisterAttached(
        "DragsField", typeof(bool), typeof(NumberField), new PropertyMetadata(false, OnDragsFieldChanged));

    public static readonly RoutedEvent EditCompletedEvent = EventManager.RegisterRoutedEvent(
        "EditCompleted", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(NumberField));

    // A pixel of drag changes the value by this much of itself (Step, near 0): 70 by 0.35 a pixel.
    private const double DragFraction = 1 / 200.0;

    private FrameworkElement? _label;
    private TextBox? _input;
    private FrameworkElement? _scrubSource;
    private double _scrubLastX;
    private double _scrubExact;
    private bool _scrubMoved;
    private bool _nudged;
    private string? _shownText;

    public NumberField()
    {
        SetResourceReference(StyleProperty, typeof(NumberField));
    }

    public event RoutedEventHandler EditCompleted
    {
        add => AddHandler(EditCompletedEvent, value);
        remove => RemoveHandler(EditCompletedEvent, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public Brush? LabelBrush
    {
        get => (Brush?)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public bool IsInteger
    {
        get => (bool)GetValue(IsIntegerProperty);
        set => SetValue(IsIntegerProperty, value);
    }

    public bool IsMixed
    {
        get => (bool)GetValue(IsMixedProperty);
        set => SetValue(IsMixedProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public static bool GetDragsField(DependencyObject element) => (bool)element.GetValue(DragsFieldProperty);

    public static void SetDragsField(DependencyObject element, bool value) => element.SetValue(DragsFieldProperty, value);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_label is not null)
            _label.MouseLeftButtonDown -= OnLabelDown;

        if (_input is not null)
        {
            _input.PreviewKeyDown -= OnInputKeyDown;
            _input.PreviewKeyUp -= OnInputKeyUp;
            _input.PreviewMouseWheel -= OnInputWheel;
            _input.LostKeyboardFocus -= OnInputLostFocus;
        }

        _label = GetTemplateChild("PART_Label") as FrameworkElement;
        _input = GetTemplateChild("PART_Input") as TextBox;
        if (_input is not null)
            CommitTextBox.PassUndoToEditor(_input);

        if (_label is not null)
            _label.MouseLeftButtonDown += OnLabelDown;

        if (_input is not null)
        {
            _input.PreviewKeyDown += OnInputKeyDown;
            _input.PreviewKeyUp += OnInputKeyUp;
            _input.PreviewMouseWheel += OnInputWheel;
            _input.LostKeyboardFocus += OnInputLostFocus;
        }

        UpdateText();
    }

    // ----- Scrubbing -----

    private void OnLabelDown(object sender, MouseButtonEventArgs e) => StartScrub((FrameworkElement)sender, e);

    /// <summary>Starts dragging the value by <paramref name="source"/> (the label, or a name beside the field).</summary>
    public void StartScrub(FrameworkElement source, MouseButtonEventArgs e)
    {
        if (_scrubSource is not null || !source.CaptureMouse())
            return;
        _scrubSource = source;
        _scrubLastX = e.GetPosition(this).X;
        _scrubExact = Value;
        _scrubMoved = false;
        source.MouseMove += OnScrubMove;
        source.MouseLeftButtonUp += OnScrubUp;
        source.LostMouseCapture += OnScrubLostCapture;
        e.Handled = true;
    }

    private void OnScrubMove(object sender, MouseEventArgs e)
    {
        var x = e.GetPosition(this).X;
        var dx = x - _scrubLastX;
        _scrubLastX = x;
        if (dx == 0)
            return;
        _scrubMoved = true;
        // Step by step, so the further from 0 it gets the faster it goes, and dragging back retraces it.
        var perPixel = DragStep(_scrubExact, Step, IsInteger) * ModifierScale();
        _scrubExact += dx * perPixel;
        SetCommitted(RoundTo(_scrubExact, Increment(perPixel)));
    }

    private void OnScrubUp(object sender, MouseButtonEventArgs e) => _scrubSource?.ReleaseMouseCapture();

    private void OnScrubLostCapture(object sender, MouseEventArgs e)
    {
        if (_scrubSource is not { } source)
            return;
        source.MouseMove -= OnScrubMove;
        source.MouseLeftButtonUp -= OnScrubUp;
        source.LostMouseCapture -= OnScrubLostCapture;
        _scrubSource = null;
        if (_scrubMoved)
            RaiseEvent(new RoutedEventArgs(EditCompletedEvent, this));
        else if (_input is not null)
        {
            // A click without a drag goes to the box, to type in.
            _input.Focus();
            _input.SelectAll();
        }
    }

    private static double ModifierScale() =>
        Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1
        : Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 10
        : 1;

    /// <summary>
    /// How much a pixel of drag changes <paramref name="value"/>: a 200th of it, but at least <paramref name="step"/> (a
    /// fifth for whole numbers, so they move every few pixels).
    /// </summary>
    public static double DragStep(double value, double step, bool isInteger = false) =>
        Math.Max(Math.Abs(value) * DragFraction, isInteger ? Math.Max(step, 0.2) : step);

    /// <summary>
    /// How much Up, Down or a turn of the wheel changes <paramref name="value"/>: a tenth of its order of magnitude (1 for
    /// 70, 0.1 for 7, 0.01 for 0.5), 0.1 from 0, and at least 1 for whole numbers.
    /// </summary>
    public static double NudgeStep(double value, bool isInteger = false)
    {
        var step = value == 0 ? 0.1 : Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(value))) - 1);
        step = Math.Max(step, 0.0001);
        return isInteger ? Math.Max(1, Math.Round(step)) : step;
    }

    // What a drag rounds to at this many units a pixel: the next power of ten up (0.35 a pixel lands on whole numbers).
    private static double Increment(double perPixel) => Math.Pow(10, Math.Ceiling(Math.Log10(perPixel)));

    private static double RoundTo(double value, double increment) =>
        Math.Round(Math.Round(value / increment) * increment, 6);

    // A name beside a number field: drags it, and shows the sideways arrows when there's one to drag.
    private static void OnDragsFieldChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;
        element.MouseEnter -= OnOuterLabelEnter;
        element.MouseLeftButtonDown -= OnOuterLabelDown;
        if (e.NewValue is true)
        {
            element.MouseEnter += OnOuterLabelEnter;
            element.MouseLeftButtonDown += OnOuterLabelDown;
        }
    }

    private static void OnOuterLabelEnter(object sender, MouseEventArgs e)
    {
        var label = (FrameworkElement)sender;
        label.Cursor = FieldNear(label) is { IsEnabled: true } ? Cursors.SizeWE : null;
    }

    private static void OnOuterLabelDown(object sender, MouseButtonEventArgs e)
    {
        if (FieldNear((FrameworkElement)sender) is { IsEnabled: true } field)
            field.StartScrub((FrameworkElement)sender, e);
    }

    /// <summary>
    /// The number field an element beside it drags: the only one inside the nearest of its parents that holds any (a few
    /// levels up at most). Null when that parent holds several, or none is near.
    /// </summary>
    public static NumberField? FieldNear(DependencyObject element)
    {
        var parent = VisualTreeHelper.GetParent(element);
        for (var level = 0; parent is not null && level < 4; level++, parent = VisualTreeHelper.GetParent(parent))
        {
            var fields = Inside(parent).Take(2).ToList();
            if (fields.Count > 0)
                return fields.Count == 1 ? fields[0] : null;
        }

        return null;
    }

    private static IEnumerable<NumberField> Inside(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is NumberField field)
                yield return field;
            else
            {
                foreach (var inner in Inside(child))
                    yield return inner;
            }
        }
    }

    // ----- Nudging -----

    /// <summary>
    /// Changes the value by one step up (<paramref name="direction"/> 1) or down (-1), the step growing with the value
    /// (<see cref="NudgeStep"/>; Shift for a tenth of it, Ctrl ten times). The undo step stays open until the keys are let
    /// go or the field is left, so a run of nudges undoes at once.
    /// </summary>
    public void Nudge(int direction)
    {
        // Anything typed but not committed yet is taken first.
        if (_input is not null && _input.Text != _shownText && TryParse(_input.Text, out var typed))
            SetCommitted(typed);
        var step = NudgeStep(Value, IsInteger) * ModifierScale();
        if (IsInteger)
            step = Math.Max(1, Math.Round(step));
        SetCommitted(RoundTo(IsMixed ? Value : Value + direction * step, step));
        _nudged = true;
        UpdateText(force: true);
        _input?.SelectAll();
    }

    private void EndNudges()
    {
        if (!_nudged)
            return;
        _nudged = false;
        RaiseEvent(new RoutedEventArgs(EditCompletedEvent, this));
    }

    private void OnInputKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
            EndNudges();
    }

    // Only while it's being typed in: otherwise the wheel scrolls the panel as usual.
    private void OnInputWheel(object sender, MouseWheelEventArgs e)
    {
        if (_input is not { IsKeyboardFocusWithin: true })
            return;
        Nudge(Math.Sign(e.Delta));
        e.Handled = true;
    }

    // ----- Typing -----

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down)
        {
            Nudge(e.Key == Key.Up ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Commit();
            _input?.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            UpdateText(force: true);
            _input?.SelectAll();
            e.Handled = true;
        }
    }

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Commit();
        EndNudges();
    }

    private void Commit()
    {
        if (_input is null)
            return;

        // Only what was typed is committed: leaving a field that still shows an old value (its entity's, before the
        // selection changed) mustn't write that value anywhere. A mixed field takes any number typed, even the one
        // it would show.
        if (_input.Text != _shownText && TryParse(_input.Text, out var parsed) && (parsed != Value || IsMixed))
        {
            SetCommitted(parsed);
            RaiseEvent(new RoutedEventArgs(EditCompletedEvent, this));
        }

        UpdateText(force: true);
    }

    private void SetCommitted(double value)
    {
        var mixed = IsMixed;
        Value = IsInteger ? Math.Round(value) : value;
        // Setting the value it already has wouldn't reach the binding, but the other entities still need it.
        if (mixed)
            GetBindingExpression(ValueProperty)?.UpdateSource();
    }

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    // While it's being typed in, the text is left alone, unless nothing has been typed yet.
    private void UpdateText(bool force = false)
    {
        if (_input is null || (_input.IsKeyboardFocusWithin && !force && _input.Text != _shownText))
            return;

        _input.Text = _shownText = IsMixed ? "—"
            : IsInteger ? Math.Round(Value).ToString(CultureInfo.InvariantCulture)
            : Value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// TextBox that pushes its binding on Enter or focus loss and reverts on Escape.
/// Dropping an asset from the Assets panel fills in its path.
/// </summary>
public class CommitTextBox : TextBox
{
    /// <summary>Same as AssetNode.DataFormat; kept here so the theme doesn't depend on features.</summary>
    public const string AssetDataFormat = "Griff.Asset";

    public CommitTextBox()
    {
        SetResourceReference(StyleProperty, typeof(TextBox));
        IsUndoEnabled = false;
        PassUndoToEditor(this);
        AllowDrop = true;
    }

    /// <summary>
    /// Lets Ctrl+Z and Ctrl+Y reach the editor's Undo and Redo from a box that has no undo of its own: otherwise the
    /// text box's own bindings take the keys and do nothing with them.
    /// </summary>
    public static void PassUndoToEditor(TextBox box)
    {
        if (box.IsUndoEnabled || box.InputBindings.OfType<KeyBinding>().Any(b => b.Command == ApplicationCommands.NotACommand))
            return;
        box.InputBindings.Add(new KeyBinding(ApplicationCommands.NotACommand, Key.Z, ModifierKeys.Control));
        box.InputBindings.Add(new KeyBinding(ApplicationCommands.NotACommand, Key.Y, ModifierKeys.Control));
        box.InputBindings.Add(new KeyBinding(ApplicationCommands.NotACommand, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
    }

    protected override void OnPreviewDragOver(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(AssetDataFormat))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        base.OnPreviewDragOver(e);
    }

    protected override void OnPreviewDrop(DragEventArgs e)
    {
        if (e.Data.GetData(AssetDataFormat) is string path)
        {
            Text = path;
            GetBindingExpression(TextProperty)?.UpdateSource();
            e.Handled = true;
            return;
        }

        base.OnPreviewDrop(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !AcceptsReturn)
        {
            GetBindingExpression(TextProperty)?.UpdateSource();
            SelectAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            GetBindingExpression(TextProperty)?.UpdateTarget();
            SelectAll();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
