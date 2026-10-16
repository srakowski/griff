using System.Numerics;
using Griff.Audio;
using Griff.Data;
using Griff.Input;
using Griff.Scenes;
using Griff.Templates;
using static Griff.Templates.ProjectTemplates;

namespace Griff.Modules.Battles;

// The RPG Stats template: a menu to pick a hero's class and a foe, and a turn-based battle between them, both fighters
// taking every number from the game's tables through Table Values (no code): races, classes, items, spells, levels
// generated from curves, and enemies. Its Balance simulation plays each class at each level against each enemy in an
// Arena scene by the same rules, and shows one class too strong from the middle levels on, from a slip in the Spells
// table.
internal sealed class RpgStatsTemplate : ProjectTemplate
{
    public override string Title => "RPG Stats";

    public override string Description => "Pick a class and a foe and fight it out by turns: attack, cast your spell or use your item. Races, classes, items, spells, levels and enemies are tables, and a Balance simulation, ready to run in Data > Simulations, plays each class at each level against each enemy to show which is too strong where. 1 to attack, 2 to cast, 3 for your item (or click), arrows to choose in the menu.";

    public override int Order => 125;

    protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Heroic, seed: 21) with { Volume = 0.5f };

    protected override Scene BuildStartScene() => Menu();

    protected override Dictionary<string, Scene> BuildMoreScenes() => new()
    {
        ["Scenes/Battle.scene"] = Battle(),
        ["Scenes/Arena.scene"] = Arena(),
    };

    protected override Dictionary<string, Entity> BuildPrefabs() => new() { ["Prefabs/Sparkle.prefab"] = Sparkle() };

    public override IReadOnlyDictionary<string, byte[]> Images() => RpgStatsArt.Files();

    public override IReadOnlyDictionary<string, Table> Tables() => RpgStatsTables.All();

    public override IReadOnlyDictionary<string, Simulation> Simulations() => new Dictionary<string, Simulation>
    {
        [$"{SimulationFile.Folder}/{BalanceName}{SimulationFile.Extension}"] = Balance(),
    };

    // Fighters swing, cast and use items with sounds of their own; the hero's hurt is the player's.
    protected override void WireSounds(Entity entity)
    {
        if (entity.GetComponent<Battler>() is { } battler)
        {
            TemplateSounds.Set(battler.AttackSound, s => battler.AttackSound = s, TemplateSounds.Swing);
            TemplateSounds.Set(battler.SpellSound, s => battler.SpellSound = s, TemplateSounds.Shoot);
            TemplateSounds.Set(battler.ItemSound, s => battler.ItemSound = s, TemplateSounds.PowerUp);
            TemplateSounds.Set(battler.FailSound, s => battler.FailSound = s, TemplateSounds.Switch);
            if (entity.Name == Hero && entity.GetComponent<Health>() is { } health)
                TemplateSounds.Set(health.HurtSound, s => health.HurtSound = s, TemplateSounds.Hurt);
        }

        base.WireSounds(entity);
    }

    /// <summary>The balance simulation it comes with.</summary>
    public const string BalanceName = "Balance";

    private const string Hero = "Hero";
    private const string Enemy = "Enemy";
    private const string HeroKey = "hero";
    private const string FoeKey = "foe";

    private static readonly Color Gold = Color.FromBytes(0xF2, 0xC9, 0x4C);
    private static readonly Color Light = Color.FromBytes(0xEC, 0xE8, 0xDC);
    private static readonly Color Dim = Color.FromBytes(0xA8, 0xA4, 0xB8);
    private static readonly Color Night = Color.FromBytes(0x14, 0x12, 0x22);
    private static readonly Color Blue = Color.FromBytes(0x4C, 0x8D, 0xF0);
    private static readonly Color Red = Color.FromBytes(0xC8, 0x48, 0x40);
    private static readonly Color Gray = Color.FromBytes(0x3A, 0x3C, 0x4E);
    private static readonly Color DarkGold = Color.FromBytes(0xA8, 0x86, 0x2E);
    private static readonly Color Purple = Color.FromBytes(0x7A, 0x4A, 0xC8);

    // Each class at each level against each enemy, 100 fights a pair, in the Arena scene.
    internal static Simulation Balance() => new()
    {
        Name = BalanceName,
        Arena = "Arena",
        First = new SimulationSide { Entity = Hero, Table = RpgStatsTables.Classes },
        Second = new SimulationSide { Entity = Enemy, Table = RpgStatsTables.Enemies },
        Runs = 100,
        Seed = 1,
        TimeLimit = 120,
        Rewards = ["Gold", "XP"],
        Track = ["Mana = Hero.Mana"],
        Sweep = new SimulationSweep { Table = RpgStatsTables.Classes, Column = "Level", From = 1, To = RpgStatsTables.TopLevel, Step = 1 },
    };

    // ----- The menu: pick a class and a foe, then fight

    private static Scene Menu()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 6;
        view.Background = Night;
        view.Transition = SceneTransition.Fade;
        scene.Add(camera);

        scene.Add(Label("Title", new Vector2(0, 4.4f), "RPG STATS", 1.4f, Gold));
        scene.Add(Label("Subtitle", new Vector2(0, 3.3f), "Every number in this fight comes from a table.", 0.5f, Dim));

        scene.Add(Picker("Class", new Vector2(0, 1.9f), "Class: < {0} >", RpgStatsTables.ClassNames, HeroKey, Blue));
        scene.Add(Picker("Foe", new Vector2(0, 0.6f), "Foe: < {0} >", RpgStatsTables.EnemyNames, FoeKey, Red));
        scene.Add(MenuButton("Fight", new Vector2(0, -0.8f), ButtonAction.LoadScene, "Battle", Key.Enter, DarkGold));
        scene.Add(MenuButton("Quit", new Vector2(0, -2.1f), ButtonAction.Quit, null, Key.Escape, Gray));

        // What the template is for: the tables, and the simulation that tests them.
        var note = Label("Balance Note", new Vector2(0, -3.6f),
            "Is it fair? In the editor, open Data > Simulations, pick Balance and click Run:\neach class at each level against each enemy, 100 fights a pair. One class is too strong.",
            0.36f, Light);
        scene.Add(note);

        var hint = Label("Hint", Vector2.Zero, "Arrows to choose  ·  Left and right to change  ·  Enter to fight", 0.4f, Dim);
        var anchor = hint.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 0.5f);
        scene.Add(hint);
        return scene;
    }

    // A menu line whose left and right pick one of the options, kept in the save data for the battle to read.
    private static Entity Picker(string name, Vector2 position, string format, string[] options, string saveAs, Color color)
    {
        var entity = MenuButton(name, position, ButtonAction.None, null, Key.None, color);
        entity.GetComponent<Sprite>()!.Size = new Vector2(6, 1.1f);
        entity.GetComponent<Button>()!.Size = new Vector2(6, 1.1f);
        var choice = entity.AddComponent<Choice>();
        (choice.Options, choice.Index, choice.Format, choice.SaveAs) = (options, 0, format, saveAs);
        entity.GetComponent<Text>()!.Value = format.Replace("{0}", options[0], StringComparison.Ordinal);
        return entity;
    }

    // ----- The battle

    private static Scene Battle()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 6;
        view.Background = Night;
        view.Transition = SceneTransition.Fade;
        scene.Add(camera);

        var meadow = new Entity("Meadow");
        meadow.Transform.Position = new Vector3(0, 0, -1);
        var ground = meadow.AddComponent<Sprite>();
        (ground.Image, ground.Size, ground.Order) = (RpgStatsArt.Meadow, new Vector2(24, 13.5f), -10);
        scene.Add(meadow);

        // The hero plays the class picked in the menu (the Knight without one), the computer the foe picked (a Goblin).
        var hero = Fighter(Hero, RpgStatsTables.Classes, "Knight", Enemy, new Vector2(-4.5f, -0.6f), shown: true);
        hero.GetComponent<TableValues>()!.SavedRow = HeroKey;
        hero.GetComponent<Health>()!.DeathMessage = "Defeat";
        scene.Add(hero);
        var enemy = Fighter(Enemy, RpgStatsTables.Enemies, "Goblin", Hero, new Vector2(4.5f, -0.6f), shown: true);
        enemy.GetComponent<TableValues>()!.SavedRow = FoeKey;
        enemy.GetComponent<Health>()!.DeathMessage = "Victory";
        enemy.GetComponent<Sprite>()!.FlipX = true;
        // Their default rows' pictures, to see in the editor; playing, each takes its row's.
        hero.GetComponent<Sprite>()!.Image = RpgStatsArt.Knight;
        enemy.GetComponent<Sprite>()!.Image = RpgStatsArt.Goblin;
        scene.Add(enemy);

        // The hero's turn, then the enemy's, which the computer plays.
        var turns = new Entity("Turns");
        turns.Transform.Position = new Vector3(0, 4.9f, 0);
        var order = turns.AddComponent<TurnOrder>();
        (order.Teams, order.ComputerTeams, order.Between, order.Format, order.EndAction) = ($"{Hero}, {Enemy}", Enemy, 0.5f, "Round {1}: {0}'s turn", null);
        var turnText = turns.AddComponent<Text>();
        (turnText.Value, turnText.Size, turnText.Color, turnText.Order) = ("Round 1: Hero's turn", 0.55f, Light, 150);
        scene.Add(turns);

        AddStatus(scene, Hero, -4.5f, withMana: true);
        AddStatus(scene, Enemy, 4.5f, withMana: false);

        // What each did last.
        scene.Add(Said("Hero Said", Hero, -3.0f, Light));
        scene.Add(Said("Enemy Said", Enemy, -3.6f, Dim));

        // The hero's moves: buttons that do the Battler's actions (faded off its turn, or without the mana or the item).
        scene.Add(Command("Attack", "Attack", new Vector2(-6, -4.9f), Key.D1, DarkGold, "1  Attack", null, null));
        scene.Add(Command("Cast Spell", "CastSpell", new Vector2(0, -4.9f), Key.D2, Purple, "2  {0}", "Spell", ("Spell Cost", "SpellCost", "{0} mana")));
        scene.Add(Command("Use Item", "UseItem", new Vector2(6, -4.9f), Key.D3, Blue, "3  {0}", "Item", ("Items Left", "Items", "{0} left")));

        scene.Add(EndPanel("Victory", "Victory!", Gold, rewards: true));
        scene.Add(EndPanel("Defeat", "Defeated", Red, rewards: false));
        AddPauseMenu(scene, menuScene: "Main");
        return scene;
    }

    // A fighter: its picture, health, turns and moves, every number from its row (picked in the menu in the battle).
    private static Entity Fighter(string name, string table, string row, string foe, Vector2 at, bool shown)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        if (shown)
        {
            var sprite = entity.AddComponent<Sprite>();
            (sprite.Size, sprite.Order) = (new Vector2(3.2f), 5);
        }

        var health = entity.AddComponent<Health>();
        (health.Max, health.Current, health.InvincibleTime) = (10, 10, 0);
        var battler = entity.AddComponent<Battler>();
        battler.Foe = foe;
        if (shown)
            battler.Effect = "Sparkle";
        else
            battler.ThinkTime = 0;
        var taker = entity.AddComponent<TurnTaker>();
        (taker.Team, taker.Actions) = (name, 1);

        var values = entity.AddComponent<TableValues>();
        (values.Table, values.Row) = (table, row);
        values.Sets = table == RpgStatsTables.Classes
            ? [
                "Health.Max = Health",
                .. shown ? ["Sprite.Image = Picture"] : Array.Empty<string>(),
                "Battler.Title = Title",
                "Battler.Damage = Damage",
                "Battler.Armor = Armor",
                "Battler.MaxMana = Mana",
                "Battler.ManaPerTurn = Mana Regen",
                "Battler.Spell = Spell",
                "Battler.SpellCost = Spell Cost",
                "Battler.SpellDamage = Spell Damage",
                "Battler.SpellHeal = Spell Heal",
                "Battler.Item = Item",
                "Battler.Items = Items",
                "Battler.ItemHeal = Item Heal",
                "Battler.ItemDamage = Item Damage",
                "Battler.ItemMana = Item Mana",
            ]
            : [
                "Health.Max = Health",
                .. shown ? ["Sprite.Image = Picture"] : Array.Empty<string>(),
                "Battler.Title = Title",
                "Battler.Damage = Damage",
                "Battler.Armor = Armor",
            ];
        return entity;
    }

    // Above a fighter: its name, its health, and its mana.
    private static void AddStatus(Scene scene, string fighter, float x, bool withMana)
    {
        var name = Label($"{fighter} Name", new Vector2(x, 2.9f), fighter, 0.55f, Light);
        var text = name.GetComponent<Text>()!;
        (text.Track, text.Output, text.Value) = (fighter, "Title", "{0}");
        scene.Add(name);

        scene.Add(Meter($"{fighter} Health", fighter, null, new Vector2(x, 2.2f), Color.FromBytes(0xD0, 0x40, 0x50)));
        var hp = Label($"{fighter} Health Text", new Vector2(x, 2.2f), "", 0.3f, Light);
        var hpText = hp.GetComponent<Text>()!;
        (hpText.Track, hpText.Output, hpText.Value, hpText.Order) = (fighter, "Health", "{0:0} / {1:0}", 160);
        scene.Add(hp);
        if (!withMana)
            return;
        scene.Add(Meter($"{fighter} Mana", fighter, "Mana", new Vector2(x, 1.75f), Color.FromBytes(0x4C, 0x8D, 0xF0)));
        var mp = Label($"{fighter} Mana Text", new Vector2(x, 1.75f), "", 0.3f, Light);
        var mpText = mp.GetComponent<Text>()!;
        (mpText.Track, mpText.Output, mpText.Value, mpText.Order) = (fighter, "Mana", "{0:0} / {1:0} mana", 160);
        scene.Add(mp);
    }

    private static Entity Meter(string name, string track, string? output, Vector2 at, Color fill)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        var bar = entity.AddComponent<Bar>();
        (bar.Track, bar.Output, bar.Size, bar.Fill) = (track, output, new Vector2(3.4f, 0.36f), fill);
        return entity;
    }

    private static Entity Said(string name, string fighter, float y, Color color)
    {
        var line = Label(name, new Vector2(0, y), "", 0.42f, color);
        var text = line.GetComponent<Text>()!;
        (text.Track, text.Output, text.Value) = (fighter, "Said", "{0}");
        return line;
    }

    // One of the hero's moves: a button doing a Battler action on the hero, its label (and a line under it) showing outputs.
    private static Entity Command(string name, string action, Vector2 at, Key key, Color color, string label, string? output, (string Name, string Output, string Format)? under)
    {
        var entity = MenuButton(name, at, ButtonAction.Do, null, key, color);
        entity.GetComponent<Sprite>()!.Size = new Vector2(5, 1.1f);
        var button = entity.GetComponent<Button>()!;
        (button.Size, button.Do, button.Targets, button.Fade) = (new Vector2(5, 1.1f), action, Hero, 0);
        var text = entity.GetComponent<Text>()!;
        (text.Value, text.Size) = (label, 0.5f);
        if (output is not null)
            (text.Track, text.Output) = (Hero, output);
        if (under is var (lineName, lineOutput, format))
        {
            var line = Label(lineName, new Vector2(0, -0.8f), "", 0.32f, Dim);
            var lineText = line.GetComponent<Text>()!;
            (lineText.Track, lineText.Output, lineText.Value) = (Hero, lineOutput, format);
            WithChild(entity, line);
        }

        return entity;
    }

    // Opens when a fighter falls: the heading, what was won (from the foe's row), and again or back to the menu.
    private static Entity EndPanel(string name, string heading, Color color, bool rewards)
    {
        var panel = new Entity(name);
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var end = panel.AddComponent<Panel>();
        (end.OpenMessage, end.CloseOnBack, end.PauseGame) = (name, false, false);
        var backdrop = panel.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new Vector2(10, 6), new Color(0.05f, 0.05f, 0.09f, 0.94f), 400);

        var title = Label("Heading", new Vector2(0, 1.8f), heading, 1.2f, color);
        title.GetComponent<Text>()!.Order = 401;
        WithChild(panel, title);

        var detail = Label("Detail", new Vector2(0, 0.6f), rewards ? "" : "Try another class, or change its row in the Classes table.", 0.42f, Light);
        detail.GetComponent<Text>()!.Order = 401;
        if (rewards)
        {
            // The foe's Reward column: a formula of its Gold and XP.
            var values = detail.AddComponent<TableValues>();
            (values.Table, values.Row, values.SavedRow, values.Sets) = (RpgStatsTables.Enemies, "Goblin", FoeKey, ["Text.Value = Reward"]);
        }

        WithChild(panel, detail);
        WithChild(panel, MenuButton("Again", new Vector2(-2.2f, -1.6f), ButtonAction.RestartScene, null, Key.Enter, Color.Lerp(color, Night, 0.25f), order: 410));
        WithChild(panel, MenuButton("Menu", new Vector2(2.2f, -1.6f), ButtonAction.LoadScene, "Main", Key.Escape, Gray, order: 410));
        return panel;
    }

    // ----- The arena the Balance simulation fights in

    // The same fighters by the same rules, with no pause before the computer moves or between turns, both played by the
    // computer: the simulation gives each its row, fight after fight.
    private static Scene Arena()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 6;
        view.Background = Night;
        scene.Add(camera);

        scene.Add(Fighter(Hero, RpgStatsTables.Classes, "Knight", Enemy, new Vector2(-3, 0), shown: false));
        scene.Add(Fighter(Enemy, RpgStatsTables.Enemies, "Goblin", Hero, new Vector2(3, 0), shown: false));
        foreach (var fighter in scene.Entities.Where(e => e.GetComponent<Battler>() is not null))
        {
            var marker = fighter.AddComponent<Sprite>();
            (marker.Size, marker.Tint) = (new Vector2(1.5f), fighter.Name == Hero ? Blue : Red);
        }

        var turns = new Entity("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Teams, order.ComputerTeams, order.Between) = ($"{Hero}, {Enemy}", $"{Hero}, {Enemy}", 0);
        scene.Add(turns);

        scene.Add(Label("Arena Note", new Vector2(0, 3.5f),
            "The arena the Balance simulation fights in (Data > Simulations).\nIt gives Hero a row of Classes and Enemy a row of Enemies, fight after fight.",
            0.4f, Light));
        return scene;
    }
}
