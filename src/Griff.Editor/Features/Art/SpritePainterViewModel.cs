using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Art;

namespace Griff.Editor.Features.Art;

public enum PaintTool
{
    Pencil,
    Eraser,
    Fill,
    Line,
    Rectangle,
    Ellipse,
    Picker,
}

/// <summary>
/// The Sprite Painter: paints pixel art for sprites (frames played in order) and tilemaps (tiles in a grid), from
/// scratch or from a generated starting point, and saves it as a PNG in the project (Sprites/ and Tiles/) that
/// remembers how it's cut up. Every stroke is one undo step.
/// </summary>
public sealed partial class SpritePainterViewModel : ObservableObject
{
    private const int UndoLimit = 200;
    private static readonly Regex ValidName = new(@"^[A-Za-z0-9 _\-]+$");

    private readonly EditorArt _art;
    private readonly DispatcherTimer _animate;
    private readonly Random _random = new();
    private readonly List<PixelSheet> _undo = [];
    private readonly List<PixelSheet> _redo = [];
    private PixelSheet _sheet;
    private PixelSheet? _strokeStart;
    private (int X, int Y) _strokeFrom, _last;
    private bool _strokeErases;
    private int _frame;
    private int _previewFrame;
    private PaintTool _tool = PaintTool.Pencil;
    private PaintTool _drawingTool = PaintTool.Pencil;
    private uint _color;
    private Palette _palette = Palette.Jam;
    private bool _mirror, _filledShapes, _onion, _showGrid = true;
    private string _name = "sprite";
    private string? _editing;
    private string? _status;
    private SpriteIdea? _lastIdea;
    private (int X, int Y)? _hover;
    private BitmapSource? _previewImage;

    public SpritePainterViewModel(EditorArt art, SheetLayout layout)
    {
        _art = art;
        _sheet = new PixelSheet(layout);
        _color = _palette.Colors[8];
        _animate = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher);
        _animate.Tick += (_, _) => AdvancePreview();

        Tools = Enum.GetValues<PaintTool>().Select(t => new ToolChoice(t, this)).ToList();
        Ideas = Enum.GetValues<SpriteIdea>().Select(i => new IdeaChoice(i, Humanize(i.ToString()), () => Generate(i))).ToList();
        Palettes = Palette.All;

        UndoCommand = new RelayCommand(Undo, () => _undo.Count > 0);
        RedoCommand = new RelayCommand(Redo, () => _redo.Count > 0);
        AddFrameCommand = new RelayCommand(AddFrame, () => Layout.Kind == SheetKind.Sprite && Layout.Frames < SheetLayout.MaxCells);
        RemoveFrameCommand = new RelayCommand(RemoveFrame, () => Layout.Kind == SheetKind.Sprite && Layout.Frames > 1);
        ClearCommand = new RelayCommand(() => Change(s => s.Clear(_frame)));
        FlipCommand = new RelayCommand(() => Change(s => s.Flip(_frame, horizontal: true)));
        FlipVerticalCommand = new RelayCommand(() => Change(s => s.Flip(_frame, horizontal: false)));
        OutlineCommand = new RelayCommand(() => Change(s => s.Outline(_frame, _color)));
        AnotherCommand = new RelayCommand(() => Generate(_lastIdea ?? SpriteIdea.Creature));
        SaveCommand = new RelayCommand(() => Save(), () => _art.AssetsPath is not null);

        _name = SuggestName("sprite");
        RebuildSwatches();
        RebuildFrames();
        RestartPreview();
    }

    /// <summary>Called with the asset path and layout of each picture saved, e.g. to set it on the field that asked for it.</summary>
    public Action<string, SheetLayout>? Saved { get; set; }

    /// <summary>Raised when any pixel, the frame shown or the layout changes, so the canvas redraws.</summary>
    public event EventHandler? PixelsChanged;

    public PixelSheet Sheet => _sheet;

    public SheetLayout Layout => _sheet.Layout;

    // ----- Frames and tiles

    public ObservableCollection<FrameItem> Frames { get; } = [];

    /// <summary>The frame (or tile) being painted, from 0.</summary>
    public int Frame
    {
        get => _frame;
        set
        {
            value = Math.Clamp(value, 0, Math.Max(0, Layout.Frames - 1));
            if (!SetProperty(ref _frame, value))
                return;
            foreach (var item in Frames)
                item.Refresh();
            OnPropertyChanged(nameof(FrameText));
            PixelsChanged?.Invoke(this, EventArgs.Empty);
            if (Layout.Kind == SheetKind.Tileset)
                ShowPreview();
        }
    }

    public string FrameText => Layout.Kind == SheetKind.Tileset
        ? $"Tile {_frame} of {Layout.Frames}"
        : $"Frame {_frame + 1} of {Layout.Frames}";

    public bool IsSprite
    {
        get => Layout.Kind == SheetKind.Sprite;
        set
        {
            if (value && Layout.Kind != SheetKind.Sprite)
                Relayout(SheetLayout.Sprite(Layout.CellWidth, Math.Min(Layout.Frames, 8), Layout.FramesPerSecond) with { CellHeight = Layout.CellHeight });
        }
    }

    public bool IsTileset
    {
        get => Layout.Kind == SheetKind.Tileset;
        set
        {
            if (value && Layout.Kind != SheetKind.Tileset)
                Relayout(SheetLayout.Tileset(Layout.CellWidth, 4, 4) with { CellHeight = Layout.CellHeight });
        }
    }

    public double CellWidth
    {
        get => Layout.CellWidth;
        set => Relayout(Layout with { CellWidth = (int)Math.Round(value) });
    }

    public double CellHeight
    {
        get => Layout.CellHeight;
        set => Relayout(Layout with { CellHeight = (int)Math.Round(value) });
    }

    /// <summary>Tiles across a tileset.</summary>
    public double Columns
    {
        get => Layout.Columns;
        set
        {
            var columns = Math.Clamp((int)Math.Round(value), 1, SheetLayout.MaxCells);
            var rows = Math.Min(Layout.Rows, Math.Max(1, SheetLayout.MaxCells / columns));
            Relayout(Layout with { Columns = columns, Rows = rows, Frames = columns * rows });
        }
    }

    /// <summary>Tiles down a tileset.</summary>
    public double Rows
    {
        get => Layout.Rows;
        set
        {
            var rows = Math.Clamp((int)Math.Round(value), 1, Math.Max(1, SheetLayout.MaxCells / Layout.Columns));
            Relayout(Layout with { Rows = rows, Frames = Layout.Columns * rows });
        }
    }

    public double FramesPerSecond
    {
        get => Layout.FramesPerSecond;
        set
        {
            var layout = (Layout with { FramesPerSecond = (float)value }).Clamped();
            if (layout == Layout)
                return;
            _sheet.Relayout(layout);
            OnPropertyChanged();
            RestartPreview();
        }
    }

    public RelayCommand AddFrameCommand { get; }

    public RelayCommand RemoveFrameCommand { get; }

    // ----- Tools

    public IReadOnlyList<ToolChoice> Tools { get; }

    public PaintTool Tool
    {
        get => _tool;
        set
        {
            if (!SetProperty(ref _tool, value))
                return;
            if (value != PaintTool.Picker)
                _drawingTool = value;
            foreach (var tool in Tools)
                tool.Refresh();
            OnPropertyChanged(nameof(IsShapeTool));
        }
    }

    public bool IsShapeTool => _tool is PaintTool.Rectangle or PaintTool.Ellipse;

    /// <summary>Everything drawn is drawn again mirrored left to right, for symmetrical sprites.</summary>
    public bool Mirror
    {
        get => _mirror;
        set
        {
            if (SetProperty(ref _mirror, value))
                PixelsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool FilledShapes
    {
        get => _filledShapes;
        set => SetProperty(ref _filledShapes, value);
    }

    /// <summary>Shows the frame before faintly under this one, to line up an animation.</summary>
    public bool Onion
    {
        get => _onion;
        set
        {
            if (SetProperty(ref _onion, value))
                PixelsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (SetProperty(ref _showGrid, value))
                PixelsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public RelayCommand ClearCommand { get; }

    public RelayCommand FlipCommand { get; }

    public RelayCommand FlipVerticalCommand { get; }

    public RelayCommand OutlineCommand { get; }

    public RelayCommand UndoCommand { get; }

    public RelayCommand RedoCommand { get; }

    // ----- Colors

    public IReadOnlyList<Palette> Palettes { get; }

    public Palette SelectedPalette
    {
        get => _palette;
        set
        {
            if (value is not null && SetProperty(ref _palette, value))
                RebuildSwatches();
        }
    }

    public ObservableCollection<Swatch> Swatches { get; } = [];

    public uint Color
    {
        get => _color;
        set
        {
            if (!SetProperty(ref _color, value))
                return;
            OnPropertyChanged(nameof(ColorHex));
            OnPropertyChanged(nameof(ColorBrush));
            foreach (var swatch in Swatches)
                swatch.Refresh();
        }
    }

    public string ColorHex
    {
        get
        {
            var (r, g, b, a) = PixelSheet.Unpack(_color);
            return a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}";
        }
        set
        {
            if (Griff.Color.TryParse(value, out var color))
                Color = PixelSheet.Pack(color.RByte, color.GByte, color.BByte, color.AByte);
            OnPropertyChanged();
        }
    }

    public Brush ColorBrush => BrushOf(_color);

    // ----- Starting points

    public IReadOnlyList<IdeaChoice> Ideas { get; }

    public RelayCommand AnotherCommand { get; }

    /// <summary>
    /// Paints a generated picture: over the whole sprite (a creature gets two frames), or into the tile being painted.
    /// </summary>
    public void Generate(SpriteIdea idea)
    {
        _lastIdea = idea;
        var frames = SpriteGenerator.Generate(idea, _random.Next(1, 100000), Layout.CellWidth, Layout.CellHeight);
        Change(sheet =>
        {
            if (Layout.Kind == SheetKind.Tileset)
            {
                sheet.SetCell(_frame, frames[0]);
                return;
            }

            sheet.Relayout(Layout with { Columns = frames.Count, Rows = 1, Frames = frames.Count });
            for (var i = 0; i < frames.Count; i++)
                sheet.SetCell(i, frames[i]);
        });
        if (Layout.Kind == SheetKind.Sprite)
            Frame = 0;
        if (_editing is null)
            Name = SuggestName(idea.ToString().ToLowerInvariant());
        Status = frames.Count > 1 ? $"A new {Humanize(idea.ToString()).ToLowerInvariant()}, {frames.Count} frames" : $"A new {Humanize(idea.ToString()).ToLowerInvariant()}";
    }

    // ----- Preview

    /// <summary>The sprite playing its frames, or a tile repeated three by three.</summary>
    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    /// <summary>The pixel under the mouse, for the status line.</summary>
    public string HoverText => _hover is { } h ? $"{h.X}, {h.Y}" : "";

    // ----- Saving

    /// <summary>The file name to save as, without the folder or ".png".</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(SavePath));
                OnPropertyChanged(nameof(NameError));
            }
        }
    }

    public string Folder => _editing is { } editing && editing.Contains('/')
        ? editing[..editing.LastIndexOf('/')]
        : Layout.Kind == SheetKind.Tileset ? "Tiles" : "Sprites";

    /// <summary>Where Save puts it, e.g. "Sprites/slime.png".</summary>
    public string SavePath => $"{Folder}/{Name.Trim()}.png";

    public string? NameError => string.IsNullOrWhiteSpace(Name) ? "Name it to save it."
        : !ValidName.IsMatch(Name.Trim()) ? "Letters, numbers, spaces, - and _ only."
        : null;

    /// <summary>What just happened: "Saved Sprites/slime.png".</summary>
    public string? Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public RelayCommand SaveCommand { get; }

    /// <summary>Saves it as a PNG in the project. Returns the asset path, or null if it can't.</summary>
    public string? Save()
    {
        if (NameError is not null || _art.AssetsPath is null)
        {
            Status = NameError ?? "Open a project to save pictures into it.";
            return null;
        }

        var assetPath = SavePath;
        try
        {
            _art.Save(_sheet, assetPath);
        }
        catch (IOException e)
        {
            Status = $"Couldn't save: {e.Message}";
            return null;
        }

        _editing = assetPath;
        OnPropertyChanged(nameof(Folder));
        Status = $"Saved {assetPath}";
        Saved?.Invoke(assetPath, Layout);
        return assetPath;
    }

    /// <summary>Opens an image from the project to paint over; saving writes over it. False if it can't be read.</summary>
    public bool Open(string assetPath, SheetLayout? layout = null)
    {
        if (_art.Load(assetPath, layout) is not { } sheet)
            return false;
        Replace(sheet);
        _editing = assetPath;
        Name = Path.GetFileNameWithoutExtension(assetPath);
        OnPropertyChanged(nameof(Folder));
        OnPropertyChanged(nameof(SavePath));
        Status = $"Editing {assetPath}";
        return true;
    }

    /// <summary>Starts a new picture, to be saved as <paramref name="assetPath"/> if given.</summary>
    public void New(SheetLayout layout, string? assetPath = null)
    {
        Replace(new PixelSheet(layout));
        _editing = assetPath;
        Name = assetPath is null ? SuggestName(layout.Kind == SheetKind.Tileset ? "tiles" : "sprite") : Path.GetFileNameWithoutExtension(assetPath);
        OnPropertyChanged(nameof(Folder));
        OnPropertyChanged(nameof(SavePath));
        Status = assetPath is null ? null : $"New picture for {assetPath}";
    }

    // "slime", then "slime2", "slime3"... whichever isn't taken in the folder.
    public string SuggestName(string stem)
    {
        if (_art.AssetsPath is not { } root)
            return stem;
        var folder = Path.Combine(root, Folder.Replace('/', Path.DirectorySeparatorChar));
        for (var i = 1; ; i++)
        {
            var name = i == 1 ? stem : $"{stem}{i}";
            if (!File.Exists(Path.Combine(folder, name + ".png")))
                return name;
        }
    }

    // ----- Painting, called by the canvas with pixel positions in the frame

    public void Hover((int X, int Y)? pixel)
    {
        _hover = pixel;
        OnPropertyChanged(nameof(HoverText));
    }

    /// <summary>Starts a stroke at a pixel; <paramref name="erase"/> (the right button) paints clear instead.</summary>
    public void BeginStroke(int x, int y, bool erase = false, bool pick = false)
    {
        if (pick || _tool == PaintTool.Picker)
        {
            if (_sheet.Get(_frame, x, y) is var picked && (picked != 0 || !erase))
                Color = picked == 0 ? _color : picked;
            if (_tool == PaintTool.Picker)
                Tool = _drawingTool;
            return;
        }

        _strokeStart = _sheet.Copy();
        _strokeFrom = _last = (x, y);
        _strokeErases = erase || _tool == PaintTool.Eraser;
        Draw(x, y);
    }

    public void ContinueStroke(int x, int y)
    {
        if (_strokeStart is null || (x, y) == _last)
            return;
        Draw(x, y);
    }

    public void EndStroke()
    {
        if (_strokeStart is not { } before)
            return;
        _strokeStart = null;
        Commit(before);
    }

    public bool IsStroking => _strokeStart is not null;

    private void Draw(int x, int y)
    {
        var color = _strokeErases ? 0u : _color;
        var w = Layout.CellWidth;
        var mirror = _mirror ? (Func<int, int>)(px => w - 1 - px) : null;
        switch (_tool == PaintTool.Eraser ? PaintTool.Pencil : _tool)
        {
            case PaintTool.Pencil:
                _sheet.Line(_frame, _last.X, _last.Y, x, y, color);
                if (mirror is not null)
                    _sheet.Line(_frame, mirror(_last.X), _last.Y, mirror(x), y, color);
                break;
            case PaintTool.Fill:
                if ((x, y) != _strokeFrom)
                    break;
                _sheet.Fill(_frame, x, y, color);
                if (mirror is not null)
                    _sheet.Fill(_frame, mirror(x), y, color);
                break;
            default:
                // Shapes are drawn again from the start of the stroke each time the mouse moves.
                _sheet.CopyFrom(_strokeStart!);
                Shape(_strokeFrom.X, _strokeFrom.Y, x, y, color);
                if (mirror is not null)
                    Shape(mirror(_strokeFrom.X), _strokeFrom.Y, mirror(x), y, color);
                break;
        }

        _last = (x, y);
        Changed();
    }

    private void Shape(int x0, int y0, int x1, int y1, uint color)
    {
        if (_tool == PaintTool.Line)
            _sheet.Line(_frame, x0, y0, x1, y1, color);
        else if (_tool == PaintTool.Rectangle)
            _sheet.Rectangle(_frame, x0, y0, x1, y1, color, _filledShapes);
        else
            _sheet.Ellipse(_frame, x0, y0, x1, y1, color, _filledShapes);
    }

    // ----- Undo

    private void Change(Action<PixelSheet> change)
    {
        var before = _sheet.Copy();
        change(_sheet);
        Commit(before);
    }

    private void Commit(PixelSheet before)
    {
        if (_sheet.SameAs(before))
            return;
        _undo.Add(before);
        if (_undo.Count > UndoLimit)
            _undo.RemoveAt(0);
        _redo.Clear();
        AfterLayoutChange(before.Layout);
    }

    public void Undo() => Step(_undo, _redo);

    public void Redo() => Step(_redo, _undo);

    private void Step(List<PixelSheet> from, List<PixelSheet> to)
    {
        if (from.Count == 0)
            return;
        var before = _sheet.Layout;
        to.Add(_sheet.Copy());
        _sheet.CopyFrom(from[^1]);
        from.RemoveAt(from.Count - 1);
        AfterLayoutChange(before);
    }

    private void Relayout(SheetLayout layout)
    {
        layout = layout.Clamped();
        if (layout == Layout)
            return;
        var kind = Layout.Kind;
        Change(s => s.Relayout(layout));
        if (_editing is null && layout.Kind != kind)
            Name = SuggestName(layout.Kind == SheetKind.Tileset ? "tiles" : "sprite");
    }

    private void Replace(PixelSheet sheet)
    {
        var before = _sheet.Layout;
        _sheet = sheet;
        _undo.Clear();
        _redo.Clear();
        _frame = 0;
        AfterLayoutChange(before);
    }

    private void AddFrame()
    {
        var at = _frame + 1;
        Change(s => s.InsertCell(at, copyOf: _frame));
        Frame = at;
    }

    private void RemoveFrame() => Change(s => s.RemoveCell(_frame));

    // ----- Keeping the views up to date

    private void AfterLayoutChange(SheetLayout before)
    {
        if (_frame >= Layout.Frames)
            _frame = Layout.Frames - 1;
        if (Layout.Frames != Frames.Count || before.Kind != Layout.Kind)
            RebuildFrames();
        foreach (var item in Frames)
            item.Refresh();
        OnPropertyChanged(string.Empty);
        RestartPreview();
        Changed();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        AddFrameCommand.NotifyCanExecuteChanged();
        RemoveFrameCommand.NotifyCanExecuteChanged();
    }

    private void Changed()
    {
        if (_frame < Frames.Count)
            Frames[_frame].Refresh();
        PixelsChanged?.Invoke(this, EventArgs.Empty);
        if (Layout.Kind == SheetKind.Tileset || Layout.Frames == 1)
            ShowPreview();
    }

    private void RebuildFrames()
    {
        Frames.Clear();
        for (var i = 0; i < Layout.Frames; i++)
            Frames.Add(new FrameItem(this, i));
    }

    private void RebuildSwatches()
    {
        Swatches.Clear();
        foreach (var color in _palette.Colors)
            Swatches.Add(new Swatch(color, this));
    }

    private void RestartPreview()
    {
        _animate.Stop();
        _previewFrame = Math.Min(_previewFrame, Layout.Frames - 1);
        ShowPreview();
        if (Layout.Kind == SheetKind.Sprite && Layout.Frames > 1)
        {
            _animate.Interval = TimeSpan.FromSeconds(1 / Math.Max(1, Layout.FramesPerSecond));
            _animate.Start();
        }
    }

    private void AdvancePreview()
    {
        _previewFrame = (_previewFrame + 1) % Math.Max(1, Layout.Frames);
        ShowPreview();
    }

    private void ShowPreview()
    {
        var (w, h) = (Layout.CellWidth, Layout.CellHeight);
        if (Layout.Kind == SheetKind.Tileset)
        {
            var tile = _sheet.Cell(_frame);
            var tiled = new uint[w * 3 * h * 3];
            for (var y = 0; y < h * 3; y++)
                for (var x = 0; x < w * 3; x++)
                    tiled[y * w * 3 + x] = tile[(y % h) * w + x % w];
            PreviewImage = Bitmap(tiled, w * 3, h * 3);
        }
        else
            PreviewImage = Bitmap(_sheet.Cell(Math.Min(_previewFrame, Layout.Frames - 1)), w, h);
    }

    internal BitmapSource CellBitmap(int cell) => Bitmap(_sheet.Cell(cell), Layout.CellWidth, Layout.CellHeight);

    internal static BitmapSource Bitmap(uint[] pixels, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    internal static Brush BrushOf(uint color)
    {
        var (r, g, b, a) = PixelSheet.Unpack(color);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Stops the preview playing, when the window closes.</summary>
    public void Stop() => _animate.Stop();

    private static string Humanize(string name) => Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
}

/// <summary>A tool button.</summary>
public sealed class ToolChoice(PaintTool tool, SpritePainterViewModel owner) : ObservableObject
{
    public PaintTool Tool { get; } = tool;

    public string Label => Tool.ToString();

    /// <summary>The key that picks it.</summary>
    public string Key => Tool switch
    {
        PaintTool.Pencil => "B",
        PaintTool.Eraser => "E",
        PaintTool.Fill => "G",
        PaintTool.Line => "L",
        PaintTool.Rectangle => "R",
        PaintTool.Ellipse => "O",
        _ => "I",
    };

    public string Tip => Tool switch
    {
        PaintTool.Pencil => "Pencil (B): draw; right-drag to erase",
        PaintTool.Eraser => "Eraser (E)",
        PaintTool.Fill => "Fill (G): fill an area of one color",
        PaintTool.Line => "Line (L): drag from one end to the other",
        PaintTool.Rectangle => "Rectangle (R): drag from corner to corner",
        PaintTool.Ellipse => "Ellipse (O): drag from corner to corner",
        _ => "Pick a color (I), or Alt-click with any tool",
    };

    public bool IsSelected
    {
        get => owner.Tool == Tool;
        set
        {
            if (value)
                owner.Tool = Tool;
            OnPropertyChanged();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>A color of the palette.</summary>
public sealed class Swatch(uint color, SpritePainterViewModel owner) : ObservableObject
{
    public uint Color { get; } = color;

    public Brush Brush { get; } = SpritePainterViewModel.BrushOf(color);

    public string Hex => $"#{Color & 0xFFFFFF:X6}";

    public bool IsSelected
    {
        get => owner.Color == Color;
        set
        {
            if (value)
            {
                owner.Color = Color;
                if (owner.Tool is PaintTool.Eraser or PaintTool.Picker)
                    owner.Tool = PaintTool.Pencil;
            }

            OnPropertyChanged();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(IsSelected));
}

/// <summary>A starting point the generator can make.</summary>
public sealed class IdeaChoice(SpriteIdea idea, string label, Action generate)
{
    public SpriteIdea Idea { get; } = idea;

    public string Label { get; } = label;

    public RelayCommand Command { get; } = new(generate);
}

/// <summary>One frame (or tile) in the strip along the bottom.</summary>
public sealed class FrameItem(SpritePainterViewModel owner, int index) : ObservableObject
{
    public int Index { get; } = index;

    /// <summary>Frames count from 1; tiles from 0, as a tilemap's rows number them.</summary>
    public string Label => owner.Layout.Kind == SheetKind.Tileset ? Index.ToString(CultureInfo.InvariantCulture) : (Index + 1).ToString(CultureInfo.InvariantCulture);

    public BitmapSource Image => owner.CellBitmap(Index);

    public bool IsSelected
    {
        get => owner.Frame == Index;
        set
        {
            if (value)
                owner.Frame = Index;
            OnPropertyChanged();
        }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}
