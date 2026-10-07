using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class HealthTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public HealthTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
            _game.Tick(Step);
    }

    private Entity AddHero(Vector2 position, float max = 3, string? layer = "Player")
    {
        var hero = new Entity("Hero");
        hero.Transform.Position = new Vector3(position, 0);
        hero.AddComponent<Sprite>();
        hero.AddComponent<BoxCollider>().Layer = layer;
        hero.AddComponent<Body>().GravityScale = 0;
        hero.AddComponent<Health>().Max = max;
        _scene.Add(hero);
        return hero;
    }

    private Entity AddSpikes(Vector2 position, bool trigger = true, string? layers = null)
    {
        var spikes = new Entity("Spikes");
        spikes.Transform.Position = new Vector3(position, 0);
        spikes.AddComponent<BoxCollider>().IsTrigger = trigger;
        spikes.AddComponent<DamageOnTouch>().Layers = layers;
        _scene.Add(spikes);
        return spikes;
    }

    [Fact]
    public void ChangingMax_AtFullHealth_KeepsItFull()
    {
        var health = new Health { Max = 5 };

        Assert.Equal(5, health.Current);

        health.Current = 2;
        health.Max = 10;
        Assert.Equal(2, health.Current);
    }

    [Fact]
    public void Damage_TakesHitPoints_ThenIgnoresHitsWhileInvincible()
    {
        var health = AddHero(Vector2.Zero).GetComponent<Health>()!;
        health.InvincibleTime = 0.5f;
        _game.Tick(Step);

        Assert.True(health.Damage(1));
        Assert.Equal(2, health.Current);
        Assert.True(health.IsInvincible);
        Assert.False(health.Damage(1));
        Assert.Equal(2, health.Current);

        Run(0.6f);
        Assert.False(health.IsInvincible);
        Assert.True(health.Damage(1));
        Assert.Equal(1, health.Current);
    }

    [Fact]
    public void Heal_NeverGoesPastMax_AndReturnsWhatItGave()
    {
        var health = new Health { Max = 3, InvincibleTime = 0 };
        health.Damage(2);

        Assert.Equal(2, health.Heal(5));
        Assert.Equal(3, health.Current);
    }

    [Fact]
    public void Death_TellsBehaviors_SendsTheMessage_AndDestroysTheEntity()
    {
        var hero = AddHero(Vector2.Zero, max: 1);
        var listener = hero.AddComponent<Listener>();
        hero.GetComponent<Health>()!.DeathMessage = "HeroDied";
        object? heard = null;
        _game.On("HeroDied", data => heard = data);
        _game.Tick(Step);

        hero.GetComponent<Health>()!.Damage(1, source: hero);
        Assert.Equal(["damage 1 from Hero", "death"], listener.Log);

        _game.Tick(Step);
        Assert.Same(hero, heard);
        Assert.Null(hero.Scene);
    }

    [Fact]
    public void Death_WithoutDestroyOnDeath_LeavesTheEntity_AndHappensOnce()
    {
        var hero = AddHero(Vector2.Zero, max: 1);
        var health = hero.GetComponent<Health>()!;
        health.DestroyOnDeath = false;
        var deaths = 0;
        health.Died += () => deaths++;

        health.Kill();
        health.Kill();
        Assert.False(health.Damage(1));
        Run(0.1f);

        Assert.Equal(1, deaths);
        Assert.Same(_scene, hero.Scene);
        Assert.True(health.IsDead);
    }

    [Fact]
    public void Hit_FlashesAndBlinksTheSprites_IncludingChildren()
    {
        var hero = AddHero(Vector2.Zero);
        var arm = new Entity("Arm");
        var armSprite = arm.AddComponent<Sprite>();
        _scene.Add(arm, hero);
        hero.GetComponent<Health>()!.InvincibleTime = 1;
        _game.Tick(Step);

        hero.GetComponent<Health>()!.Damage(1);

        Assert.Equal(1, armSprite.FlashOverlay.A, 3);
        Assert.True(armSprite.IsBlinkedOut);
        var draws = SceneRenderer.Build(_scene, 1).Sprites;
        Assert.DoesNotContain(draws, d => d.Entity == arm);

        // The flash fades out quickly; blinking goes on while the hero can't be hurt, then stops.
        Run(0.2f);
        Assert.Equal(0, armSprite.FlashOverlay.A);
        var shown = 0;
        for (var i = 0; i < 30; i++)
        {
            _game.Tick(Step);
            shown += armSprite.IsBlinkedOut ? 0 : 1;
        }

        Assert.InRange(shown, 5, 25);
        Run(1);
        Assert.False(armSprite.IsBlinkedOut);
        Assert.Contains(SceneRenderer.Build(_scene, 1).Sprites, d => d.Entity == arm);
    }

    [Fact]
    public void Flash_IsDrawnOverTheSprite()
    {
        var hero = AddHero(Vector2.Zero);
        _game.Tick(Step);

        _game.Flash(hero, Color.Red, 0.5f);

        var draw = SceneRenderer.Build(_scene, 1).Sprites.Single(d => d.Entity == hero);
        Assert.Equal(Color.Red, draw.Flash);
        Assert.Equal(Color.White, draw.Tint);
    }

    [Fact]
    public void Spikes_HurtWhatStandsInThem_AgainAfterInvincibilityWearsOff()
    {
        var hero = AddHero(Vector2.Zero);
        var health = hero.GetComponent<Health>()!;
        health.InvincibleTime = 0.5f;
        AddSpikes(Vector2.Zero);

        Run(0.1f);
        Assert.Equal(2, health.Current);

        Run(0.5f);
        Assert.Equal(1, health.Current);
    }

    [Fact]
    public void SolidHazards_HurtOnContact()
    {
        var hero = AddHero(new Vector2(0, 2));
        hero.GetComponent<Body>()!.GravityScale = 1;
        hero.GetComponent<Health>()!.InvincibleTime = 5;
        AddSpikes(new Vector2(0, 0), trigger: false);

        Run(1);

        Assert.Equal(2, hero.GetComponent<Health>()!.Current);
        Assert.True(hero.GetComponent<Body>()!.IsGrounded);
    }

    [Fact]
    public void Layers_LimitWhoGetsHurt()
    {
        var hero = AddHero(Vector2.Zero, layer: "Player");
        AddSpikes(Vector2.Zero, layers: "Enemy");

        Run(0.2f);

        Assert.Equal(3, hero.GetComponent<Health>()!.Current);
    }

    [Fact]
    public void DestroyOnHit_SpendsABullet_AndKnockbackPushesTheTarget()
    {
        var hero = AddHero(Vector2.Zero);
        var bullet = AddSpikes(new Vector2(-0.5f, 0));
        bullet.GetComponent<DamageOnTouch>()!.DestroyOnHit = true;
        bullet.GetComponent<DamageOnTouch>()!.Knockback = 6;

        _game.Tick(Step);

        Assert.Null(bullet.Scene);
        Assert.Equal(2, hero.GetComponent<Health>()!.Current);
        Assert.True(hero.GetComponent<Body>()!.Velocity.X > 5);
    }

    [Fact]
    public void AHitboxChild_HurtsItsOwnersHealth_ButNeverItsOwner()
    {
        var hero = AddHero(new Vector2(5, 0));
        var sword = new Entity("Sword");
        sword.AddComponent<BoxCollider>().IsTrigger = true;
        sword.AddComponent<DamageOnTouch>();
        _scene.Add(sword, hero);

        var dummy = new Entity("Dummy");
        dummy.Transform.Position = new Vector3(5.5f, 0, 0);
        var hurtbox = new Entity("Hurtbox");
        hurtbox.AddComponent<BoxCollider>();
        dummy.AddComponent<Health>();
        _scene.Add(dummy);
        _scene.Add(hurtbox, dummy);

        Run(0.1f);

        Assert.Equal(3, hero.GetComponent<Health>()!.Current);
        Assert.Equal(2, dummy.GetComponent<Health>()!.Current);
    }

    [Fact]
    public void HitStop_FreezesGameTime_ForRealSeconds()
    {
        var mover = AddHero(Vector2.Zero);
        mover.GetComponent<Body>()!.Velocity = new Vector2(1, 0);
        _game.Tick(Step);
        var before = mover.Transform.Position.X;

        _game.HitStop(0.1f);
        Run(0.1f);
        Assert.Equal(before, mover.Transform.Position.X);
        Assert.Equal(0, _game.Time.Delta);

        Run(0.1f);
        Assert.True(mover.Transform.Position.X > before);
        Assert.False(_game.Time.IsFrozen);
        Assert.Equal(1, _game.Time.Scale);
    }

    public class Listener : Behavior
    {
        public List<string> Log { get; } = [];

        protected override void OnDamage(float amount, Entity? source) => Log.Add($"damage {amount} from {source?.Name}");

        protected override void OnDeath() => Log.Add("death");
    }
}
