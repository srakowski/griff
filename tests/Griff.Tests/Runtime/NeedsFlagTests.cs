using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Flags outside dialogue: switches, doors, spawners and panels that need one, and triggers that set it.</summary>
public sealed class NeedsFlagTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public NeedsFlagTests()
    {
        _game = new Game(_scene, _input)
        {
            PrefabLoader = name => new Entity(name),
            SceneLoader = _ => new Scene(),
            SceneName = "Level",
        };
        _game.Physics.Gravity = Vector2.Zero;
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
        {
            _game.Tick(Step);
            _input.EndFrame();
        }
    }

    private void Press(Key key)
    {
        _input.SetKey(key, true);
        _game.Tick(Step);
        _input.EndFrame();
        _input.SetKey(key, false);
        _game.Tick(Step);
        _input.EndFrame();
    }

    private Entity Add(string name, Vector2 position = default, Entity? parent = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        _scene.Add(entity, parent);
        return entity;
    }

    private Entity AddBox(string name, Vector2 position, Vector2 size, bool trigger = false)
    {
        var entity = Add(name, position);
        var collider = entity.AddComponent<BoxCollider>();
        (collider.Size, collider.IsTrigger) = (size, trigger);
        return entity;
    }

    private Entity AddPlayer(Vector2 position)
    {
        var player = AddBox("Player", position, new Vector2(0.8f, 1));
        player.GetComponent<BoxCollider>()!.Layer = "Player";
        player.AddComponent<Body>();
        return player;
    }

    [Fact]
    public void FlagHolds_ForASetFlag_OrNotOne_WhateverItsCapitals()
    {
        Assert.True(_game.FlagHolds(null));
        Assert.True(_game.FlagHolds("  "));
        Assert.False(_game.FlagHolds("PowerOn"));
        Assert.True(_game.FlagHolds("not PowerOn"));
        Assert.True(_game.FlagHolds("!PowerOn"));

        _game.SetFlag("PowerOn");

        Assert.True(_game.FlagHolds(" poweron "));
        Assert.False(_game.FlagHolds("NOT PowerOn"));
    }

    [Fact]
    public void AButton_ThatNeedsAFlag_DoesNothingUntilATriggerSetsIt()
    {
        var button = AddBox("Button", Vector2.Zero, Vector2.One, trigger: true).AddComponent<Switch>();
        (button.Mode, button.NeedsFlag) = (SwitchMode.Button, "PowerOn");
        var player = AddPlayer(Vector2.Zero);
        var rules = Add("Rules").AddComponent<Trigger>();
        (rules.Message, rules.Do, rules.With) = ("Generator", "Set Flag", "PowerOn");
        Run(0.2f);
        Assert.False(button.IsOn);

        _game.Send("Generator");
        Run(0.2f);

        Assert.True(_game.HasFlag("PowerOn"));
        Assert.True(button.IsOn);
        Assert.NotNull(player);
    }

    [Fact]
    public void ALever_ThatNeedsAFlag_HidesItsPrompt_AndIgnoresInteract()
    {
        var lever = AddBox("Lever", Vector2.Zero, Vector2.One, trigger: true).AddComponent<Switch>();
        (lever.Mode, lever.NeedsFlag, lever.Prompt) = (SwitchMode.Lever, "not Jammed", "Hint");
        var hint = Add("Hint");
        AddPlayer(Vector2.Zero);
        _game.SetFlag("Jammed");
        Run(0.1f);

        Press(Key.E);
        Assert.False(lever.IsOn);
        Assert.False(hint.IsEnabled);

        _game.SetFlag("Jammed", on: false);
        Run(0.1f);
        Press(Key.E);
        Assert.True(lever.IsOn);
    }

    [Fact]
    public void ADoor_ThatNeedsAFlag_StaysShutUntilItsSet()
    {
        var door = AddBox("Door", Vector2.Zero, new Vector2(1, 2), trigger: true).AddComponent<Door>();
        (door.Target, door.Fade, door.NeedsFlag) = ("Far", 0, "MetKing");
        Add("Far", new Vector2(20, 0));
        var player = AddPlayer(Vector2.Zero);
        Run(0.2f);
        Assert.Equal(0, player.Transform.WorldPosition.X, 0.01f);

        _game.SetFlag("MetKing");
        Run(0.2f);

        Assert.Equal(20, player.Transform.WorldPosition.X, 0.5f);
    }

    [Fact]
    public void ASpawner_ThatNeedsAFlag_WaitsForIt()
    {
        var spawner = Add("Spawner").AddComponent<Spawner>();
        (spawner.Prefab, spawner.Interval, spawner.NeedsFlag) = ("Slime", 0.1f, "Alarm");
        Run(1);
        Assert.Equal(0, spawner.Spawned);

        _game.SetFlag("Alarm");
        Run(0.5f);

        Assert.True(spawner.Spawned > 0);
    }

    [Fact]
    public void APanel_ThatNeedsAFlag_OpensOnItsMessageAndKey_OnlyOnceItsSet()
    {
        var map = Add("Map").AddComponent<Panel>();
        (map.OpenMessage, map.OpenOn, map.NeedsFlag, map.PauseGame) = ("ShowMap", "Pause", "FoundMap", false);
        Run(0.1f);
        Assert.False(map.Entity!.IsEnabled);

        _game.Send("ShowMap");
        Press(Key.Escape);
        Run(0.1f);
        Assert.False(map.Entity.IsEnabled);

        _game.SetFlag("FoundMap");
        _game.Send("ShowMap");
        Run(0.1f);
        Assert.True(map.Entity.IsEnabled);
    }
}
