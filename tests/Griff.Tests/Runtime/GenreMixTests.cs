using System.Numerics;
using Griff.Data;
using Griff.Input;
using Griff.Modules.Adventures;
using Griff.Modules.BlockWorlds;
using Griff.Modules.Cards;
using Griff.Modules.Contraptions;
using Griff.Modules.FirstPerson;
using Griff.Modules.PhysicsGames;
using Griff.Modules.Rhythm;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// The newer genres mixed with each other and the rest with no code (the Mixing genres guide): a card that spawns an enemy, a
/// rhythm note that fires a weapon, a pinball bumper that hurts a boss and a hotspot that opens a door. Each scene is built
/// from components alone, saved and loaded back (so all there is to it is what a scene file holds), and played the way a
/// player would.
/// </summary>
public sealed class GenreMixTests
{
    private const float Frame = 1 / 60f;
    private static readonly SceneSerializer Serializer = new(ComponentRegistry.CreateDefault());

    // The scene as a scene file holds it: no scripts, nothing but its components' fields.
    private static Scene Saved(Scene scene) => Serializer.LoadFromString(Serializer.SaveToString(scene));

    private static void Run(Game game, InputState input, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Frame)
        {
            game.Tick(Frame);
            input.EndFrame();
        }
    }

    private static Vector2 At(Entity entity) => new(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    private static Entity Add(Scene scene, string name, float x, float y, Entity? parent = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(x, y, 0);
        scene.Add(entity, parent);
        return entity;
    }

    [Fact]
    public void ACardPlayed_SpawnsAnEnemy()
    {
        var scene = new Scene();
        var deck = Add(scene, "Deck", 8, -1).AddComponent<Deck>();
        (deck.ShuffleOnStart, deck.ClickToDraw) = (false, false);
        var pile = Add(scene, "Discard Pile", 8, 2).AddComponent<Deck>();
        (pile.FaceUp, pile.TakesDrops, pile.ShuffleOnStart, pile.ClickToDraw) = (true, true, false, false);
        var hand = Add(scene, "Hand", 0, -3.5f).AddComponent<Hand>();
        (hand.Deck, hand.StartWith, hand.DiscardPile) = ("Deck", 0, "Discard Pile");
        var table = Add(scene, "Table", 0, 0.5f).AddComponent<PlayZone>();
        (table.DiscardPile, table.Size) = ("Discard Pile", new Vector2(9, 2.4f));
        var card = Add(scene, "Summon Slime", 0, 0, hand.Entity);
        card.AddComponent<Card>();
        var effect = card.AddComponent<CardEffect>();
        (effect.Do, effect.DoOn) = ("Spawn", "Slime Pit");

        // A platformer's spawner, which only spawns when told to.
        var spawner = Add(scene, "Slime Pit", 12, 6).AddComponent<Spawner>();
        (spawner.Prefab, spawner.Running) = ("Slime", false);

        var input = new InputState();
        var game = new Game(Saved(scene), input)
        {
            PrefabLoader = name =>
            {
                var enemy = new Entity(name);
                enemy.AddComponent<Health>().Max = 3;
                return enemy;
            },
        };
        Run(game, input, 0.5f);
        Assert.Null(game.Find("Slime"));

        // The player drags the card onto the table.
        var played = game.Find("Summon Slime")!;
        var onto = At(game.Find("Table")!);
        input.MouseWorld = At(played);
        Run(game, input, Frame);
        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, Frame);
        for (var i = 1; i <= 6; i++)
        {
            input.MouseWorld = Vector2.Lerp(At(played), onto, i / 6f);
            Run(game, input, Frame);
        }

        input.SetMouseButton(MouseButton.Left, false);
        Run(game, input, 0.2f);

        var slime = game.Find("Slime");
        Assert.NotNull(slime);
        Assert.Equal(new Vector2(12, 6), At(slime));
        Assert.NotNull(slime.GetComponent<Health>());
    }

    [Fact]
    public void ARhythmNoteHit_FiresAWeapon()
    {
        var scene = new Scene();
        var lanes = Add(scene, "Lanes", 0, 0);
        lanes.AddComponent<BeatClock>().Tempo = 120;
        var notes = lanes.AddComponent<NoteLanes>();
        (notes.Chart, notes.Difficulty) = ("Chart", "");

        // A first-person hero who doesn't have the input (the lanes do), with a gun that fires on each note hit.
        var hero = Add(scene, "Hero", 0, 20);
        hero.AddComponent<FirstPersonMovement>().Controllable = false;
        var gun = hero.AddComponent<Weapon>();
        (gun.Damage, gun.FireRate) = (1, 20);
        var trigger = hero.AddComponent<Trigger>();
        (trigger.Message, trigger.From, trigger.Do) = ("Hit", "Lanes", "Fire");
        var target = Add(scene, "Target", 6, 20);
        target.AddComponent<BoxCollider>().Size = new Vector2(1, 1);
        var health = target.AddComponent<Health>();
        (health.Max, health.InvincibleTime) = (10, 0);

        var input = new InputState();
        var game = new Game(Saved(scene), input) { Audio = new SilentAudio(), Tables = new TableSet([Chart(4, 6, 8)]) };
        var clock = game.Find("Lanes")!.GetComponent<BeatClock>()!;
        var weapon = game.Find("Hero")!.GetComponent<Weapon>()!;
        var hurt = game.Find("Target")!.GetComponent<Health>()!;

        // The player taps lane 1 (D) on the first two notes and lets the third go by.
        foreach (var beat in new[] { 4.0, 6.0 })
        {
            while (clock.Beats < beat - 2.0 / 60 - 1e-6)
                Run(game, input, Frame);
            input.SetKey(Key.D, true);
            Run(game, input, Frame);
            input.SetKey(Key.D, false);
            Run(game, input, Frame * 3);
        }

        Assert.Equal(2, game.Find("Lanes")!.GetComponent<NoteLanes>()!.Perfects + game.Find("Lanes")!.GetComponent<NoteLanes>()!.Greats);
        Assert.Equal(2, weapon.Shots);
        Assert.Equal(8, hurt.Current);
        while (clock.Beats < 9)
            Run(game, input, Frame);
        Assert.Equal(1, game.Find("Lanes")!.GetComponent<NoteLanes>()!.Misses);
        Assert.Equal(2, weapon.Shots);
    }

    [Fact]
    public void APinballBumperHit_HurtsABoss()
    {
        var scene = new Scene();
        var bumper = Add(scene, "Bumper", 0, 0);
        PinballParts.MakeBumper(bumper, Vector2.Zero);
        var ball = PinballParts.Ball(new Vector2(-1.5f, 0.3f));
        scene.Add(ball);
        var body = ball.GetComponent<Body>()!;
        (body.Velocity, body.GravityScale) = (new Vector2(4, 0), 0);

        // A boss in another part of the game, hurt each time the bumper scores.
        var boss = Add(scene, "Boss", 30, 0);
        var health = boss.AddComponent<Health>();
        (health.Max, health.InvincibleTime) = (5, 0);
        var trigger = boss.AddComponent<Trigger>();
        (trigger.Message, trigger.From, trigger.Do, trigger.With) = ("Score", "Bumper", "Damage", "2");

        var input = new InputState();
        var game = new Game(Saved(scene), input);
        var bumps = game.Find("Bumper")!.GetComponent<Bumper>()!;
        for (var i = 0; i < 60 && bumps.Hits == 0; i++)
            Run(game, input, Frame);
        Run(game, input, Frame);

        Assert.Equal(1, bumps.Hits);
        Assert.Equal(3, game.Find("Boss")!.GetComponent<Health>()!.Current);
    }

    [Fact]
    public void AHotspotUsed_OpensADoor()
    {
        var scene = new Scene();
        Add(scene, "Floor", 0, 0).AddComponent<WalkArea>().Points = "-10 -5; 10 -5; 10 5; -10 5";
        Add(scene, "Player", -6, 0).AddComponent<Walker>().Speed = 6;
        var lever = Add(scene, "Lever", 6, 2).AddComponent<Hotspot>();
        (lever.Points, lever.StandAt) = ("-0.5 -0.5; 0.5 -0.5; 0.5 0.5; -0.5 0.5", new Vector2(0, -1.5f));
        (lever.UseLine, lever.UseDo, lever.UseTargets) = ("Something clanks far away.", "Open", "Vault Door");

        // A platformer's locked door, needing a key, somewhere else in the game.
        var vault = Add(scene, "Vault Door", 40, 0);
        vault.AddComponent<BoxCollider>().IsTrigger = true;
        var door = vault.AddComponent<Door>();
        (door.Target, door.Locked, door.Key) = ("Treasure Room", true, "Gold Key");

        var input = new InputState();
        var game = new Game(Saved(scene), input);
        Run(game, input, Frame);

        // The player clicks the lever: the walker walks over and pulls it.
        input.MouseWorld = new Vector2(6, 2);
        input.SetMouseButton(MouseButton.Left, true);
        Run(game, input, Frame);
        input.SetMouseButton(MouseButton.Left, false);
        Run(game, input, 4);

        var opened = game.Find("Vault Door")!.GetComponent<Door>()!;
        Assert.Equal(1, game.Find("Lever")!.GetComponent<Hotspot>()!.TimesUsed);
        Assert.False(opened.Locked);
        Assert.Null(opened.Key);
    }

    // ----- What the audit filled in: each genre names what it does, and what reads input waits for focus -----

    [Fact]
    public void AWeaponsFireAction_FiresOnlyTheOneInHand()
    {
        var scene = new Scene();
        var hero = Add(scene, "Hero", 0, 0);
        hero.AddComponent<FirstPersonMovement>();
        var pistol = hero.AddComponent<Weapon>();
        var shotgun = hero.AddComponent<Weapon>();
        (shotgun.Name, shotgun.Slot) = ("Shotgun", 2);
        var input = new InputState();
        var game = new Game(scene, input);
        Run(game, input, Frame);

        game.Do(hero, "Fire");
        Run(game, input, Frame);
        Assert.Equal((1, 0), (pistol.Shots, shotgun.Shots));
        Assert.Equal(1f, ComponentOutputs.Read(hero, "Weapon.Cooling Down"));

        // Not again until it's ready.
        game.Do(hero, "Fire");
        Run(game, input, Frame);
        Assert.Equal(1, pistol.Shots);
    }

    [Fact]
    public void AHotbarsActions_PickItsSlots()
    {
        var scene = new Scene();
        var player = Add(scene, "Player", 0, 0);
        player.AddComponent<Inventory>();
        var hotbar = player.AddComponent<Hotbar>();
        hotbar.Slots = 3;
        var input = new InputState();
        var game = new Game(scene, input);
        Run(game, input, Frame);

        game.Do(player, "Next");
        Run(game, input, Frame);
        Assert.Equal(1, hotbar.Selected);
        game.Do(player, "Previous");
        game.Do(player, "Previous");
        Run(game, input, Frame);
        Assert.Equal(2, hotbar.Selected);
        game.Do(player, "Pick", "1");
        Run(game, input, Frame);
        Assert.Equal(0, hotbar.Selected);
    }

    [Fact]
    public void AWireButtonsPressAction_FlipsIt_OrPulsesOneThatsHeld_AndStandingOnIt_NeedsTheInput()
    {
        var scene = new Scene();
        var flip = Add(scene, "Flip", 0, 0);
        flip.AddComponent<BoxCollider>().IsTrigger = true;
        var toggle = flip.AddComponent<WireButton>();
        var hold = Add(scene, "Hold", 10, 0);
        hold.AddComponent<BoxCollider>().IsTrigger = true;
        var held = hold.AddComponent<WireButton>();
        held.Toggle = false;
        var player = Add(scene, "Player", 0, 0);
        player.AddComponent<BoxCollider>();
        player.AddComponent<Body>().GravityScale = 0;
        var movement = player.AddComponent<TopDownMovement>();
        // The mouse is off the buttons: only standing on one presses it.
        var input = new InputState { MouseWorld = new Vector2(50, 50) };
        var game = new Game(scene, input);
        Run(game, input, Frame * 2);

        game.Do(flip, "Press");
        game.Do(hold, "Press");
        Run(game, input, Frame);
        Assert.True(toggle.IsOn);
        Assert.True(held.IsOn);
        Run(game, input, Frame);
        Assert.True(toggle.IsOn);
        Assert.False(held.IsOn);

        // The player stands on it: Interact presses it only while they have the input.
        movement.Controllable = false;
        input.SetKey(Key.E, true);
        Run(game, input, Frame);
        input.SetKey(Key.E, false);
        Run(game, input, Frame);
        Assert.True(toggle.IsOn);
        movement.Controllable = true;
        input.SetKey(Key.E, true);
        Run(game, input, Frame);
        input.SetKey(Key.E, false);
        Run(game, input, Frame);
        Assert.False(toggle.IsOn);
    }

    // A chart of notes in lane 1 on these beats.
    private static Table Chart(params double[] beats)
    {
        var columns = new[] { new TableColumn("Beat"), new TableColumn("Lane", ColumnType.WholeNumber) };
        var rows = beats.Select((beat, i) => ($"Note {i + 1}", (IReadOnlyList<TableValue>)[TableValue.Of(beat), TableValue.Of(1L)]));
        return new Table("Chart", columns, rows);
    }
}
