using System.Numerics;
using Griff.Audio;
using Griff.Data;
using Griff.Input;
using Griff.Projects;
using Griff.Scenes;
using static Griff.Projects.ProjectTemplates;

namespace Griff.Modules.Cards;

// The Cards template: a card duel against the computer with no code. A menu (Play, Quit), and a Duel scene on a card table:
// the player's mage and the computer's lich, each with 20 health, draw from one deck made from the Cards table (creatures
// and spells, effects and costs as columns) and play onto their own side of the table with mana that grows each turn.
// Creatures stay and act as each of their team's turns starts; spells do what they do and go to the discard pile.
// Victory or Defeat when a hero falls.
internal sealed class CardsTemplate : ProjectTemplate
{
    public override string Description => "A card duel against the computer: draw from a deck made from a table, spend mana that grows each turn on creatures that strike every turn and spells that burn, heal and draw, and bring the lich's health to nothing. Drag a card onto your side of the table (or tap it, then the table; or arrows and Space), aim spells with the dotted line, Tab ends your turn.";

    public override int Order => 127;

    protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Chill, seed: 27) with { Volume = 0.45f };

    protected override Scene BuildStartScene() => Menu();

    protected override Dictionary<string, Scene> BuildMoreScenes() => new() { [$"Scenes/{DuelScene}.scene"] = Duel() };

    protected override Dictionary<string, Entity> BuildPrefabs() => new()
    {
        [$"Prefabs/{CreaturePrefab}.prefab"] = CreatureCard(),
        [$"Prefabs/{SpellPrefab}.prefab"] = SpellCard(),
        ["Prefabs/Hit.prefab"] = Burst("Hit", Color.FromBytes(0xFF, 0x8A, 0x4A), 14, 4, 0.18f),
        ["Prefabs/Blast.prefab"] = Burst("Blast", Color.FromBytes(0xFF, 0x60, 0x20), 32, 6.5f, 0.3f),
        ["Prefabs/Mend.prefab"] = Rising("Mend", Color.FromBytes(0x7A, 0xF0, 0x8A)),
        ["Prefabs/Shine.prefab"] = Rising("Shine", Color.FromBytes(0xFF, 0xE0, 0x6A)),
    };

    public override IReadOnlyDictionary<string, byte[]> Images() => CardsArt.Files();

    public override IReadOnlyDictionary<string, Table> Tables() => CardsTables.All();

    // Cards whoosh as they're drawn, click as they're picked and played, and the turn chimes; the player's hero cries out.
    protected internal override void WireSounds(Entity entity)
    {
        foreach (var component in entity.Components)
        {
            switch (component)
            {
                case Deck deck:
                    TemplateSounds.Set(deck.DrawSound, s => deck.DrawSound = s, TemplateSounds.Dash);
                    TemplateSounds.Set(deck.ShuffleSound, s => deck.ShuffleSound = s, TemplateSounds.Dig);
                    break;
                case Hand hand:
                    TemplateSounds.Set(hand.PickSound, s => hand.PickSound = s, TemplateSounds.Select);
                    break;
                case PlayZone zone:
                    TemplateSounds.Set(zone.PlaySound, s => zone.PlaySound = s, TemplateSounds.Place);
                    TemplateSounds.Set(zone.RefuseSound, s => zone.RefuseSound = s, TemplateSounds.Switch);
                    break;
                case TurnOrder order:
                    TemplateSounds.Set(order.Sound, s => order.Sound = s, TemplateSounds.Jump);
                    break;
                case Health health when entity.Name == PlayerHero:
                    TemplateSounds.Set(health.HurtSound, s => health.HurtSound = s, TemplateSounds.Hurt);
                    break;
                case Panel panel when entity.Name == "Victory":
                    TemplateSounds.Set(panel.Sound, s => panel.Sound = s, TemplateSounds.PowerUp);
                    break;
            }
        }

        base.WireSounds(entity);
    }

    /// <summary>The scene the duel is played in.</summary>
    public const string DuelScene = "Duel";

    public const string PlayerHero = "Mage";
    public const string RivalHero = "Lich";
    public const string PlayerHand = "Player Hand";
    public const string RivalHand = "Rival Hand";
    public const string PlayerRow = "Player Row";
    public const string RivalRow = "Rival Row";
    public const string DeckName = "Deck";
    public const string Pile = "Discard Pile";
    public const string CreaturePrefab = "Creature";
    public const string SpellPrefab = "Spell";

    /// <summary>Each hero's health as the duel starts.</summary>
    public const int Life = 20;

    private const string Player = "Player";
    private const string Rival = "Rival";

    private static readonly Color Gold = Color.FromBytes(0xF2, 0xC9, 0x4C);
    private static readonly Color Light = Color.FromBytes(0xEC, 0xE8, 0xDC);
    private static readonly Color Dim = Color.FromBytes(0xB4, 0xC4, 0xB8);
    private static readonly Color Night = Color.FromBytes(0x10, 0x16, 0x14);
    private static readonly Color Blue = Color.FromBytes(0x4C, 0x8D, 0xF0);
    private static readonly Color Red = Color.FromBytes(0xD0, 0x48, 0x50);
    private static readonly Color Gray = Color.FromBytes(0x3A, 0x3F, 0x4B);
    private static readonly Color DarkGold = Color.FromBytes(0xA8, 0x86, 0x2E);

    // ----- The menu -----

    private static Scene Menu()
    {
        var scene = new Scene();
        scene.Add(Camera());
        scene.Add(Felt());

        scene.Add(Label("Title", new Vector2(0, 4.5f), "CARD DUEL", 1.5f, Gold));
        scene.Add(Label("Subtitle", new Vector2(0, 3.45f), "Creatures, spells and mana against the lich. Every card is a row of the Cards table.", 0.42f, Dim));

        // Three cards from the set, fanned, made as the deck makes them.
        scene.Add(Showcase("Fireball", new Vector2(-2.3f, 0.75f), 9, creature: false));
        scene.Add(Showcase("Wyvern", new Vector2(0, 1.0f), 0, creature: true));
        scene.Add(Showcase("Healing Potion", new Vector2(2.3f, 0.75f), -9, creature: false));

        scene.Add(MenuButton("Play", new Vector2(0, -2.35f), ButtonAction.LoadScene, DuelScene, Key.Enter, DarkGold));
        scene.Add(MenuButton("Quit", new Vector2(0, -3.65f), ButtonAction.Quit, null, Key.Escape, Gray));

        var hint = Label("Hint", Vector2.Zero, "Enter or click to play  ·  Esc to quit", 0.38f, Dim);
        var anchor = hint.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 0.5f);
        scene.Add(hint);
        return scene;
    }

    // A card of the set on the menu, lying on the table: its row's look and effect, set here as the deck sets them.
    private static Entity Showcase(string name, Vector2 at, float angle, bool creature)
    {
        var entity = creature ? CreatureCard() : SpellCard();
        entity.Name = name;
        entity.Transform.Position = new Vector3(at, 0);
        entity.Transform.Rotation = new Vector3(0, 0, angle);
        entity.Transform.Scale = new Vector3(1.3f, 1.3f, 1);
        var values = entity.GetComponent<TableValues>()!;
        values.Row = name;
        // Set from the row now too, so the editor shows it as the game will.
        values.Apply(new TableSet(CardsTables.All().Values), session: "template");
        return entity;
    }

    // ----- The duel -----

    private static Scene Duel()
    {
        var scene = new Scene();
        scene.Add(Camera());
        scene.Add(Felt());

        // The player goes first; the computer plays the rival, ending its turn once it has nothing it wants to play.
        var turns = new Entity("Turns");
        turns.Transform.Position = new Vector3(9.0f, 0.3f, 0);
        var order = turns.AddComponent<TurnOrder>();
        (order.Teams, order.ComputerTeams, order.ComputerTurnTime, order.Between, order.Format) =
            ($"{Player}, {Rival}", Rival, 0, 0.6f, "Round {1}\n{0}'s turn");
        var turnText = turns.AddComponent<Text>();
        (turnText.Value, turnText.Size, turnText.Color, turnText.Order) = ("Round 1\nPlayer's turn", 0.36f, Light, 150);
        scene.Add(turns);

        // The heroes: their health is each side's life.
        scene.Add(Hero(PlayerHero, Player, CardsArt.Mage, new Vector2(-8.6f, -1.3f), "Defeat", Blue));
        scene.Add(Hero(RivalHero, Rival, CardsArt.Lich, new Vector2(-8.6f, 2.3f), "Victory", Red));

        // One deck for both, made from the Cards table and shuffled, and a discard pile it's shuffled back from.
        var deckEntity = new Entity(DeckName);
        deckEntity.Transform.Position = new Vector3(9.0f, -1.6f, 0);
        var deck = deckEntity.AddComponent<Deck>();
        (deck.Table, deck.Prefab, deck.RefillFrom, deck.ClickToDraw, deck.Back, deck.BackColor) =
            (CardsTables.Cards, SpellPrefab, Pile, false, CardsArt.Back, Color.FromBytes(0x1E, 0x2C, 0x5C));
        scene.Add(deckEntity);
        var count = Label("Deck Count", new Vector2(9.0f, -2.95f), "", 0.28f, Dim);
        (count.GetComponent<Text>()!.Track, count.GetComponent<Text>()!.Output, count.GetComponent<Text>()!.Value) = (DeckName, "Count", "{0} in the deck");
        scene.Add(count);

        var pileEntity = new Entity(Pile);
        pileEntity.Transform.Position = new Vector3(9.0f, 2.1f, 0);
        var pile = pileEntity.AddComponent<Deck>();
        (pile.FaceUp, pile.TakesDrops, pile.ShuffleOnStart, pile.ClickToDraw) = (true, false, false, false);
        scene.Add(pileEntity);

        // Each side's row on the table; spells go on to the discard pile a moment after they're played.
        scene.Add(Row(RivalRow, RivalHand, new Vector2(0.4f, 2.0f), "The lich's side", Color.FromBytes(0xFF, 0x70, 0x70, 0x16)));
        scene.Add(Row(PlayerRow, PlayerHand, new Vector2(0.4f, -1.45f), "Your side: drag a card here to play it", Color.FromBytes(0x80, 0xB0, 0xFF, 0x1A)));

        // The hands: four cards each to start and one more as each turn starts, mana from one up to ten.
        scene.Add(HandOf(PlayerHand, Player, PlayerHero, new Vector2(0.4f, -4.65f), hidden: false));
        scene.Add(HandOf(RivalHand, Rival, RivalHero, new Vector2(0.4f, 5.35f), hidden: true));

        // What each side last did.
        scene.Add(Said("Rival Said", RivalHand, new Vector2(0.4f, 3.75f), Color.FromBytes(0xFF, 0xC8, 0xB8)));
        scene.Add(Said("Player Said", PlayerHand, new Vector2(0.4f, 0.28f), Color.FromBytes(0xC8, 0xDC, 0xFF)));

        // Ending the turn: the button, Tab or the gamepad's Back.
        var end = MenuButton("End Turn", new Vector2(9.0f, -4.45f), ButtonAction.EndTurn, null, Key.None, DarkGold);
        end.GetComponent<Sprite>()!.Size = new Vector2(2.8f, 0.9f);
        var endButton = end.GetComponent<Button>()!;
        (endButton.Size, endButton.Navigable) = (new Vector2(2.8f, 0.9f), false);
        end.GetComponent<Text>()!.Size = 0.42f;
        scene.Add(end);
        scene.Add(Label("End Turn Hint", new Vector2(9.0f, -5.25f), "{EndTurn}", 0.28f, Dim));

        // How to play, in the bottom-left corner.
        var help = Label("Help", Vector2.Zero, "Drag a card onto your side to play it.\nArrows and Space (or A) work too,\nShift (or B) puts a card back.", 0.27f, Dim);
        (help.GetComponent<Text>()!.Align, help.GetComponent<Text>()!.Order) = (TextAlign.Left, 200);
        var corner = help.AddComponent<ScreenAnchor>();
        (corner.Anchor, corner.Offset) = (Anchor.BottomLeft, new Vector2(0.35f, 0.9f));
        scene.Add(help);

        scene.Add(EndPanel("Victory", "Victory!", "The lich is beaten.", Gold));
        scene.Add(EndPanel("Defeat", "Defeated", "The lich wins this time.", Red));
        AddPauseMenu(scene, menuScene: Project.DefaultSceneName);
        return scene;
    }

    private static Entity Camera()
    {
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 6;
        view.Background = Night;
        view.Transition = SceneTransition.Fade;
        return camera;
    }

    private static Entity Felt()
    {
        var felt = new Entity("Table");
        felt.Transform.Position = new Vector3(0, 0, -1);
        var sprite = felt.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (CardsArt.Table, new Vector2(24, 13.5f), -10);
        return felt;
    }

    // A hero: its portrait, health and side, its name above and a bar of its health below. Falling sends `falls`.
    private static Entity Hero(string name, string side, string portrait, Vector2 at, string falls, Color color)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        (sprite.Image, sprite.Size, sprite.Order) = (portrait, new Vector2(2.2f), 5);
        var health = entity.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime, health.DestroyOnDeath, health.DeathMessage) = (Life, Life, 0, false, falls);
        entity.AddComponent<TurnTaker>().Team = side;

        var title = Label("Name", new Vector2(0, 1.45f), name, 0.4f, Light);
        WithChild(entity, title);
        var bar = new Entity("Health Bar");
        bar.Transform.Position = new Vector3(0, -1.45f, 0);
        var meter = bar.AddComponent<Bar>();
        (meter.Track, meter.Size, meter.Fill) = (name, new Vector2(2.4f, 0.36f), Color.Lerp(color, Color.FromBytes(0xD0, 0x40, 0x50), 0.6f));
        WithChild(entity, bar);
        var life = Label("Health Text", new Vector2(0, -1.45f), "", 0.28f, Light);
        var lifeText = life.GetComponent<Text>()!;
        (lifeText.Track, lifeText.Output, lifeText.Value, lifeText.Order) = (name, "Health", "{0:0} / {1:0}", 160);
        WithChild(entity, life);
        return entity;
    }

    // A side's row on the table: only its hand plays onto it, six cards at most; spells cast there go on to the discard pile.
    private static Entity Row(string name, string hand, Vector2 at, string label, Color color)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        var zone = entity.AddComponent<PlayZone>();
        (zone.From, zone.Limit, zone.DiscardPile, zone.DiscardAfter, zone.Size, zone.Spacing, zone.Label, zone.Color) =
            (hand, 6, Pile, 0.9f, new Vector2(13, 2.7f), 1.9f, label, color);
        return entity;
    }

    private static Entity HandOf(string name, string side, string hero, Vector2 at, bool hidden)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        var hand = entity.AddComponent<Hand>();
        (hand.Deck, hand.DiscardPile, hand.Hero, hand.StartWith, hand.Hidden) = (DeckName, Pile, hero, 4, hidden);
        (hand.MaxMana, hand.ManaGrowth, hand.ManaLimit) = (0, 1, 10);
        (hand.MaxWidth, hand.HoverRise, hand.HoverScale, hand.ThinkTime) = (10, hidden ? 0.6f : 1.1f, hidden ? 1.15f : 1.25f, 1.4f);
        hand.DrawAction = null;
        entity.AddComponent<TurnTaker>().Team = side;
        // A card more as each of its team's turns starts.
        var draw = entity.AddComponent<Trigger>();
        (draw.Message, draw.Do) = ($"{side}Turn", "Draw");
        return entity;
    }

    private static Entity Said(string name, string hand, Vector2 at, Color color)
    {
        var line = Label(name, at, "", 0.34f, color);
        var text = line.GetComponent<Text>()!;
        (text.Track, text.Output, text.Value) = (hand, "Said", "{0}");
        return line;
    }

    // Opens when a hero falls and stops the duel: what happened, and again or back to the menu.
    private static Entity EndPanel(string name, string heading, string detail, Color color)
    {
        var panel = new Entity(name);
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var end = panel.AddComponent<Panel>();
        (end.OpenMessage, end.CloseOnBack, end.PauseGame) = (name, false, true);
        var backdrop = panel.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(10, 6), new Color(0.04f, 0.06f, 0.05f, 0.94f), 400);

        var title = Label("Heading", new Vector2(0, 1.7f), heading, 1.3f, color);
        title.GetComponent<Text>()!.Order = 401;
        WithChild(panel, title);
        var line = Label("Detail", new Vector2(0, 0.45f), detail, 0.45f, Light);
        line.GetComponent<Text>()!.Order = 401;
        WithChild(panel, line);
        WithChild(panel, MenuButton("Again", new Vector2(-2.2f, -1.4f), ButtonAction.RestartScene, null, Key.Enter, Color.Lerp(color, Night, 0.3f), order: 410));
        WithChild(panel, MenuButton("Menu", new Vector2(2.2f, -1.4f), ButtonAction.LoadScene, Project.DefaultSceneName, Key.Escape, Gray, order: 410));
        return panel;
    }

    // ----- The cards' prefabs -----

    // A creature: a card with a health that stays on the table and acts as each of its team's turns starts (it strikes the
    // opponent's hero, or heals its own), going to the discard pile when it dies. Its row of the Cards table sets it.
    internal static Entity CreatureCard()
    {
        var entity = new Entity(CreaturePrefab);
        var card = entity.AddComponent<Card>();
        (card.Title, card.Kind, card.Cost, card.Color, card.Image) = ("Goblin", "Creature", 2, Color.FromBytes(0xE9, 0xDD, 0xC4), CardsArt.Picture("Goblin"));
        var effect = entity.AddComponent<CardEffect>();
        (effect.When, effect.Damage, effect.Effect) = (CardWhen.EachTurn, 2, "Hit");
        var health = entity.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime, health.DestroyOnDeath) = (2, 2, 0, false);
        var values = entity.AddComponent<TableValues>();
        (values.Table, values.Row) = (CardsTables.Cards, "Goblin");
        values.Sets = ["Title", "Kind", "Cost", "Color", "Image", "Damage", "Heal", "Draw", "Target", "Effect", "Sound", "Health.Max = Health"];
        return entity;
    }

    // A spell: a card that does what its row says as it's played, at the opponent, the player or what the player aims it
    // at, and goes to the discard pile.
    internal static Entity SpellCard()
    {
        var entity = new Entity(SpellPrefab);
        var card = entity.AddComponent<Card>();
        (card.Title, card.Kind, card.Cost, card.Color, card.Image) = ("Spark", "Spell", 1, Color.FromBytes(0xF6, 0xD7, 0xC3), CardsArt.Picture("Spark"));
        var effect = entity.AddComponent<CardEffect>();
        (effect.Target, effect.Damage, effect.Effect) = (CardTarget.Chosen, 2, "Hit");
        var values = entity.AddComponent<TableValues>();
        (values.Table, values.Row) = (CardsTables.Cards, "Spark");
        return entity;
    }

    // A burst of sparks that cleans itself up: a hit lands.
    private static Entity Burst(string name, Color color, int sparks, float speed, float size)
    {
        var entity = Sparkle();
        entity.Name = name;
        var emitter = entity.GetComponent<ParticleEmitter>()!;
        (emitter.BurstOnStart, emitter.Speed, emitter.StartSize, emitter.StartColor, emitter.EndColor, emitter.Lifetime) =
            (sparks, speed, size, color, color with { A = 0 }, 0.5f);
        emitter.Order = 300;
        return entity;
    }

    // Motes rising and fading: healing, a gift of mana or cards.
    private static Entity Rising(string name, Color color)
    {
        var entity = Sparkle();
        entity.Name = name;
        var emitter = entity.GetComponent<ParticleEmitter>()!;
        (emitter.BurstOnStart, emitter.Speed, emitter.Direction, emitter.Spread, emitter.StartSize, emitter.StartColor, emitter.EndColor, emitter.Lifetime) =
            (18, 2.2f, 90, 70, 0.2f, color, color with { A = 0 }, 0.8f);
        emitter.Order = 300;
        return entity;
    }
}
