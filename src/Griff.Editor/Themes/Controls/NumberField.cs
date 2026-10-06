using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Griff.Editor.Themes.Controls;

/// <summary>
/// Numeric input. Type a value (Enter or focus loss commits, Esc reverts), or drag the label
/// sideways to scrub: Shift for fine steps, Ctrl for coarse. <see cref="EditCompletedEvent"/>
/// fires once per typed commit or finished drag, so callers can close an undo step.
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

    /// <summary>Change in value per pixel of drag.</summary>
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(NumberField), new PropertyMetadata(0.05));

    public static readonly RoutedEvent EditCompletedEvent = EventManager.RegisterRoutedEvent(
        "EditCompleted", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(NumberField));

    private FrameworkElement? _label;
    private TextBox? _input;
    private bool _scrubbing;
    private Point _scrubOrigin;
    private double _scrubStartValue;

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

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_label is not null)
        {
            _label.MouseLeftButtonDown -= OnLabelDown;
            _label.MouseMove -= OnLabelMove;
            _label.MouseLeftButtonUp -= OnLabelUp;
            _label.LostMouseCapture -= OnLabelLostCapture;
        }

        if (_input is not null)
        {
            _input.KeyDown -= OnInputKeyDown;
            _input.LostKeyboardFocus -= OnInputLostFocus;
        }

        _label = GetTemplateChild("PART_Label") as FrameworkElement;
        _input = GetTemplateChild("PART_Input") as TextBox;

        if (_label is not null)
        {
            _label.MouseLeftButtonDown += OnLabelDown;
            _label.MouseMove += OnLabelMove;
            _label.MouseLeftButtonUp += OnLabelUp;
            _label.LostMouseCapture += OnLabelLostCapture;
        }

        if (_input is not null)
        {
            _input.KeyDown += OnInputKeyDown;
            _input.LostKeyboardFocus += OnInputLostFocus;
        }

        UpdateText();
    }

    // ----- Scrubbing -----

    private void OnLabelDown(object sender, MouseButtonEventArgs e)
    {
        _scrubbing = _label!.CaptureMouse();
        _scrubOrigin = e.GetPosition(this);
        _scrubStartValue = Value;
        e.Handled = true;
    }

    private void OnLabelMove(object sender, MouseEventArgs e)
    {
        if (!_scrubbing)
            return;

        var dx = e.GetPosition(this).X - _scrubOrigin.X;
        var scale = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1
            : Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 10
            : 1;
        var step = IsInteger ? Math.Max(Step, 0.2) : Step;
        SetCommitted(_scrubStartValue + dx * step * scale);
    }

    private void OnLabelUp(object sender, MouseButtonEventArgs e) => _label?.ReleaseMouseCapture();

    private void OnLabelLostCapture(object sender, MouseEventArgs e)
    {
        if (!_scrubbing)
            return;
        _scrubbing = false;
        RaiseEvent(new RoutedEventArgs(EditCompletedEvent, this));
    }

    // ----- Typing -----

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
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

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    private void Commit()
    {
        if (_input is null)
            return;

        if (TryParse(_input.Text, out var parsed) && parsed != Value)
        {
            SetCommitted(parsed);
            RaiseEvent(new RoutedEventArgs(EditCompletedEvent, this));
        }

        UpdateText(force: true);
    }

    private void SetCommitted(double value) => Value = IsInteger ? Math.Round(value) : value;

    private static bool TryParse(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private void UpdateText(bool force = false)
    {
        if (_input is null || (_input.IsKeyboardFocusWithin && !force))
            return;

        _input.Text = IsInteger
            ? Math.Round(Value).ToString(CultureInfo.InvariantCulture)
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
        AllowDrop = true;
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
