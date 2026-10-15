using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.Cards;

/// <summary>
/// A playing card, with no code: it draws itself as a card of its <see cref="Color"/> with its <see cref="Title"/> along
/// the top, its <see cref="Value"/> in the corner, a picture (<see cref="Image"/>) and its rules <see cref="Text"/>, or as
/// its back while it's face down. Put cards inside a <see cref="Deck"/> to draw from, a <see cref="Hand"/> to hold them or
/// a <see cref="PlayZone"/> on the table: each lays out the cards inside it, and a card that goes from one to another
/// slides over. Its <see cref="Kind"/> is what a play zone checks before it takes it.
/// </summary>
/// <remarks>
/// With a Sprite on it, the sprite is its face and the card only draws its back over it. Its back is the one of the deck it
/// belongs to (its <see cref="Home"/>). Components on it that are an <see cref="ICardPart"/> hear when it's drawn, played
/// and discarded, and can say it can't be played: that's how card effects and costs hang off a card. With a
/// <see cref="TableValues"/> its title, text, value, kind and color come from a row of a table (a deck made from a table
/// gives each of its cards one). In a network game, a card in a face-down deck or in another side's hidden hand isn't sent
/// to players who aren't to see it.
/// </remarks>
[Category("Cards")]
[WorksWith(typeof(TableValues), Note = "its title, text, value, kind and color from a row of a table")]
[ComponentName("Griff.Card")]
public sealed class Card : Component, IDrawable, IDrawEffect, INetSecret, IPlaytestReport
{
    private string? _text;
    private float _wrappedAt;
    private string[] _lines = [];

    /// <summary>What it's called, written along its top. Empty writes its entity's name.</summary>
    public string? Title { get; set; }

    /// <summary>What it does, written under its picture and wrapped to fit, e.g. "Deal 3 damage."</summary>
    public string? Text
    {
        get => _text;
        set
        {
            _text = value;
            _wrappedAt = 0;
        }
    }

    /// <summary>
    /// A number in its top-left corner: its rank, its strength, its points. 0 shows none. A card with no picture and no text
    /// shows it big in its middle too, like a playing card.
    /// </summary>
    [Output]
    public int Value { get; set; }

    /// <summary>What kind of card it is, e.g. "Spell" or "Hearts": a play zone's Accepts names the kinds it takes.</summary>
    public string? Kind { get; set; }

    /// <summary>The color of its face.</summary>
    [Group("Look")]
    public Color Color { get; set; } = Color.FromBytes(0xF4, 0xEE, 0xE0);

    /// <summary>The color of its writing.</summary>
    public Color TextColor { get; set; } = Color.FromBytes(0x2A, 0x26, 0x30);

    /// <summary>A picture drawn on its face, e.g. "Art/fireball.png".</summary>
    public string? Image { get; set; }

    /// <summary>How big it is, in world units: 1.6 by 2.24 has a playing card's shape.</summary>
    public Vector2 Size { get; set; } = new(1.6f, 2.24f);

    /// <summary>The font of its writing (see Text's Font). Empty uses the built-in one.</summary>
    [Names(NameKind.Font)]
    public string? Font { get; set; }

    /// <summary>
    /// True while its face shows. A deck turns the cards put in it face down (a face-up deck, such as a discard pile, face
    /// up), a hand face up and a play zone as its Face Up says. A hidden hand shows its faces only to its own side.
    /// </summary>
    [Group("State")]
    public bool FaceUp { get; set; } = true;

    /// <summary>
    /// The deck it belongs to, by name: its back is that deck's, and the deck's Restart gathers it back. Set by the game as
    /// it starts for the cards in each deck that have none.
    /// </summary>
    [EntityName]
    public string? Home { get; set; }

    /// <summary>The place it's in (the deck, hand or play zone it's inside), or null.</summary>
    [DoNotSerialize]
    public CardZone? Zone => Entity?.Parent?.GetComponent<CardZone>();

    /// <summary>True once it has slid into its place in its deck, hand or play zone.</summary>
    [DoNotSerialize]
    public bool IsResting => Resting;

    /// <summary>Raised as it's drawn into a hand, with the hand.</summary>
    public event Action<Card, Hand>? Drawn;

    /// <summary>Raised as it's played, with the play zone.</summary>
    public event Action<Card, PlayZone>? Played;

    /// <summary>Raised as it's put on a discard pile (or any deck), with the deck.</summary>
    public event Action<Card, Deck>? Discarded;

    /// <summary>The title it shows: <see cref="Title"/>, or its entity's name.</summary>
    public string ShownTitle => string.IsNullOrWhiteSpace(Title) ? Entity?.Name ?? "" : Title;

    internal void RaiseDrawn(Hand hand) => Drawn?.Invoke(this, hand);

    internal void RaisePlayed(PlayZone zone) => Played?.Invoke(this, zone);

    internal void RaiseDiscarded(Deck pile) => Discarded?.Invoke(this, pile);

    // ----- Kept by the game while it runs -----

    // In its place in its zone, not sliding: sent, so players' machines know a card has settled into a deck's pile.
    [Sent]
    internal bool Resting { get; set; }

    // The zone it last came to rest in: a card in another is travelling there, and draws over everything on its way.
    internal Entity? RestedIn { get; set; }

    internal bool Travelling { get; set; }

    // Drawn as part of its deck's pile rather than by itself.
    internal bool InPile { get; set; }

    // Shown face down on this machine: in a hidden hand of a side this player isn't on.
    internal bool HiddenHere { get; set; }

    // Lit with a hand's highlight on this machine (under the mouse, picked, the keyboard's cursor on it), and in what color.
    internal Color Highlight { get; set; }

    // How far it's lifted out of its hand on this machine (0 to 1: hovered, picked, dragged), and how much a lift raises
    // and enlarges it; and how far it's drawn from where it is (dragged, or easing back after a drag).
    internal Smoothed<float> Lift;
    internal Smoothed<Vector2> Offset;
    internal float LiftRise { get; set; }
    internal float LiftScale { get; set; } = 1;
    internal bool Dragged { get; set; }

    // Its back: its home deck's picture and color.
    internal string? BackImage { get; set; }
    internal Color BackColor { get; set; } = CardLook.DefaultBack;

    // Showing its back (here): face down, or hidden from this machine's player.
    internal bool ShowsBack => !FaceUp || HiddenHere;

    // ----- Drawing -----

    int? IDrawable.DrawOrder => 5;

    int IDrawable.DrawStep => DrawSteps.Sprite + 10;

    bool IDrawEffect.DrawsOnTop => Travelling || Dragged || Lift.Value > 0.01f || Lift.Was > 0.01f;

    // Lifted out of its hand: raised, turned upright and drawn bigger about its middle; dragged, drawn where the mouse has it.
    Matrix4x4? IDrawEffect.DrawTransform
    {
        get
        {
            var blend = StepBlend.For(Entity!);
            var lift = blend.Of(Lift);
            var offset = blend.Of(Offset);
            if (lift <= 0.0005f && offset == Vector2.Zero)
                return null;
            var world = Entity!.Transform.WorldMatrix;
            var center = new Vector2(world.M41, world.M42);
            var angle = MathF.Atan2(world.M12, world.M11);
            var scale = 1 + (LiftScale - 1) * lift;
            return Matrix4x4.CreateTranslation(-center.X, -center.Y, 0) * Matrix4x4.CreateRotationZ(-angle * lift) *
                   Matrix4x4.CreateScale(scale, scale, 1) *
                   Matrix4x4.CreateTranslation(center.X + offset.X, center.Y + offset.Y + LiftRise * lift, 0);
        }
    }

    // Face down, its other drawables (a sprite face, a text on it) don't show.
    Color IDrawEffect.Tint(IDrawable drawn, Color color) => ShowsBack && !ReferenceEquals(drawn, this) ? Color.Transparent : color;

    void IDrawable.Draw(Drawing drawing)
    {
        var entity = Entity!;
        // In a deck, its pile draws it (in the editor too, where nothing has been laid out).
        if (drawing.IsPlaying ? InPile : entity.Parent?.GetComponent<Deck>() is not null)
            return;
        var world = entity.Transform.WorldMatrix;
        if (ShowsBack)
            CardLook.Back(drawing, this, world, Size, BackImage, BackColor);
        else if (entity.GetComponent<Sprite>() is null)
            CardLook.Face(drawing, this, world);
        if (Highlight.A > 0)
            CardLook.Outline(drawing, this, world, Size + new Vector2(0.08f), 0.06f, drawing.Tint(this, Highlight));
    }

    // Its rules text in lines that fit across it at `size`, worked out again only when the text or size changes.
    internal string[] Lines(float size, float width)
    {
        var at = size * 1000 + width;
        if (at == _wrappedAt)
            return _lines;
        _wrappedAt = at;
        _lines = CardLook.Wrap(_text, size, width);
        return _lines;
    }

    // ----- Networking and playtesting -----

    // A card resting in a face-down deck is nobody's business; one in a hidden hand is only its side's.
    bool INetSecret.HiddenFrom(int player, string team)
    {
        if (Entity?.Parent is not { } parent)
            return false;
        if (parent.GetComponent<Deck>() is { FaceUp: false } && Resting)
            return true;
        return parent.GetComponent<Hand>() is { Hidden: true } hand && hand.Side is { Length: > 0 } side && !TurnOrder.SameSide(side, team);
    }

    // Where it is: ", in Hand (2 of 5)", ", in Deck (top)", ", on Table", and whether it's face down.
    string IPlaytestReport.Report()
    {
        var text = "";
        if (Zone is { } zone)
        {
            var count = zone.Cards.Count;
            var index = zone.IndexOf(this);
            text = zone switch
            {
                Hand => $", in {zone.Entity!.Name} ({index + 1} of {count})",
                Deck => $", in {zone.Entity!.Name}{(index == count - 1 ? " (top)" : "")}",
                _ => $", on {zone.Entity!.Name}",
            };
        }

        if (!FaceUp)
            text += ", face down";
        return text + (Resting || Zone is null ? "" : ", sliding into place");
    }
}

/// <summary>
/// A component on a card's entity (beside its <see cref="Card"/>) that hears what happens to the card and has a say in
/// whether it can be played: an effect that deals damage as it's played, a cost that has to be paid first. A script
/// component can be one too. Every method has a default, so a part needs only what it uses.
/// </summary>
/// <example>
/// <code>
/// // In a script on a card:
/// public class Heal : Behavior, ICardPart
/// {
///     public void Played(Card card, PlayZone zone) => Game.Find("Player")?.GetComponent&lt;Health&gt;()?.Heal(2);
/// }
/// </code>
/// </example>
public interface ICardPart
{
    /// <summary>False keeps the card from being played onto <paramref name="zone"/> (it can't be afforded yet). Asked before each play.</summary>
    bool CanPlay(Card card, PlayZone zone) => true;

    /// <summary>The card has been drawn into <paramref name="hand"/>.</summary>
    void Drawn(Card card, Hand hand)
    {
    }

    /// <summary>The card has been played onto <paramref name="zone"/>.</summary>
    void Played(Card card, PlayZone zone)
    {
    }

    /// <summary>The card has been put on <paramref name="pile"/>: discarded, or gathered back into its deck.</summary>
    void Discarded(Card card, Deck pile)
    {
    }
}

/// <summary>How a <see cref="PlayZone"/> lays out the cards played onto it.</summary>
public enum CardLayout
{
    /// <summary>Side by side, in the order they were played, closer together as it fills up.</summary>
    Row,

    /// <summary>One on top of another, the last played showing: a trick, a pile to play onto.</summary>
    Pile,
}
