using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Art;
using Color = Griff.Color;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// A color field's picker while it's open: a square of saturation and value for the picked hue, a hue strip, an alpha
/// strip, the hex, the Sprite Painter's palette and the colors picked lately. Every change sets the field (on each
/// selected entity); a drag is one undo step, ended by <see cref="Commit"/>, which also puts the color first in Recent.
/// Agents set <see cref="Hue"/>, <see cref="Saturation"/>, <see cref="Value"/>, <see cref="Alpha"/> or <see cref="Hex"/>,
/// or <see cref="Pick"/> a color.
/// </summary>
public sealed class ColorPicker : ObservableObject
{
    /// <summary>How many colors Recent keeps.</summary>
    public const int RecentCount = 12;

    private readonly Func<Color?> _read;
    private readonly Action<Color> _set;
    private readonly Action<Color> _commit;
    private readonly Func<IReadOnlyList<Color>> _recent;
    private readonly Func<Palette> _getPalette;
    private readonly Action<Palette> _setPalette;
    private float _hue, _saturation, _value, _alpha = 1;
    private Color? _shown;
    private bool _changed;

    /// <param name="read">The field's color now (the primary entity's, when the selected entities' colors differ).</param>
    /// <param name="set">Sets the field's color, joining the undo step that's open.</param>
    /// <param name="commit">Ends the undo step and remembers the color in Recent.</param>
    /// <param name="recent">The colors picked lately, most recent first.</param>
    /// <param name="getPalette">The palette to offer, the Sprite Painter's.</param>
    /// <param name="setPalette">Chooses another palette, for the Sprite Painter too.</param>
    public ColorPicker(Func<Color?> read, Action<Color> set, Action<Color> commit, Func<IReadOnlyList<Color>> recent,
        Func<Palette> getPalette, Action<Palette> setPalette)
    {
        _read = read;
        _set = set;
        _commit = commit;
        _recent = recent;
        _getPalette = getPalette;
        _setPalette = setPalette;
        Sync();
        RebuildSwatches();
        RebuildRecent();
    }

    /// <summary>The color it shows (what the field holds, or what was last picked).</summary>
    public Color Color => Color.FromHsv(_hue, _saturation, _value, _alpha);

    /// <summary>The hue, 0 to 360 degrees round the color wheel (0 red, 120 green, 240 blue).</summary>
    public double Hue
    {
        get => _hue;
        set => Change(() => _hue = (float)Math.Clamp(value, 0, 360));
    }

    /// <summary>How strong the color is, 0 (gray) to 1: across the square.</summary>
    public double Saturation
    {
        get => _saturation;
        set => Change(() => _saturation = (float)Math.Clamp(value, 0, 1));
    }

    /// <summary>How bright the color is, 0 (black) to 1: up the square.</summary>
    public double Value
    {
        get => _value;
        set => Change(() => _value = (float)Math.Clamp(value, 0, 1));
    }

    /// <summary>How solid the color is, 0 (see-through) to 1.</summary>
    public double Alpha
    {
        get => _alpha;
        set => Change(() => _alpha = (float)Math.Clamp(value, 0, 1));
    }

    /// <summary>A point in the square: saturation across, value up, as one change (a drag there).</summary>
    public void SetSaturationValue(double saturation, double value) => Change(() =>
    {
        _saturation = (float)Math.Clamp(saturation, 0, 1);
        _value = (float)Math.Clamp(value, 0, 1);
    });

    /// <summary>"#FF004D", or with see-through colors "#FF004D80". Setting a hex sets the color and ends the undo step.</summary>
    public string Hex
    {
        get => HexOf(Color);
        set
        {
            if (Color.TryParse(value, out var color))
                Pick(color);
            OnPropertyChanged();
        }
    }

    /// <summary>Picks a color by its hex ("#29ADFF"), as clicking a swatch does: sets it and ends the undo step.</summary>
    public void Pick(string hex)
    {
        if (!Color.TryParse(hex, out var color))
            throw new ArgumentException($"\"{hex}\" isn't a color: give it as hex, like #29ADFF or #29ADFF80.");
        Pick(color);
    }

    private void Pick(Color color)
    {
        Show(color);
        _set(color);
        _changed = true;
        Commit();
    }

    /// <summary>
    /// Ends the undo step (the mouse let go of the square or a strip), and puts the color first in Recent if it changed.
    /// </summary>
    public void Commit()
    {
        if (!_changed)
            return;
        _changed = false;
        _commit(Color);
        RebuildRecent();
    }

    /// <summary>Catches up with the field when it changed some other way (undo, typing its hex), keeping the hue of grays.</summary>
    public void Sync()
    {
        if (_read() is not { } color || (_shown is { } shown && shown.ToHex() == color.ToHex()))
            return;
        Show(color);
    }

    private void Show(Color color)
    {
        var (hue, saturation, value) = color.ToHsv();
        // Black has no saturation and grays no hue to read back: keep what was picked.
        if (value > 0)
        {
            if (saturation > 0)
                _hue = hue;
            _saturation = saturation;
        }

        _value = value;
        _alpha = color.A;
        _shown = color;
        Changed();
    }

    private void Change(Action change)
    {
        change();
        _shown = Color;
        _set(_shown.Value);
        _changed = true;
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(string.Empty);
        foreach (var swatch in Swatches.Concat(Recent))
            swatch.Refresh();
    }

    // ----- What it draws with

    /// <summary>The color, solid or see-through.</summary>
    public Brush Brush => BrushOf(Color);

    /// <summary>The color with no see-through: the far end of the alpha strip.</summary>
    public Brush SolidBrush => BrushOf(Color with { A = 1 });

    /// <summary>The picked hue at its brightest: the square's top right corner.</summary>
    public Brush HueBrush => BrushOf(Color.FromHsv(_hue, 1, 1));

    // ----- Palette and recent colors

    /// <summary>The palettes to choose from, the Sprite Painter's.</summary>
    public IReadOnlyList<Palette> Palettes => Palette.All;

    /// <summary>The palette whose colors it offers: the one last picked here or in the Sprite Painter.</summary>
    public Palette SelectedPalette
    {
        get => _getPalette();
        set
        {
            if (value is null)
                return;
            _setPalette(value);
            RebuildSwatches();
            OnPropertyChanged();
        }
    }

    /// <summary>The palette's colors, each picked with a click.</summary>
    public IReadOnlyList<ColorSwatch> Swatches { get; private set; } = [];

    /// <summary>The colors picked lately in any color field, most recent first.</summary>
    public IReadOnlyList<ColorSwatch> Recent { get; private set; } = [];

    public bool HasRecent => Recent.Count > 0;

    private void RebuildSwatches()
    {
        Swatches = [.. SelectedPalette.Colors.Select(c => new ColorSwatch(Color.FromBytes((byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)(c >> 24)), this))];
        OnPropertyChanged(nameof(Swatches));
    }

    private void RebuildRecent()
    {
        Recent = [.. _recent().Take(RecentCount).Select(c => new ColorSwatch(c, this))];
        OnPropertyChanged(nameof(Recent));
        OnPropertyChanged(nameof(HasRecent));
    }

    /// <summary>"#FF004D", with the alpha only when it isn't solid.</summary>
    public static string HexOf(Color color) => color.AByte == 255 ? color.ToHex()[..7] : color.ToHex();

    internal static Brush BrushOf(Color color)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(color.AByte, color.RByte, color.GByte, color.BByte));
        brush.Freeze();
        return brush;
    }
}

/// <summary>A color to pick in a color picker: one of the palette's, or one picked lately.</summary>
public sealed class ColorSwatch(Color color, ColorPicker owner) : ObservableObject
{
    public Color Color { get; } = color;

    /// <summary>"#29ADFF".</summary>
    public string Hex { get; } = ColorPicker.HexOf(color);

    public Brush Brush { get; } = ColorPicker.BrushOf(color);

    /// <summary>True when it's the color picked now.</summary>
    public bool IsCurrent => owner.Color.ToHex() == Color.ToHex();

    public CommunityToolkit.Mvvm.Input.IRelayCommand PickCommand => _pick ??= new CommunityToolkit.Mvvm.Input.RelayCommand(() => owner.Pick(Hex));

    private CommunityToolkit.Mvvm.Input.IRelayCommand? _pick;

    internal void Refresh() => OnPropertyChanged(nameof(IsCurrent));

    public override string ToString() => IsCurrent ? $"{Hex} (picked)" : Hex;
}
