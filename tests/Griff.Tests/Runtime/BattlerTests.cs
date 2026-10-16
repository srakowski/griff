using System.Numerics;
using Griff.Data;
using Griff.Input;
using Griff.Modules.Battles;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// Battlers: turn-based fighters that attack through armor, cast spells paid for in mana and use items, each move an
/// action of their turn taker; the computer's choices; buttons that do an action, faded while it can't be done; and
/// Table Values reading the row a menu saved.
/// </summary>
public sealed class BattlerTests
{
    private const double Frame = 1 / 60.0;
    private readonly InputState _input = new();

    private static Entity Fighter(string name, string side, string foe, float health)
    {
        var entity = new Entity(name);
        var hp = entity.AddComponent<Health>();
        (hp.Max, hp.Current, hp.InvincibleTime) = (health, health, 0);
        var battler = entity.AddComponent<Battler>();
        (battler.Foe, battler.ThinkTime, battler.AttackAction) = (foe, 0, null);
        var taker = entity.AddComponent<TurnTaker>();
        (taker.Team, taker.Actions) = (side, 1);
        return entity;
    }

    private (Game Game, Battler Hero, Battler Enemy) Duel(string computer = "Enemy")
    {
        var scene = new Scene();
        var hero = Fighter("Hero", "Hero", "Enemy", 20);
        var enemy = Fighter("Enemy", "Enemy", "Hero", 20);
        scene.Add(hero);
        scene.Add(enemy);
        var turns = new Entity("Turns");
        var order = turns.AddComponent<TurnOrder>();
        (order.Teams, order.ComputerTeams, order.Between) = ("Hero, Enemy", computer, 0);
        scene.Add(turns);
        var game = new Game(scene, _input);
        Run(game, Frame * 2);
        return (game, hero.GetComponent<Battler>()!, enemy.GetComponent<Battler>()!);
    }

    private void Run(Game game, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private static Health HealthOf(Battler battler) => battler.Entity!.GetComponent<Health>()!;

    // With no Foe named, a battler fights the first battler standing on another team.
    [Fact]
    public void WithNoFoe_ItFightsABattlerOnAnotherTeam()
    {
        var (game, hero, enemy) = Duel(computer: "");
        (hero.Foe, hero.Damage) = ("", 5);
        hero.Attack();
        Run(game, Frame * 2);
        Assert.Equal(15, HealthOf(enemy).Current);
    }

    [Fact]
    public void AnAttack_IsSoftenedByArmor_AndPassesTheTurn()
    {
        var (game, hero, enemy) = Duel(computer: "");
        (hero.Damage, enemy.Armor) = (5, 2);
        Assert.True(hero.CanAct);
        Assert.False(enemy.CanAct);

        hero.Attack();
        Run(game, Frame * 2);

        Assert.Equal(17, HealthOf(enemy).Current);
        Assert.Equal("Hero hits Enemy for 3.", hero.Said);
        Assert.Equal("Enemy", game.Find("Turns")!.GetComponent<TurnOrder>()!.Current);
        Assert.False(hero.CanAct);

        // Off its turn, an attack asked for is dropped.
        hero.Attack();
        Run(game, Frame * 2);
        Assert.Equal(17, HealthOf(enemy).Current);
    }

    [Fact]
    public void ManaAllSpent_StaysSpent_WhenTheSceneIsLoadedAgain()
    {
        var entity = Fighter("Hero", "Hero", "Enemy", 20);
        var battler = entity.GetComponent<Battler>()!;
        battler.MaxMana = 6;
        battler.Mana = 0;
        var scene = new Scene();
        scene.Add(entity);
        var serializer = new Griff.Scenes.Serialization.SceneSerializer(Griff.Scenes.Serialization.ComponentRegistry.CreateDefault());
        var copy = serializer.LoadFromString(serializer.SaveToString(scene)).Entities.Single().GetComponent<Battler>()!;
        Assert.Equal((0f, 6f), (copy.Mana, copy.MaxMana));
    }

    [Fact]
    public void ASpell_CostsMana_GoesThroughArmor_AndWithoutTheManaUsesNoTurn()
    {
        var (game, hero, enemy) = Duel(computer: "");
        (hero.MaxMana, hero.Spell, hero.SpellCost, hero.SpellDamageRoll, hero.SpellDamage) = (6, "Fireball", 5, "4", 4);
        enemy.Armor = 10;

        hero.CastSpell();
        Run(game, Frame * 2);
        Assert.Equal(16, HealthOf(enemy).Current);
        Assert.Equal(1, hero.Mana);
        Assert.Equal("Hero casts Fireball on Enemy: 4 damage.", hero.Said);

        // The enemy passes; back to the hero, who hasn't the mana.
        enemy.Attack();
        Run(game, Frame * 2);
        hero.CastSpell();
        Run(game, Frame * 2);
        Assert.Equal("Hero hasn't the mana for Fireball (5).", hero.Said);
        Assert.Equal("Hero", game.Find("Turns")!.GetComponent<TurnOrder>()!.Current);
        Assert.True(hero.CanAct);
    }

    [Fact]
    public void Items_HealAndHurt_AndRunOut()
    {
        var (game, hero, enemy) = Duel(computer: "");
        (hero.Item, hero.Items, hero.ItemHeal) = ("Potion", 1, 6);
        HealthOf(hero).Current = 10;

        hero.UseItem();
        Run(game, Frame * 2);
        Assert.Equal(16, HealthOf(hero).Current);
        Assert.Equal(0, hero.Items);
        Assert.Equal("Hero uses a Potion: +6 health.", hero.Said);
        Assert.False(((IActionReady)hero).IsReady("UseItem"));
    }

    [Fact]
    public void TheComputer_HealsWhenHurt_CastsWhileItCan_AndOtherwiseAttacks()
    {
        var (game, hero, enemy) = Duel();
        (enemy.Damage, enemy.Item, enemy.Items, enemy.ItemHeal) = (2, "Potion", 1, 5);
        (enemy.MaxMana, enemy.Spell, enemy.SpellCost, enemy.SpellDamage) = (5, "Bolt", 5, 4);
        HealthOf(enemy).Current = 5;

        hero.Attack();
        Run(game, Frame * 3);
        Assert.Equal("Enemy uses a Potion: +5 health.", enemy.Said);

        hero.Attack();
        Run(game, Frame * 3);
        Assert.Equal("Enemy casts Bolt on Hero: 4 damage.", enemy.Said);

        hero.Attack();
        Run(game, Frame * 3);
        Assert.Equal("Enemy hits Hero for 2.", enemy.Said);
    }

    [Fact]
    public void ADoButton_DoesTheAction_AndFadesWhileItCant()
    {
        var (game, hero, enemy) = Duel(computer: "");
        hero.Spell = "Fireball";
        (hero.SpellCost, hero.SpellDamage) = (5, 3);
        var spellButton = new Entity("Cast");
        var button = spellButton.AddComponent<Button>();
        (button.Action, button.Do, button.Targets, button.Shortcut) = (ButtonAction.Do, "Cast Spell", "Hero", Key.D2);
        var attackButton = new Entity("Hit");
        var hit = attackButton.AddComponent<Button>();
        (hit.Action, hit.Do, hit.Targets, hit.Shortcut) = (ButtonAction.Do, "Battler.Attack", "Hero", Key.D1);
        game.Scene.Add(spellButton);
        game.Scene.Add(attackButton);
        Run(game, Frame * 2);

        Assert.False(button.Interactable, "no mana for the spell");
        Assert.True(hit.Interactable);
        _input.SetKey(Key.D1, true);
        Run(game, Frame);
        _input.SetKey(Key.D1, false);
        Run(game, Frame * 2);

        Assert.Equal(18, HealthOf(enemy).Current); // its Damage, 2
        Assert.False(hit.Interactable, "faded on the enemy's turn");
    }

    [Fact]
    public void TableValues_ReadTheRowAMenuSaved_OrTheirOwnWithoutOne()
    {
        var table = TableFile.Read("""
            { "columns": [ { "name": "Health", "type": "number" } ],
              "rows": { "Knight": { "Health": 30 }, "Mage": { "Health": 12 } } }
            """, "Classes");

        int HealthWith(string? saved)
        {
            var scene = new Scene();
            var hero = new Entity("Hero");
            hero.AddComponent<Health>();
            var values = hero.AddComponent<TableValues>();
            (values.Table, values.Row, values.SavedRow, values.Sets) = ("Classes", "Knight", "hero", ["Health.Max = Health"]);
            scene.Add(hero);
            var game = new Game(scene, _input) { Tables = new TableSet([table]) };
            if (saved is not null)
                game.SaveData.Set("hero", saved);
            game.Tick(Frame);
            return (int)hero.GetComponent<Health>()!.Max;
        }

        Assert.Equal(12, HealthWith("Mage"));
        Assert.Equal(30, HealthWith(null));
        Assert.Equal(30, HealthWith("Bard")); // a row the table hasn't got: its own
    }
}
