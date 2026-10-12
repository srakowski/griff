using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// A color field's picker (its <see cref="ColorPicker"/>): dragging in the square or along a strip sets the color as
/// the mouse moves, one undo step a drag.
/// </summary>
public partial class ColorPickerView : UserControl
{
    private ColorPicker? _picker;

    public ColorPickerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_picker is not null)
                _picker.PropertyChanged -= OnPickerChanged;
            _picker = DataContext as ColorPicker;
            if (_picker is not null)
                _picker.PropertyChanged += OnPickerChanged;
            PlaceThumbs();
        };
    }

    private void OnPickerChanged(object? sender, PropertyChangedEventArgs e) => PlaceThumbs();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => PlaceThumbs();

    // The thumbs sit where the color is: in the square by saturation and value, on the strips by hue and alpha.
    private void PlaceThumbs()
    {
        if (_picker is null)
            return;
        Canvas.SetLeft(SquareThumb, _picker.Saturation * Square.ActualWidth - SquareThumb.Width / 2);
        Canvas.SetTop(SquareThumb, (1 - _picker.Value) * Square.ActualHeight - SquareThumb.Height / 2);
        Canvas.SetLeft(HueThumb, _picker.Hue / 360 * HueStrip.ActualWidth - HueThumb.Width / 2);
        Canvas.SetLeft(AlphaThumb, _picker.Alpha * AlphaStrip.ActualWidth - AlphaThumb.Width / 2);
    }

    private void OnStripDown(object sender, MouseButtonEventArgs e)
    {
        var strip = (FrameworkElement)sender;
        if (!strip.CaptureMouse())
            return;
        Drag(strip, e);
        e.Handled = true;
    }

    private void OnStripMove(object sender, MouseEventArgs e)
    {
        var strip = (FrameworkElement)sender;
        if (strip.IsMouseCaptured)
            Drag(strip, e);
    }

    private void OnStripUp(object sender, MouseButtonEventArgs e)
    {
        var strip = (FrameworkElement)sender;
        if (!strip.IsMouseCaptured)
            return;
        strip.ReleaseMouseCapture();
        _picker?.Commit();
        e.Handled = true;
    }

    private void Drag(FrameworkElement strip, MouseEventArgs e)
    {
        if (_picker is null || strip.ActualWidth <= 0)
            return;
        var at = e.GetPosition(strip);
        var across = Math.Clamp(at.X / strip.ActualWidth, 0, 1);
        if (strip == Square)
            _picker.SetSaturationValue(across, 1 - Math.Clamp(at.Y / strip.ActualHeight, 0, 1));
        else if (strip == HueStrip)
            _picker.Hue = across * 360;
        else
            _picker.Alpha = across;
    }
}

/// <summary>
/// The popup a color field's picker opens in, under the field: open while its <see cref="PropertyField.IsColorOpen"/>
/// is, and shut by a click anywhere else (or Escape).
/// </summary>
public sealed class ColorPickerPopup : Popup
{
    public ColorPickerPopup()
    {
        AllowsTransparency = true;
        StaysOpen = false;
        Placement = PlacementMode.Bottom;
        PopupAnimation = PopupAnimation.None;
        VerticalOffset = 2;
        var view = new ColorPickerView();
        // The popup's own data is the field; the picker is the field's ColorPicker.
        view.SetBinding(DataContextProperty, new Binding($"{nameof(DataContext)}.{nameof(PropertyField.ColorPicker)}") { Source = this });
        view.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                IsOpen = false;
                e.Handled = true;
            }
        };
        Child = view;
        SetBinding(IsOpenProperty, new Binding(nameof(PropertyField.IsColorOpen)) { Mode = BindingMode.TwoWay });
    }
}
