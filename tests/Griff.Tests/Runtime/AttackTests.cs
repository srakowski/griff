using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Attacks: a hitbox swung out on a button press (or by an enemy in reach), hurting each thing once per swing.</summary>
public class AttackTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public AttackTests()
    {
        _game = new Game(_scene, _input);
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
    }

    private Entity AddBox(string name, Vector2 position, string layer, Entity? parent = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        _scene.Add(entity, parent);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Layer = layer;
        return entity;
    }

    // A player with a sword: a hitbox one unit to its right that hurts enemies.
    private (Entity Player, Attack Attack, Entity Hitbox) AddSwordsman(Vector2 position)
    {
        var player = AddBox("Player", position, "Player");
        player.AddComponent<Body>();
        player.AddComponent<Sprite>();
        player.AddComponent<Health>().Max = 3;
        var attack = player.AddComponent<Attack>();
        attack.HitStop = 0;
        var hitbox = AddBox("Hitbox", new Vector2(1, 0), "Attack", player);
        hitbox.GetComponent<BoxCollider>()!.IsTrigger = true;
        hitbox.GetComponent<BoxCollider>()!.Size = new Vector2(1.2f, 0.6f);
        hitbox.AddComponent<DamageOnTouch>().Layers = "Enemy";
        return (player, attack, hitbox);
    }

    private Health AddEnemy(Vector2 position, int max = 5)
    {
        var enemy = AddBox("Slime", position, "Enemy");
        var health = enemy.AddComponent<Health>();
        health.Max = max;
        health.InvincibleTime = 0;
        return health;
    }

    [Fact]
    public void Hitbox_IsHidden_UntilTheAttackButtonIsPressed()
    {
        var (_, attack, hitbox) = AddSwordsman(Vector2.Zero);
        Run(0.1f);
        Assert.False(hitbox.IsEnabled);

        Press(Key.X);
        Assert.True(attack.IsAttacking);
        Run(0.06f);
        Assert.True(hitbox.IsEnabled);
        Assert.True(attack.IsHitting);

        Run(0.2f);
        Assert.False(hitbox.IsEnabled);
        Assert.False(attack.IsAttacking);
    }

    [Fact]
    public void ASwing_HurtsWhatItTouchesOnce_HoweverLongItLingers()
    {
        var (_, attack, _) = AddSwordsman(Vector2.Zero);
        attack.ActiveTime = 0.5f;
        var slime = AddEnemy(new Vector2(1.2f, 0));
        var hits = new List<Entity>();
        attack.Hit += hits.Add;

        Press(Key.X);
        Run(0.6f);

        Assert.Equal(4, slime.Current);
        Assert.Equal([slime.Entity!], hits);

        Run(0.3f);
        Press(Key.X);
        Run(0.3f);
        Assert.Equal(3, slime.Current);
    }

    [Fact]
    public void ASwing_NeverHurtsItsOwner()
    {
        var (player, _, _) = AddSwordsman(Vector2.Zero);
        player.GetComponent<BoxCollider>()!.Layer = "Enemy";

        Press(Key.X);
        Run(0.3f);

        Assert.Equal(3, player.GetComponent<Health>()!.Current);
    }

    [Fact]
    public void Hitbox_SwingsTheWayThePlayerFaces()
    {
        var (player, attack, hitbox) = AddSwordsman(Vector2.Zero);
        player.AddComponent<PlatformerMovement>().Facing = -1;
        var behind = AddEnemy(new Vector2(-1.2f, 0));
        var ahead = AddEnemy(new Vector2(1.2f, 0));

        Press(Key.X);
        Run(0.1f);

        Assert.Equal(-1, hitbox.Transform.Position.X, 3);
        Assert.Equal(4, behind.Current);
        Assert.Equal(5, ahead.Current);
    }

    [Fact]
    public void TopDown_HitboxTurnsUpAndDown()
    {
        var (player, _, hitbox) = AddSwordsman(Vector2.Zero);
        player.AddComponent<TopDownMovement>().Facing = Vector2.UnitY;
        var above = AddEnemy(new Vector2(0, 1.2f));

        Press(Key.X);
        Run(0.1f);

        Assert.Equal(new Vector3(0, 1, 0), hitbox.Transform.Position);
        Assert.Equal(new Vector2(0.6f, 1.2f), hitbox.GetComponent<BoxCollider>()!.Size);
        Assert.Equal(4, above.Current);
    }

    [Fact]
    public void Cooldown_WaitsBeforeTheNextSwing_ButRemembersThePress()
    {
        var (_, attack, _) = AddSwordsman(Vector2.Zero);
        attack.Cooldown = 0.5f;
        var swings = 0;
        attack.Swung += () => swings++;

        Press(Key.X);
        Run(0.3f);
        Assert.Equal(1, swings);
        Press(Key.X);
        Run(0.1f);
        Assert.False(attack.IsAttacking);

        Run(0.5f);
        Assert.Equal(2, swings);
    }

    [Fact]
    public void Enemy_SwingsAtItsTargetOnceInRange_FacingIt()
    {
        var player = AddBox("Player", new Vector2(-5, 0), "Player");
        var health = player.AddComponent<Health>();
        health.Max = 3;
        var knight = AddBox("Knight", Vector2.Zero, "Enemy");
        knight.AddComponent<Sprite>();
        var attack = knight.AddComponent<Attack>();
        attack.Action = null;
        attack.Range = 2;
        attack.HitStop = 0;
        var hitbox = AddBox("Hitbox", new Vector2(1, 0), "EnemyAttack", knight);
        hitbox.GetComponent<BoxCollider>()!.IsTrigger = true;
        hitbox.AddComponent<DamageOnTouch>().Layers = "Player";

        Run(0.3f);
        Assert.False(attack.IsAttacking);
        Assert.Equal(3, health.Current);

        player.Transform.Position = new Vector3(-1.5f, 0, 0);
        Run(0.15f);

        Assert.True(knight.GetComponent<Sprite>()!.FlipX);
        Assert.Equal(2, health.Current);
    }

    [Fact]
    public void Lunge_PushesTheAttackerForward_AndHitStopFreezesOnAHit()
    {
        var (player, attack, _) = AddSwordsman(Vector2.Zero);
        attack.Lunge = 6;
        attack.HitStop = 0.1f;
        AddEnemy(new Vector2(1.5f, 0));

        Press(Key.X);
        Run(0.06f);

        Assert.True(player.GetComponent<Body>()!.Velocity.X > 5);
        Run(Step * 2);
        Assert.True(_game.Time.IsFrozen);
    }

    [Fact]
    public void Swing_FromAScript_AndTheAttackClipPlays()
    {
        var (player, attack, _) = AddSwordsman(Vector2.Zero);
        var movement = player.AddComponent<TopDownMovement>();
        var animation = player.AddComponent<Animation>();
        animation.Clips = "Idle 0, Attack 1-3";
        Run(0.1f);

        Assert.True(attack.Swing());
        Assert.False(attack.Swing());
        Run(Step * 3);

        Assert.Equal("Attack", animation.Clip);
        Run(0.6f);
        Assert.Equal("Idle", animation.Clip);
        Assert.NotNull(movement);
    }

    [Fact]
    public void Attack_IsSavedWithTheScene()
    {
        var (player, attack, _) = AddSwordsman(Vector2.Zero);
        (attack.Windup, attack.ActiveTime, attack.Cooldown, attack.Lunge, attack.Sound) = (0.2f, 0.3f, 0.4f, 5, "Sounds/swing.wav");
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var copy = serializer.LoadFromString(serializer.SaveToString(_scene)).Entities.Single(e => e.Name == "Player").GetComponent<Attack>()!;

        Assert.Equal((0.2f, 0.3f, 0.4f, 5f, "Sounds/swing.wav", "Hitbox", "Attack"),
            (copy.Windup, copy.ActiveTime, copy.Cooldown, copy.Lunge, copy.Sound, copy.Hitbox, copy.Action));
        Assert.NotNull(player);
    }
}
