using System.Numerics;
using Griff.Data;
using Griff.Exporting;
using Griff.Input;
using Griff.Modules.Cards;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Templates;
using Xunit.Abstractions;

namespace Griff.Tests.Projects;

/// <summary>
/// The Cards template: a menu, and a card duel against the computer made from the Cards table with no code, played with
/// the mouse, a touch screen's taps, the keys or a gamepad, from the menu to Victory or Defeat, the same every time for a
/// seed.
/// </summary>
public sealed class CardsTemplateTests(ITestOutputHelper output) : IDisposable
{
    private const double Frame = 1 / 60.0;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-cards-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<Project> CreateProject() => await Project.CreateAsync(_root, "Duel", ProjectTemplates.Get("Cards"));

    private Game Start(Project project, string scene, List<string>? warnings = null)
    {
        Scene Load(string name) => GameLoader.LoadScene(project, _registry, name);
        var game = new Game(Load(scene), _input)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            SceneLoader = Load,
            SceneName = scene,
            Components = _registry,
            ViewAspect = 16 / 9f,
        };
        if (warnings is not null)
            game.Problems.Reported += p => warnings.Add(p.Message);
        game.UseProjectSettings(project);
        return game;
    }

    private void Run(Game game, double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-6; t += Frame)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Tap(Game game, Key key, double after = 0.1)
    {
        _input.SetKey(key, true);
        Run(game, Frame);
        _input.SetKey(key, false);
        Run(game, after);
    }

    private void Pad(Game game, GamepadButton button, double after = 0.1)
    {
        _input.SetGamepad([button], Vector2.Zero);
        Run(game, Frame);
        _input.SetGamepad([], Vector2.Zero);
        Run(game, after);
    }

    private void Click(Game game, Vector2 at, double after = 0.1)
    {
        _input.MouseWorld = at;
        Run(game, Frame);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(game, Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(game, after);
    }

    private void Drag(Game game, Vector2 from, Vector2 to)
    {
        _input.MouseWorld = from;
        Run(game, Frame * 2);
        _input.SetMouseButton(MouseButton.Left, true);
        Run(game, Frame);
        for (var i = 1; i <= 8; i++)
        {
            _input.MouseWorld = Vector2.Lerp(from, to, i / 8f);
            Run(game, Frame);
        }

        _input.SetMouseButton(MouseButton.Left, false);
        Run(game, Frame);
        _input.MouseWorld = new Vector2(0, -20); // off the cards
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    private static Hand HandOf(Game game, string name) => game.Find(name)!.GetComponent<Hand>()!;

    private static bool Chooses(Card card) => card.Entity!.GetComponents<CardEffect>().Any(e => e.Chooses);

    private static Panel? Ended(Game game) =>
        Ends.Select(n => game.Find(n)?.GetComponent<Panel>()).FirstOrDefault(p => p is { Open: true });

    private static readonly string[] Ends = ["Victory", "Defeat"];

    [Fact]
    public async Task TheCardsTable_Loads_AndTheDeckMakesEveryCardFromIt_WithItsPictureEffectAndCost()
    {
        var project = await CreateProject();
        var tables = TableSet.Load(project.AssetsPath);
        Assert.Empty(tables.Problems);
        var cards = tables.Find(CardsTables.Cards)!;
        Assert.Equal(["Mana Coin", "Slime", "Spark", "Goblin", "Healing Potion", "Insight", "Chain Lightning", "Rogue", "Skeleton", "Cleric", "Knight", "Fireball", "Orc", "Quake", "Meteor", "Wyvern"],
            cards.Rows.Select(r => r.Name));
        Assert.Equal(CardsTables.Creatures.Concat(CardsTables.Spells).Order(), cards.Rows.Select(r => r.Name).Order());
        var prefabs = new PrefabLibrary(project, _registry);
        foreach (var row in cards.Rows)
        {
            Assert.True(File.Exists(Path.Combine(project.AssetsPath, row["Image"].ToString())), $"{row.Name}'s picture");
            Assert.True(File.Exists(Path.Combine(project.AssetsPath, row["Sound"].ToString())), $"{row.Name}'s sound");
            Assert.NotNull(prefabs.Instantiate(row["Prefab"].ToString()));
            Assert.NotNull(prefabs.Instantiate(row["Effect"].ToString()));
        }

        var warnings = new List<string>();
        var game = Start(project, CardsTemplate.DuelScene, warnings);
        Run(game, 0.1);
        Assert.Empty(warnings);

        // One deck of every card, Count copies of each, dealt from as the duel starts.
        var all = game.Scene.All<Card>().Where(c => c.Home == CardsTemplate.DeckName).ToList();
        Assert.Equal(cards.Rows.Sum(r => (int)r["Count"].AsWholeNumber), all.Count);

        // A creature: a Health from its row, striking each turn; a spell: dice to roll, a target to choose.
        var orc = all.First(c => c.Entity!.Name == "Orc");
        Assert.Equal((5, "Creature", 6f), (orc.Cost, orc.Kind, orc.Entity!.GetComponent<Health>()!.Max));
        Assert.Equal((CardWhen.EachTurn, 3f), (orc.Entity.GetComponent<CardEffect>()!.When, orc.Entity.GetComponent<CardEffect>()!.Damage));
        Assert.Equal("Each turn: deal 3 damage.", orc.ShownText);
        Assert.Equal(CardsArt.Picture("Orc"), orc.Image);
        var fireball = all.First(c => c.Entity!.Name == "Fireball");
        Assert.Null(fireball.Entity!.GetComponent<Health>());
        Assert.Equal((4, "1d4+3", CardTarget.Chosen, "Blast"), (fireball.Cost, fireball.Entity.GetComponent<CardEffect>()!.DamageRoll,
            fireball.Entity.GetComponent<CardEffect>()!.Target, fireball.Entity.GetComponent<CardEffect>()!.Effect));
        Assert.Equal("Deal 1d4+3 damage to a target.", fireball.ShownText);
        Assert.Equal("Gain 2 mana.", all.First(c => c.Entity!.Name == "Mana Coin").ShownText);
    }

    [Fact]
    public async Task FromTheMenu_ADuelIsPlayedWithTheMouse_ToVictoryOrDefeat_TheSameEveryTimeForASeed()
    {
        var project = await CreateProject();
        var first = PlayToTheEnd(project, seed: 4);
        output.WriteLine(first);
        Assert.Equal(first, PlayToTheEnd(project, seed: 4));
    }

    // Clicks Play on the menu, then plays the player's turns with the mouse: every card it can pay for dragged onto its row
    // (a spell that's aimed dropped straight on the lich), then Tab; until a hero falls. Says how it went.
    private string PlayToTheEnd(Project project, int seed)
    {
        Rand.Seed(seed);
        var game = Start(project, Project.DefaultSceneName);
        Run(game, 0.2);
        Click(game, At(game.Find("Play")!), after: 0.8);
        Assert.Equal(CardsTemplate.DuelScene, game.SceneName);

        var order = game.Find("Turns")!.GetComponent<TurnOrder>()!;
        var hand = HandOf(game, CardsTemplate.PlayerHand);
        var rival = HandOf(game, CardsTemplate.RivalHand);
        var row = game.Find(CardsTemplate.PlayerRow)!.GetComponent<PlayZone>()!;
        var lich = game.Find(CardsTemplate.RivalHero)!;
        var mage = game.Find(CardsTemplate.PlayerHero)!;
        var played = 0;
        var rivalSaid = new HashSet<string>();
        for (var turn = 0; turn < 40 && Ended(game) is null; turn++)
        {
            for (var wait = 0; wait < 600 && Ended(game) is null && !order.IsTurn("Player"); wait++)
            {
                Run(game, 0.1);
                if (rival.Said is { Length: > 0 } said)
                    rivalSaid.Add(said);
            }

            if (Ended(game) is not null)
                break;
            Run(game, 0.6); // the card drawn arrives
            Assert.Equal(Math.Min(10, order.Round), hand.MaxMana);
            var skipped = new HashSet<Card>();
            for (var plays = 0; plays < 12 && Ended(game) is null; plays++)
            {
                var card = hand.Cards.FirstOrDefault(c => c.Cost <= hand.Mana && !skipped.Contains(c) && c.IsResting);
                if (card is null)
                    break;
                var count = hand.Count;
                var at = At(card.Entity!) - new Vector2(0.25f, 0);
                Drag(game, at, Chooses(card) ? At(lich) : At(row.Entity!));
                Run(game, 0.5);
                if (card.Zone == hand)
                    skipped.Add(card);
                else
                    played++;
                Assert.True(card.Zone == hand || hand.Count < count || card.Entity!.GetComponent<CardEffect>()?.Draw > 0);
            }

            Tap(game, Key.Tab);
        }

        var ended = Ended(game);
        Assert.NotNull(ended);
        Assert.True(game.IsPaused, "the duel stops once it's won or lost");
        Assert.True(played >= 5, $"the player played {played} cards");
        Assert.Contains(rivalSaid, s => s.StartsWith("Rival plays ", StringComparison.Ordinal));
        var won = ended!.Entity!.Name == "Victory";
        Assert.Equal(won, lich.GetComponent<Health>()!.IsDead);
        Assert.Equal(!won, mage.GetComponent<Health>()!.IsDead);
        return $"{ended.Entity.Name} in round {order.Round}: Mage {mage.GetComponent<Health>()!.Current}, Lich {lich.GetComponent<Health>()!.Current}, " +
               $"{played} played; the rival said {string.Join(" / ", rivalSaid.Take(6))}";
    }

    [Fact]
    public async Task TheKeys_AGamepad_AndTaps_PlayCardsToo_AndEndTheTurn()
    {
        var project = await CreateProject();
        Rand.Seed(2);
        var game = Start(project, CardsTemplate.DuelScene);
        Run(game, 1.2);
        var order = game.Find("Turns")!.GetComponent<TurnOrder>()!;
        var hand = HandOf(game, CardsTemplate.PlayerHand);
        var row = game.Find(CardsTemplate.PlayerRow)!.GetComponent<PlayZone>()!;
        Assert.True(order.IsTurn("Player"));
        Assert.Equal(5, hand.Count);
        game.Do(hand.Entity!, "GainMana", 20); // enough for anything
        _input.MouseWorld = new Vector2(0, -20);

        // The keys: Right shows the cursor, Space takes the card up, Space plays it on the row; a spell that's aimed waits
        // for Space again, at what the computer would pick.
        Tap(game, Key.Right);
        Tap(game, Key.Space);
        var picked = hand.Picked!;
        Assert.NotNull(picked);
        Tap(game, Key.Space);
        if (Chooses(picked))
            Tap(game, Key.Space);
        Run(game, 0.3);
        Assert.NotSame(hand, picked.Zone);
        Assert.StartsWith($"Player plays {picked.ShownTitle}", hand.Said, StringComparison.Ordinal);

        // A gamepad: the d-pad and A, and B puts a card back.
        Pad(game, GamepadButton.DPadLeft);
        Pad(game, GamepadButton.A);
        Assert.NotNull(hand.Picked);
        Pad(game, GamepadButton.B);
        Assert.Null(hand.Picked);
        Pad(game, GamepadButton.A);
        var padded = hand.Picked!;
        Pad(game, GamepadButton.A);
        if (Chooses(padded))
            Pad(game, GamepadButton.A);
        Run(game, 0.3);
        Assert.NotSame(hand, padded.Zone);

        // A touch screen: tap a card, then tap the table (or for an aimed spell, the lich).
        var tapped = hand.Cards[0];
        Click(game, At(tapped.Entity!) - new Vector2(0.25f, 0));
        Assert.Equal(tapped, hand.Picked);
        Click(game, At(row.Entity!));
        if (Chooses(tapped))
            Click(game, At(game.Find(CardsTemplate.RivalHero)!));
        Run(game, 0.3);
        Assert.NotSame(hand, tapped.Zone);

        // The gamepad's Back ends the turn; the computer plays the rival's (with mana to spare), from its hidden hand.
        var rival = HandOf(game, CardsTemplate.RivalHand);
        rival.MaxMana = 6;
        Pad(game, GamepadButton.Back);
        Run(game, 0.8);
        Assert.Equal("Rival", order.Current);
        Assert.All(rival.Cards, c => Assert.True(c.HiddenHere));
        Assert.True(Wait(game, () => rival.Cards.Any(c => c.Readied), 3), "the computer shows the card it's about to play");
        Assert.True(Wait(game, () => rival.Said?.StartsWith("Rival plays ", StringComparison.Ordinal) == true, 3), "and then plays it");
        Assert.True(Wait(game, () => order.IsTurn("Player"), 20), "and ends its turn");
    }

    private bool Wait(Game game, Func<bool> until, double seconds)
    {
        for (var t = 0.0; t < seconds; t += 0.05)
        {
            if (until())
                return true;
            Run(game, 0.05);
        }

        return until();
    }

    [Fact]
    public async Task Spells_GoToTheDiscardPile_WhileCreaturesStay_AndStrikeAsTheirSidesTurnsStart()
    {
        var project = await CreateProject();
        Rand.Seed(3);
        var game = Start(project, CardsTemplate.DuelScene);
        Run(game, 1.2);
        var order = game.Find("Turns")!.GetComponent<TurnOrder>()!;
        var hand = HandOf(game, CardsTemplate.PlayerHand);
        var row = game.Find(CardsTemplate.PlayerRow)!.GetComponent<PlayZone>()!;
        var pile = game.Find(CardsTemplate.Pile)!.GetComponent<Deck>()!;
        var lich = game.Find(CardsTemplate.RivalHero)!.GetComponent<Health>()!;

        // A goblin and a spark, played by a script as a player would.
        Card Take(string name)
        {
            // One from the deck, the discard pile or the rival's hand: wherever the shuffle put them.
            var card = game.Scene.All<Card>().First(c => c.Entity!.Name == name && (c.Zone is Deck || c.Zone?.Entity?.Name == CardsTemplate.RivalHand));
            game.Scene.Move(card.Entity!, hand.Entity);
            return card;
        }

        var goblin = Take("Goblin");
        var spark = Take("Spark");
        Run(game, 0.5);
        game.Do(hand.Entity!, "GainMana", 5);
        hand.Play(goblin, row);
        hand.Play(spark, row, lich.Entity);
        Run(game, 0.1);
        Assert.Equal(18f, lich.Current);
        Assert.Equal(row, goblin.Zone);
        Assert.Equal(row, spark.Zone);

        // The spark goes on to the discard pile a moment after it lands; the goblin stays.
        Run(game, 1.5);
        Assert.Equal(pile, spark.Zone);
        Assert.Equal(row, goblin.Zone);

        // As the player's next turn starts (the rival keeping its cards), the goblin strikes the lich, and the hand says so.
        HandOf(game, CardsTemplate.RivalHand).Keep = 99;
        Tap(game, Key.Tab);
        Assert.True(Wait(game, () => goblin.Strikes > 0, 30));
        Run(game, Frame);
        Assert.True(goblin.Striking > 0, "it lunges at the lich");
        Assert.True(lich.Entity!.GetComponent<Sprite>()!.FlashOverlay.A > 0, "which flashes");
        Assert.Equal(16f, lich.Current);
        Assert.Equal("Goblin: 2 damage to Lich.", hand.Said);
        Assert.Equal((1, "Lich"), (goblin.Strikes, goblin.StruckAt));
        Assert.Equal("Player", order.Current);

        // A spell aimed at the goblin hurts it: it flashes and shakes, and dies at nothing, going to the discard pile.
        Assert.True(Wait(game, () => order.IsTurn("Player"), 5));
        var bolt = Take("Spark");
        Run(game, 0.5);
        hand.Play(bolt, row, goblin.Entity);
        Run(game, Frame * 2);
        Assert.Equal(pile, goblin.Zone);
        Assert.Equal(2f, goblin.Entity!.GetComponent<Health>()!.Current);
        var orc = Take("Orc");
        Run(game, 0.5);
        game.Do(hand.Entity!, "GainMana", 5);
        hand.Play(orc, row);
        var zap = Take("Spark");
        Run(game, 0.5);
        hand.Play(zap, row, orc.Entity);
        Run(game, Frame * 2);
        Assert.Equal(4f, orc.Entity!.GetComponent<Health>()!.Current);
        Assert.True(orc.Hurting > 0, "the orc flashes");
    }

    [Fact]
    public async Task Exports_ForTheDesktopAndTheWeb_CarryTheTableAndTheCards()
    {
        var project = await CreateProject();
        string[] wanted = [.. CardsTables.All().Keys.Select(k => "Assets/" + k), "Assets/Prefabs/Creature.prefab", "Assets/Prefabs/Spell.prefab",
            "Assets/" + CardsArt.Picture("Wyvern"), "Assets/" + CardsArt.Back];

        // The desktop: one .exe, holding the game's files, which plays the duel.
        var player = Path.Combine(_root, "player", "Griff.Player.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(player)!);
        File.WriteAllText(player, "player exe");
        var desktop = GameExporter.Export(project, player, Path.Combine(_root, "out"));
        Assert.True(desktop.Success, desktop.Error);
        var extracted = GamePackage.Extract(desktop.Executable!, Path.Combine(_root, "cache"), "Duel")!;
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(extracted, file)), file));
        var loaded = await GameLoader.LoadAsync(extracted);
        Assert.Empty(loaded.Problems);
        var game = (loaded with { Scene = loaded.LoadScene(CardsTemplate.DuelScene), SceneName = CardsTemplate.DuelScene }).NewGame();
        for (var i = 0; i < 30; i++)
            game.Tick(1 / 60f);
        Assert.True(HandOf(game, CardsTemplate.PlayerHand).Count > 0);
        Assert.Contains(game.Scene.All<Card>(), c => c.Entity!.GetComponent<Health>() is not null);

        // The web: the files the browser fetches are listed.
        var web = Path.Combine(_root, "web-player");
        Directory.CreateDirectory(Path.Combine(web, "_framework"));
        File.WriteAllText(Path.Combine(web, WebExporter.Page), "<html>");
        var site = WebExporter.Export(project, web, Path.Combine(_root, "web-out"));
        Assert.True(site.Success, site.Error);
        var gameFolder = Path.Combine(_root, "web-out", ExportLayout.GameFolder);
        var listed = File.ReadAllLines(Path.Combine(gameFolder, ExportLayout.WebFileList));
        Assert.All(wanted, file => Assert.Contains(file, listed));
        Assert.All(wanted, file => Assert.True(File.Exists(Path.Combine(gameFolder, file)), file));
    }
}
