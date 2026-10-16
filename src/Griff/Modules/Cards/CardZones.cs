using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.Cards;

/// <summary>
/// A place cards go: a <see cref="Deck"/>, a <see cref="Hand"/> or a <see cref="PlayZone"/>. Its cards are the entities
/// inside it with a <see cref="Card"/>, in order (the last is a deck's top card, a hand's rightmost), and it lays them out:
/// a card that comes in slides to its place.
/// </summary>
public abstract class CardZone : Component
{
    /// <summary>How many cards are in it. Kept up to date by the game; in a network game, players' machines are sent it.</summary>
    [DoNotSerialize]
    [Sent]
    [Output]
    public int Count { get; internal set; }

    /// <summary>The cards in it, in order: the entities inside it with a <see cref="Card"/>.</summary>
    [DoNotSerialize]
    public IReadOnlyList<Card> Cards => CardsIn(Entity);

    /// <summary>Where <paramref name="card"/> is among its <see cref="Cards"/>, or -1 if it isn't in it.</summary>
    public int IndexOf(Card card)
    {
        var index = 0;
        foreach (var child in Entity?.Children ?? [])
        {
            if (child.GetComponent<Card>() is not { } c)
                continue;
            if (c == card)
                return index;
            index++;
        }

        return -1;
    }

    // Seconds a card takes to slide to its place here.
    internal abstract float Slide { get; }

    // Where the card at `index` of `count` goes, in this entity's own space, and how far it's turned (degrees).
    internal abstract (Vector2 At, float Angle) Slot(int index, int count, Vector2 card);

    // Turns a card put here face up or down.
    internal abstract bool PutsFaceUp { get; }

    internal static List<Card> CardsIn(Entity? entity)
    {
        var cards = new List<Card>();
        foreach (var child in entity?.Children ?? [])
        {
            if (child.GetComponent<Card>() is { } card)
                cards.Add(card);
        }

        return cards;
    }
}

/// <summary>
/// A pile of cards to draw from, with no code: its cards are the entities inside it with a <see cref="Card"/> (the last one
/// is the top), or made from a <see cref="Table"/>, one for each row, as the game starts. It's shuffled as the game starts
/// (shuffles use the game's random numbers, so a seeded game deals the same), and a <see cref="Hand"/> draws its top card.
/// Face down it shows only its back; face up (<see cref="FaceUp"/>) it's a discard pile showing its top card. Click it to
/// draw a card into your hand. Run out, it shuffles its <see cref="RefillFrom"/> pile back in.
/// </summary>
/// <remarks>
/// Its Draw, Shuffle and Restart actions are there for a <see cref="Trigger"/>, a wire or a script, and its Count output for
/// a Counter or a Text. The order of its cards is saved with the scene, so a rewind goes back to it. In a network game the
/// host shuffles and deals, and the cards in a face-down deck aren't sent to anyone's machine: only how many there are.
/// </remarks>
[Category("Cards")]
[WorksWith(typeof(Card), Note = "its cards, inside it")]
[WorksWith(typeof(Hand), Note = "draws from it")]
[WorksWith(typeof(TableValues), Back = "a Deck makes its cards from a table's rows, one with Table Values each")]
[ComponentName("Griff.Deck")]
public sealed class Deck : CardZone, IDrawable, IDrawEffect, IPlaytestReport
{
    /// <summary>
    /// Shows its top card face up: a discard pile. Face down (as it is by default) shows only its back, and nobody can tell
    /// the order of its cards: in a network game they aren't even sent to players' machines.
    /// </summary>
    public bool FaceUp { get; set; }

    /// <summary>Shuffles it as the game starts.</summary>
    public bool ShuffleOnStart { get; set; } = true;

    /// <summary>
    /// Clicking it draws its top card into the clicking player's hand (one that draws from it), as does a hand's Draw
    /// Action on the keys or a gamepad.
    /// </summary>
    public bool ClickToDraw { get; set; } = true;

    /// <summary>Cards can be dragged from a hand onto it, and land on top: a discard pile.</summary>
    public bool TakesDrops { get; set; }

    /// <summary>
    /// Another deck (a discard pile) whose cards are shuffled back into this one when a card is drawn from it once it has
    /// run out. Empty: it stays empty.
    /// </summary>
    [EntityName]
    public string? RefillFrom { get; set; }

    /// <summary>
    /// A table to make its cards from as the game starts, e.g. "Cards": a card for each row (more with
    /// <see cref="CountColumn"/>), named like the row, with a Table Values setting it from the row, so a column called Title,
    /// Text, Value, Kind, Color or Image sets the card's, and any other column a property of the same name on the
    /// <see cref="Prefab"/>'s components.
    /// </summary>
    [Group("From a Table")]
    [Names(NameKind.Table)]
    public string? Table { get; set; }

    /// <summary>
    /// The prefab each card made from the table is a copy of, e.g. "Card": its look and parts. Empty makes plain cards. A row
    /// can name its own in the <see cref="PrefabColumn"/>.
    /// </summary>
    [Names(NameKind.Prefab)]
    public string? Prefab { get; set; }

    /// <summary>
    /// A column of the table naming the prefab each row's cards are a copy of, e.g. "Creature" for a card with a Health and
    /// "Spell" for one without, so one table holds cards of several makes. A row with it empty (or a table without it) uses
    /// <see cref="Prefab"/>.
    /// </summary>
    [Names(NameKind.Column, Table = nameof(Table))]
    public string? PrefabColumn { get; set; } = "Prefab";

    /// <summary>A whole-number column of the table saying how many copies of each row's card go in it. A row without one gets one.</summary>
    [Names(NameKind.Column, Table = nameof(Table))]
    public string? CountColumn { get; set; } = "Count";

    /// <summary>How big its pile is drawn: its cards' size.</summary>
    [Group("Look")]
    public Vector2 Size { get; set; } = new(1.6f, 2.24f);

    /// <summary>The picture on the back of its cards. Empty draws a plain back of <see cref="BackColor"/>.</summary>
    [AssetFile(AssetFileKind.Image)]
    public string? Back { get; set; }

    /// <summary>The color of its cards' backs.</summary>
    public Color BackColor { get; set; } = CardLook.DefaultBack;

    /// <summary>How far up each card of the pile sits from the one under it, so a thick deck looks thick.</summary>
    public float Spread { get; set; } = 0.012f;

    /// <summary>Seconds a card takes to slide onto it.</summary>
    public float SlideTime { get; set; } = 0.3f;

    /// <summary>A sound played as a card is drawn from it, e.g. "Sounds/draw.wav".</summary>
    [Group("Sound and Messages")]
    public string? DrawSound { get; set; }

    /// <summary>A sound played as it's shuffled.</summary>
    public string? ShuffleSound { get; set; }

    /// <summary>A message sent as it's shuffled, with this entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? ShuffleMessage { get; set; }

    /// <summary>
    /// A message sent when a card is to be drawn from it while it's empty and nothing refills it, with this entity as its
    /// data: the end of the game in some card games.
    /// </summary>
    [Names(NameKind.Message)]
    public string? EmptyMessage { get; set; }

    /// <summary>Set by the game once it has made its cards and shuffled, so a rewind doesn't do it again. Leave it be.</summary>
    [Group("State")]
    public bool Started { get; set; }

    /// <summary>How many times it has been shuffled. Saved with the scene, so a rewind takes them back.</summary>
    [Output]
    public int Shuffles { get; set; }

    /// <summary>Its top card, or null if it's empty.</summary>
    [DoNotSerialize]
    public Card? Top => Cards is { Count: > 0 } cards ? cards[^1] : null;

    /// <summary>Raised as it's shuffled.</summary>
    public event Action<Deck>? Shuffled;

    /// <summary>
    /// Draws <paramref name="count"/> cards from it at the start of the next frame, into the hand that draws from it (the one
    /// whose side's turn it is, if several do), a tenth of a second apart.
    /// </summary>
    [Action]
    public void Draw(int count = 1) => DrawRequests += Math.Max(0, count);

    /// <summary>Shuffles it at the start of the next frame.</summary>
    [Action]
    public void Shuffle() => ShuffleRequested = true;

    /// <summary>
    /// Gathers every card that belongs to it (see Card.Home) back in at the start of the next frame, face down, shuffles it,
    /// and has the hands that draw from it deal their Start With afresh: a new game.
    /// </summary>
    [Action]
    public void Restart() => RestartRequested = true;

    public override bool Uses(string property) => property != nameof(Started);

    internal int DrawRequests { get; set; }

    internal bool ShuffleRequested { get; set; }

    internal bool RestartRequested { get; set; }

    internal void RaiseShuffled() => Shuffled?.Invoke(this);

    internal override float Slide => SlideTime;

    internal override bool PutsFaceUp => FaceUp;

    // Stacked, each a little higher than the one under it, up to a thick pile.
    internal override (Vector2 At, float Angle) Slot(int index, int count, Vector2 card) => (PileOffset(index), 0);

    internal Vector2 PileOffset(int index) => new Vector2(-0.35f, 1) * (Spread * Math.Min(index, CardLook.PileLayers));

    // ----- Kept by the game while it runs, on each machine -----

    // How many of its cards its pile draws here (those resting in it), its last shuffles seen and how long it has been
    // jiggling since.
    internal int PileCount { get; set; }

    internal int ShufflesSeen { get; set; } = -1;

    internal float Jiggle { get; set; }

    // Lit on this machine: 1 when a held card could be dropped on it, 2 when it would be.
    internal int Lit { get; set; }

    internal Color LitColor { get; set; }

    int? IDrawable.DrawOrder => 0;

    int IDrawable.DrawStep => DrawSteps.Sprite;

    // A shuffle shakes it for a moment.
    Matrix4x4? IDrawEffect.DrawTransform
    {
        get
        {
            if (Jiggle <= 0)
                return null;
            var center = Entity!.Transform.WorldPosition;
            var angle = MathF.Sin(Jiggle * 45) * 0.08f * (Jiggle / CardLook.JiggleTime);
            return Matrix4x4.CreateRotationZ(angle, new Vector3(center.X, center.Y, 0));
        }
    }

    void IDrawable.Draw(Drawing drawing)
    {
        var entity = Entity!;
        var pile = drawing.IsPlaying ? PileCount : CardsIn(entity).Count;
        var world = entity.Transform.WorldMatrix;
        if (pile == 0)
        {
            CardLook.EmptySlot(drawing, this, world, Size, !drawing.IsPlaying && !string.IsNullOrWhiteSpace(Table) ? Table.Trim() : null);
        }
        else
        {
            // Face down, a stack of backs; face up, the edges of the cards under the top one, which draws itself.
            var layers = Math.Min(pile, CardLook.PileLayers + 1);
            for (var i = 0; i < layers; i++)
            {
                var at = Matrix4x4.CreateTranslation(PileOffset(i).X, PileOffset(i).Y, 0) * world;
                if (!FaceUp)
                    CardLook.Back(drawing, this, at, Size, Back, BackColor);
                else if (i < layers - 1 || !drawing.IsPlaying)
                    CardLook.Edge(drawing, this, at, Size);
            }
        }

        if (Lit > 0)
            CardLook.Outline(drawing, this, world, Size + new Vector2(0.16f), Lit == 2 ? 0.08f : 0.04f, drawing.Tint(this, LitColor));
    }

    // ", 30 cards, top Red 3" or ", empty".
    string IPlaytestReport.Report()
    {
        if (Count == 0)
            return ", empty";
        var text = $", {Count} card{(Count == 1 ? "" : "s")}";
        return FaceUp && Top is { } top ? text + $", {top.Entity!.Name} on top" : text + ", face down";
    }
}

/// <summary>
/// The cards a player holds, fanned out along the bottom of the screen, with no code. It draws <see cref="StartWith"/> from
/// its <see cref="Deck"/> as the game starts. Drag a card onto a <see cref="PlayZone"/> that takes it to play it, or click
/// it and then the zone (a tap and a tap on a touch screen); dropped anywhere else it slides back. Hovering a card lifts it
/// out of the fan. With the keys or a gamepad, left and right pick a card, the <see cref="PickAction"/> (Space, A) takes it
/// up, left and right choose where it goes and the Pick Action again plays it; the <see cref="CancelAction"/> puts it back.
/// </summary>
/// <remarks>
/// Give it a <see cref="TurnTaker"/> naming its side and it's played only on its side's turn, each card played using one of
/// the turn taker's actions (with Actions set), and in a network game only by the players on that team (the Multiplayer's
/// Teams). With <see cref="Hidden"/>, only its own side sees its faces. Its Draw and Discard actions are there for a
/// <see cref="Trigger"/> (draw a card as each turn starts, discard the hand as it ends), a wire or a script, and its Count
/// output for a Counter or a Text. Untick <see cref="Controllable"/> (or Focus something else) and it ignores the mouse and
/// keys.
/// </remarks>
[Category("Cards")]
[WorksWith(typeof(Deck), Note = "the deck it draws from")]
[WorksWith(typeof(PlayZone), Note = "where its cards are played")]
[WorksWith(typeof(TurnTaker), Note = "its side: played on its side's turn", Back = "a Hand played on its side's turn")]
[ComponentName("Griff.Hand")]
public sealed class Hand : CardZone, IDrawable, IDrawEffect, ITurnReady, IPlaytestReport
{
    private int _maxMana;
    private string? _manaText;
    private (int, int) _manaFor;

    /// <summary>The deck it draws from, by name. Empty draws from the scene's first face-down deck.</summary>
    [EntityName]
    public string? Deck { get; set; }

    /// <summary>The deck its Discard action puts its cards on, by name. Empty uses the scene's first face-up deck.</summary>
    [EntityName]
    public string? DiscardPile { get; set; }

    /// <summary>How many cards it draws as the game starts, dealt one after another.</summary>
    public int StartWith { get; set; } = 5;

    /// <summary>The most cards it holds: once it has this many it draws no more. 0 is no limit.</summary>
    public int Limit { get; set; } = 10;

    /// <summary>
    /// Shows its cards' faces only to its own side (its Turn Taker's): in a network game, players on other teams see
    /// their backs, and their machines aren't even sent which cards they are; at one screen, a side the computer plays
    /// keeps its cards face down, and with several people taking turns, each hand is face down while it isn't its side's
    /// turn.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// Untick it to have it ignore the mouse, keys and gamepad (its actions and scripts still work): another part of the
    /// scene has the player's input. Its Focus action ticks it and unticks it on everything else the player plays.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>
    /// The entity whose <see cref="Health"/> is this side's life, by name, e.g. "Blue Hero": what its cards' effects do
    /// to "Self", and the other sides' to "Opponent". Empty: the hand itself if it has a Health, else the first entity
    /// with a Health and a Turn Taker on its side.
    /// </summary>
    [EntityName]
    public string? Hero { get; set; }

    /// <summary>The mana it fills up to as each of its side's turns starts. Changing it while full keeps it full. 0 doesn't refill.</summary>
    [Group("Mana")]
    [Output]
    public int MaxMana
    {
        get => _maxMana;
        set
        {
            if (Mana == _maxMana)
                Mana = value;
            _maxMana = value;
        }
    }

    // After Max Mana, so a scene loaded (a rewind) sets what's left after the most it fills to.

    /// <summary>Mana it has to pay its cards' costs with: it fills up to <see cref="MaxMana"/> as each of its side's turns starts.</summary>
    [Output(OutOf = nameof(MaxMana))]
    public int Mana { get; set; }

    /// <summary>How much <see cref="MaxMana"/> grows as each of its side's turns starts, up to <see cref="ManaLimit"/>: 1 adds a crystal a turn.</summary>
    public int ManaGrowth { get; set; }

    /// <summary>The most <see cref="MaxMana"/> grows to.</summary>
    [ShownWhen(nameof(ManaGrowth))]
    public int ManaLimit { get; set; } = 10;

    /// <summary>
    /// For the computer (a hand on one of its turn order's Computer Sides): seconds it thinks before each play, the last
    /// part of it showing the card it's about to play lifted out of its hand, so the player can follow. 0 plays at once.
    /// </summary>
    [Group("Computer")]
    public float ThinkTime { get; set; } = 0.8f;

    /// <summary>For the computer: the share of its hero's health below which it plays its healing, e.g. 0.5 for under half. It keeps healing cards until then.</summary>
    public float HealBelow { get; set; } = 0.5f;

    /// <summary>For the computer: how many cards it keeps in its hand for later rather than play.</summary>
    public int Keep { get; set; }

    /// <summary>How far apart its cards are, middle to middle, while there's room.</summary>
    [Group("Look")]
    public float Spacing { get; set; } = 1.15f;

    /// <summary>How wide it fans out at most: more cards than fit squeeze closer together.</summary>
    public float MaxWidth { get; set; } = 9;

    /// <summary>Degrees each card is turned from the one beside it, fanning them out. 0 lines them up straight.</summary>
    public float Fan { get; set; } = 4;

    /// <summary>How far the cards at the ends sit below the middle one, so the fan curves.</summary>
    public float Arc { get; set; } = 0.2f;

    /// <summary>How far a card is lifted out of the fan while it's under the mouse or picked.</summary>
    public float HoverRise { get; set; } = 0.6f;

    /// <summary>How much bigger a card is drawn while it's lifted: 1.15 is 15% bigger.</summary>
    public float HoverScale { get; set; } = 1.15f;

    /// <summary>The outline round the card picked, and the one the keys are on.</summary>
    public Color HighlightColor { get; set; } = Color.FromBytes(0xFF, 0xD8, 0x4A);

    /// <summary>Seconds a card takes to slide into the hand.</summary>
    public float SlideTime { get; set; } = 0.25f;

    /// <summary>The input action that takes up the card the keys are on, and plays it where they point: "Jump" is Space or the gamepad's A.</summary>
    [Group("Controls")]
    [Names(NameKind.Action)]
    public string? PickAction { get; set; } = "Jump";

    /// <summary>The input action that puts a card taken up back, as a right click does: "Dash" is Shift or the gamepad's B.</summary>
    [Names(NameKind.Action)]
    public string? CancelAction { get; set; } = "Dash";

    /// <summary>The input action that draws a card from its deck (one with Click To Draw): "Interact" is E, Enter or the gamepad's Y.</summary>
    [Names(NameKind.Action)]
    public string? DrawAction { get; set; } = "Interact";

    /// <summary>A sound played as a card is taken up, e.g. "Sounds/pick.wav".</summary>
    [Group("Sound and Messages")]
    public string? PickSound { get; set; }

    /// <summary>A message sent as a card is drawn into it, with the card's entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? DrawMessage { get; set; }

    /// <summary>Set by the game once it has drawn its Start With, so a rewind doesn't deal again. Leave it be.</summary>
    [Group("State")]
    public bool Dealt { get; set; }

    /// <summary>Cards still to draw, one a tenth of a second (Draw asks for them). Saved with the scene, so a rewind keeps them.</summary>
    public int ToDraw { get; set; }

    /// <summary>
    /// What was last played from it, e.g. "Red plays Fireball on Blue Hero: 3 damage.", or what one of its cards on the table did
    /// as its side's turn began ("Wolf: 2 damage to Blue Hero."). The game sets it. Read as the "Said" output.
    /// </summary>
    [Output]
    [PlainText("what was last played: the game sets it")]
    public string? Said { get; set; }

    /// <summary>Its side: its Turn Taker's (on it or an entity it's inside), or empty with none.</summary>
    [DoNotSerialize]
    public string Side => Entity is { } entity && TurnTaker.Of(entity) is { } taker ? taker.Side?.Trim() ?? "" : "";

    /// <summary>The card this machine's player has taken up from it (clicked, or with the keys), or null.</summary>
    [DoNotSerialize]
    public Card? Picked { get; internal set; }

    /// <summary>Raised as a card is drawn into it.</summary>
    public event Action<Hand, Card>? Drew;

    /// <summary>Draws <paramref name="count"/> cards from its deck, starting at the next frame, a tenth of a second apart.</summary>
    [Action]
    public void Draw(int count = 1) => ToDraw += Math.Max(0, count);

    /// <summary>Puts every card in it on its discard pile at the start of the next frame.</summary>
    [Action]
    public void Discard() => Requests.Add(new CardRequest(null, null, null));

    /// <summary>Puts <paramref name="card"/> (one of its cards) on its discard pile at the start of the next frame.</summary>
    public void Discard(Card card) => Requests.Add(new CardRequest(card, null, null));

    /// <summary>
    /// Plays <paramref name="card"/> (one of its cards) onto <paramref name="zone"/> at the start of the next frame, if the zone
    /// takes it, as a player dragging it there does: a play zone, or a deck that takes drops. A card with an effect whose
    /// target is chosen goes for <paramref name="target"/> (a card on the table or something with a Health), or without
    /// one, for what the computer would pick.
    /// </summary>
    public void Play(Card card, CardZone zone, Entity? target = null) => Requests.Add(new CardRequest(card, zone, target));

    /// <summary>Gives it <paramref name="amount"/> more mana to pay its cards' costs with (less than 0 takes some away).</summary>
    [Action]
    public void GainMana(int amount = 1) => Mana = Math.Max(0, Mana + amount);

    public override bool Uses(string property) => property != nameof(Dealt);

    internal List<CardRequest> Requests { get; } = [];

    // Seconds until the next card of ToDraw is drawn.
    internal float NextDraw { get; set; }

    // Its side's turn has begun since the game last looked: its cards on the table with Each Turn effects go off.
    internal bool TurnBegan { get; set; }

    // The computer's play: seconds until its next move this turn (-1 starts afresh), the card it's showing and where it's
    // going, and whether it has ended its turn. Kept where the game is run, not saved: a rewound game thinks afresh.
    internal float ThinkLeft { get; set; } = -1;

    internal Card? Readied { get; set; }

    internal CardZone? ReadiedZone { get; set; }

    internal Entity? ReadiedTarget { get; set; }

    internal bool TurnDone { get; set; }

    // Mana back up, and more of it, as its side's turn begins; the computer thinks afresh.
    void ITurnReady.TurnBegins()
    {
        if (ManaGrowth != 0)
            _maxMana = Math.Clamp(_maxMana + ManaGrowth, 0, Math.Max(_maxMana, ManaLimit));
        if (_maxMana > 0)
            Mana = _maxMana;
        TurnBegan = true;
        (ThinkLeft, TurnDone) = (-1, false);
    }

    // On this machine: a line from the card being aimed to where it's aimed, and the boxes round what it can be aimed at
    // (the one it's on brighter). Drawn over everything.
    internal (Vector2 From, Vector2 To)? Aim { get; set; }

    internal List<(Vector2 Center, Vector2 Size, bool On)> AimMarks { get; } = [];

    bool IDrawEffect.DrawsOnTop => Aim is not null;

    internal void RaiseDrew(Card card) => Drew?.Invoke(this, card);

    internal override float Slide => SlideTime;

    internal override bool PutsFaceUp => true;

    // Fanned out about its middle: closer together as it fills, turned away from the middle and dipping at the ends.
    internal override (Vector2 At, float Angle) Slot(int index, int count, Vector2 card)
    {
        if (count <= 1)
            return (Vector2.Zero, 0);
        var spacing = Math.Min(Spacing, Math.Max(0, MaxWidth - card.X) / (count - 1));
        var middle = (count - 1) / 2f;
        var from = index - middle;
        var t = from / middle;
        return (new Vector2(from * spacing, -Arc * t * t), -from * Fan);
    }

    // ----- Kept by the game while it runs, on each machine -----

    // The cards it holds that this machine has (in a network game, other sides' hidden cards aren't sent), and their backs.
    internal int Shown { get; set; }

    internal string? BackImage { get; set; }

    internal Color BackColor { get; set; } = CardLook.DefaultBack;

    int? IDrawable.DrawOrder => 5;

    int IDrawable.DrawStep => DrawSteps.Sprite;

    // Backs for the cards it holds that this machine wasn't sent (another side's hidden hand), and in the editor where it
    // fans out.
    void IDrawable.Draw(Drawing drawing)
    {
        var entity = Entity!;
        var world = entity.Transform.WorldMatrix;
        var card = new Vector2(1.6f, 2.24f);
        if (!drawing.IsPlaying)
        {
            CardLook.HandArea(drawing, this, world, new Vector2(Math.Max(MaxWidth, card.X), card.Y));
            return;
        }

        if (MaxMana > 0 || Mana > 0)
        {
            var width = Math.Min(MaxWidth, Spacing * Math.Max(0, Count - 1) + card.X);
            if (_manaText is null || _manaFor != (Mana, MaxMana))
            {
                _manaFor = (Mana, MaxMana);
                _manaText = MaxMana > 0 ? $"{Mana}/{MaxMana}" : $"{Mana}";
            }

            CardLook.Mana(drawing, this, world, new Vector2(-Math.Max(width, card.X) / 2 - 0.8f, -0.5f), Mana > 0, _manaText);
        }

        if (Aim is { } aim)
            CardLook.Aim(drawing, this, aim.From, aim.To, AimMarks, HighlightColor);

        for (var i = Shown; i < Count; i++)
        {
            var (at, angle) = Slot(i, Count, card);
            var place = Matrix4x4.CreateRotationZ(angle * MathF.PI / 180) * Matrix4x4.CreateTranslation(at.X, at.Y, 0) * world;
            CardLook.Back(drawing, this, place, card, BackImage, BackColor);
        }
    }

    // ", holds 5: Red 1, Blue 4, ...", and whether it can be played now.
    string IPlaytestReport.Report()
    {
        var cards = Cards;
        var text = cards.Count == 0 ? ", holds nothing" : $", holds {cards.Count}: {string.Join(", ", cards.Select(c => c.Entity!.Name))}";
        if (MaxMana > 0 || Mana > 0)
            text += $", mana {Mana}/{MaxMana}";
        if (Computer)
            return text + ", played by the computer" + (Readied is { Entity: { } next } ? $", about to play {next.Name}" : "");
        return text + (CanPlay ? "" : ", waiting for its turn");
    }

    string IPlaytestReport.MouseReport() =>
        Picked is { Entity: { } picked } ? (Picked.Dragged ? $", dragging {picked.Name}" : $", picked {picked.Name}") : Aiming is { Entity: { } aimed } ? $", aiming {aimed.Name}" : "";

    // Whether this machine's player can play it now (it's theirs, it's its side's turn), whether the computer plays it, and
    // the card this machine's player is aiming from it. Set by the game.
    internal bool CanPlay { get; set; } = true;

    internal bool Computer { get; set; }

    internal Card? Aiming { get; set; }
}

/// <summary>
/// A place on the table cards are played onto, with no code: drag a card from a <see cref="Hand"/> onto it (or click the
/// card, then it) and it slides into place, laid out in a row or a pile. It takes only the kinds it <see cref="Accepts"/>,
/// up to its <see cref="Limit"/>, from the hands named in <see cref="From"/>; a card it won't take slides back to the hand.
/// It lights up while a card it would take is held over it. Each card played sends its <see cref="PlayMessage"/>.
/// </summary>
/// <remarks>
/// Its Count and Played outputs are there for a Counter or a Text, and its Discard action (put every card on it on its
/// discard pile: clearing the table at the end of a round) for a <see cref="Trigger"/>, a wire or a script. What's on it
/// and how many have been played are saved with the scene, so a rewind takes them back.
/// </remarks>
[Category("Cards")]
[WorksWith(typeof(Hand), Note = "plays cards onto it")]
[WorksWith(typeof(Card), Note = "the cards played on it, inside it")]
[ComponentName("Griff.PlayZone")]
public sealed class PlayZone : CardZone, IDrawable, IPlaytestReport
{
    /// <summary>The kinds of card it takes (each a card's Kind), separated by commas, e.g. "Creature, Spell". Empty takes any.</summary>
    [Names(NameKind.Kind, Scope = "Card", Many = true)]
    public string? Accepts { get; set; }

    /// <summary>The most cards it holds: once it has this many it takes no more. 0 is no limit.</summary>
    public int Limit { get; set; }

    /// <summary>The hands that may play onto it, by name, separated by commas: each side's own row. Empty: any hand.</summary>
    [EntityName(Many = true, Incoming = true)]
    public string? From { get; set; }

    /// <summary>Turns the cards played onto it face up. Untick it to play cards face down: a trap, a hidden bet.</summary>
    public bool FaceUp { get; set; } = true;

    /// <summary>How it lays out the cards played onto it: in a <see cref="CardLayout.Row"/> or a <see cref="CardLayout.Pile"/>.</summary>
    public CardLayout Layout { get; set; }

    /// <summary>The deck its Discard action puts its cards on, by name. Empty uses the scene's first face-up deck.</summary>
    [EntityName]
    public string? DiscardPile { get; set; }

    /// <summary>
    /// Seconds a card played onto it stays once it has slid into place, before it goes on to its <see cref="DiscardPile"/>:
    /// a spell, cast and gone. Cards that stay on the table to do something (a creature, with a Health, or a card with a
    /// Card Effect that acts each turn) stay. 0 keeps every card until its Discard action.
    /// </summary>
    public float DiscardAfter { get; set; }

    /// <summary>How big it is, in world units: cards dropped inside it are played onto it.</summary>
    [Group("Look")]
    public Vector2 Size { get; set; } = new(9, 2.8f);

    /// <summary>How far apart cards in a row are, middle to middle, while there's room.</summary>
    public float Spacing { get; set; } = 1.8f;

    /// <summary>The color it's drawn in on the table. Transparent draws nothing (it can lie over a picture of the table).</summary>
    public Color Color { get; set; } = Color.FromBytes(0xFF, 0xFF, 0xFF, 0x18);

    /// <summary>The color it lights up in while a card it would take is held over it, or the keys point at it.</summary>
    public Color HoverColor { get; set; } = Color.FromBytes(0xFF, 0xE0, 0x82, 0x60);

    /// <summary>Words written in it while it's empty, e.g. "Play a card".</summary>
    [PlainText("what's written in it, its own words")]
    public string? Label { get; set; }

    /// <summary>Seconds a card takes to slide into place on it.</summary>
    public float SlideTime { get; set; } = 0.25f;

    /// <summary>A sound played as a card is played onto it, e.g. "Sounds/play.wav".</summary>
    [Group("Sound and Messages")]
    public string? PlaySound { get; set; }

    /// <summary>A sound played as it turns a card away.</summary>
    public string? RefuseSound { get; set; }

    /// <summary>A message sent as a card is played onto it, with the card's entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? PlayMessage { get; set; }

    /// <summary>How many cards have been played onto it, all told. Saved with the scene, so a rewind takes them back.</summary>
    [Group("State")]
    [Output]
    public int Played { get; set; }

    /// <summary>True while it holds its <see cref="Limit"/>.</summary>
    [DoNotSerialize]
    public bool IsFull => Limit > 0 && Count >= Limit;

    /// <summary>Raised as a card is played onto it.</summary>
    public event Action<PlayZone, Card>? CardPlayed;

    /// <summary>Puts every card on it on its discard pile at the start of the next frame.</summary>
    [Action]
    public void Discard() => DiscardRequested = true;

    /// <summary>True if it takes a card of <paramref name="kind"/> (a card's Kind): it accepts any, or names that one.</summary>
    public bool AcceptsKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(Accepts))
            return true;
        foreach (var name in Accepts.Split(','))
        {
            if (string.Equals(name.Trim(), kind?.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>True if <paramref name="hand"/> may play onto it: <see cref="From"/> is empty or names it.</summary>
    public bool TakesFrom(Hand hand)
    {
        if (string.IsNullOrWhiteSpace(From))
            return true;
        var name = hand.Entity?.Name;
        foreach (var part in From.Split(','))
        {
            if (string.Equals(part.Trim(), name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal bool DiscardRequested { get; set; }

    internal void RaisePlayed(Card card) => CardPlayed?.Invoke(this, card);

    internal override float Slide => SlideTime;

    internal override bool PutsFaceUp => FaceUp;

    // A row centered on it, squeezed to fit its width; or a pile, a little higher each card.
    internal override (Vector2 At, float Angle) Slot(int index, int count, Vector2 card)
    {
        if (Layout == CardLayout.Pile)
            return (new Vector2(0.02f, 0.03f) * Math.Min(index, CardLook.PileLayers), 0);
        if (count <= 1)
            return (Vector2.Zero, 0);
        var spacing = Math.Min(Spacing, Math.Max(0, Size.X - card.X) / (count - 1));
        return (new Vector2((index - (count - 1) / 2f) * spacing, 0), 0);
    }

    // Lit on this machine: 1 when a held card could go here, 2 when it would.
    internal int Lit { get; set; }

    int? IDrawable.DrawOrder => 0;

    int IDrawable.DrawStep => DrawSteps.Sprite;

    void IDrawable.Draw(Drawing drawing)
    {
        var entity = Entity!;
        var world = entity.Transform.WorldMatrix;
        var color = Lit == 2 ? HoverColor : Lit == 1 ? Color.Lerp(Color, HoverColor, 0.4f) : Color;
        CardLook.Zone(drawing, this, world, Size, drawing.Tint(this, color), Count == 0 || !drawing.IsPlaying ? Label : null);
    }

    // ", holds Red 1, Blue 2 (full)" or ", empty", and what it takes.
    string IPlaytestReport.Report()
    {
        var cards = Cards;
        var text = cards.Count == 0 ? ", empty" : $", holds {string.Join(", ", cards.Select(c => c.Entity!.Name))}{(IsFull ? " (full)" : "")}";
        return text + (string.IsNullOrWhiteSpace(Accepts) ? "" : $", takes {Accepts.Trim()}");
    }
}

// What a script asked of a hand: play a card onto a zone, or discard it (no zone), or discard them all (no card).
internal sealed record CardRequest(Card? Card, CardZone? Zone, Entity? Target);
