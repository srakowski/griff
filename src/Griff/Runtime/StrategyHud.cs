using System.Numerics;

namespace Griff.Runtime;

/// <summary>A rectangle on the strategy HUD, in world units (the HUD is laid out over the camera's view).</summary>
public readonly record struct HudRect(float X, float Y, float Width, float Height)
{
    public Vector2 Center => new(X + Width / 2, Y + Height / 2);

    public float Top => Y + Height;

    public float Right => X + Width;

    public bool Contains(Vector2 point) => point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Y + Height;
}

/// <summary>A button on the command card: what it says, its key, and what pressing it does.</summary>
internal sealed record CardButton(string Label, string Key, string Tip, bool Enabled, Action Press)
{
    /// <summary>A prefab to show the color of, e.g. the unit it trains.</summary>
    public Color? Swatch { get; init; }
}

/// <summary>
/// Where everything on the strategy HUD goes for a view: the resources bar along the top, and along the bottom the
/// minimap, what's selected and the command card. Sized from the view's height, so it looks the same at any zoom.
/// </summary>
public sealed class StrategyHud
{
    private readonly Vector2 _mapCenter;
    private readonly Vector2 _mapSize;

    public const int CardColumns = 3;
    public const int CardRows = 2;
    public const int TileColumns = 10;
    public const int TileRows = 2;

    public StrategyHud(Vector2 viewCenter, Vector2 viewHalf, Vector2 mapCenter, Vector2 mapSize)
    {
        var height = viewHalf.Y * 2;
        var width = viewHalf.X * 2;
        var left = viewCenter.X - viewHalf.X;
        var bottom = viewCenter.Y - viewHalf.Y;
        Unit = height / 100;
        (_mapCenter, _mapSize) = (mapCenter, mapSize);

        View = new HudRect(left, bottom, width, height);
        TopBar = new HudRect(left, bottom + height - 4.5f * Unit, width, 4.5f * Unit);
        Panel = new HudRect(left, bottom, width, 22 * Unit);

        var margin = 1.2f * Unit;
        var inner = Panel.Height - 2 * margin;
        var aspect = mapSize.Y > 0 ? mapSize.X / mapSize.Y : 1;
        var minimapWidth = Math.Min(inner * aspect, width * 0.3f);
        Minimap = new HudRect(left + margin, bottom + margin, minimapWidth, minimapWidth / aspect);

        var gap = 0.8f * Unit;
        Button = (inner - gap * (CardRows - 1)) / CardRows;
        var cardWidth = CardColumns * Button + (CardColumns - 1) * gap;
        Card = new HudRect(left + width - margin - cardWidth, bottom + margin, cardWidth, inner);
        Gap = gap;

        var infoLeft = Minimap.Right + 2 * margin;
        Info = new HudRect(infoLeft, bottom + margin, Math.Max(0, Card.X - 2 * margin - infoLeft), inner);
    }

    /// <summary>A hundredth of the view's height: the HUD's unit of size.</summary>
    public float Unit { get; }

    public float Button { get; }

    public float Gap { get; }

    public HudRect View { get; }

    public HudRect TopBar { get; }

    public HudRect Panel { get; }

    public HudRect Minimap { get; }

    public HudRect Card { get; }

    public HudRect Info { get; }

    /// <summary>True if <paramref name="point"/> is on the HUD rather than the battlefield.</summary>
    public bool Covers(Vector2 point) => TopBar.Contains(point) || Panel.Contains(point);

    /// <summary>The command card's button in a slot, counted along the top row first.</summary>
    public HudRect CardSlot(int index)
    {
        var column = index % CardColumns;
        var row = index / CardColumns;
        return new HudRect(Card.X + column * (Button + Gap), Card.Top - (row + 1) * Button - row * Gap, Button, Button);
    }

    /// <summary>A small square for one of several selected things, in rows across the selection area.</summary>
    public HudRect Tile(int index)
    {
        var size = Math.Min(Info.Width / TileColumns, Info.Height / (TileRows + 0.6f)) - 0.6f * Unit;
        var column = index % TileColumns;
        var row = index / TileColumns;
        var step = size + 0.6f * Unit;
        return new HudRect(Info.X + column * step, Info.Top - 5 * Unit - (row + 1) * step, size, size);
    }

    /// <summary>The world point under a spot on the minimap.</summary>
    public Vector2 MinimapToWorld(Vector2 point)
    {
        var t = new Vector2((point.X - Minimap.X) / Minimap.Width, (point.Y - Minimap.Y) / Minimap.Height);
        t = Vector2.Clamp(t, Vector2.Zero, Vector2.One);
        return _mapCenter - _mapSize / 2 + t * _mapSize;
    }

    /// <summary>The spot on the minimap for a world point.</summary>
    public Vector2 WorldToMinimap(Vector2 world)
    {
        var t = (world - (_mapCenter - _mapSize / 2)) / _mapSize;
        return new Vector2(Minimap.X + t.X * Minimap.Width, Minimap.Y + t.Y * Minimap.Height);
    }
}
