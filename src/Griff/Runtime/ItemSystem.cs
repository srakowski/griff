using System.Globalization;
using System.Numerics;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Items with no code: pickups collected, and an inventory's number keys (or a script) using what it holds, as its Item
/// Uses say: potions that heal, bombs that spawn an explosion, scrolls that send a message. Taking turns, using one is an
/// action. And items given, counted and paid for the way every genre does (see Items): from what holds them on an entity,
/// or its team's.
/// </summary>
internal sealed class ItemSystem : GameSystem
{
    public override string Name => "Items";

    public int CountItems(Entity? entity, string item) => Items.Count(entity, item.Trim(), Game.TeamOf(entity));

    public bool CanPay(Entity? entity, string? cost) => Items.CanPay(entity, cost, Game.TeamOf(entity));

    public bool Pay(Entity? entity, string? cost) => Items.Pay(entity, cost, Game.TeamOf(entity));

    public void Give(Entity entity, string item, int count = 1)
    {
        item = item.Trim();
        if (item.Length == 0 || count == 0)
            return;
        var holder = Items.HolderOf(entity, item, Game.TeamOf(entity)) ?? entity.AddComponent<Inventory>();
        if (count > 0)
            holder.Add(item, count);
        else
            holder.Take(item, -count);
    }

    internal void RunItemUses()
    {
        foreach (var inventory in Game.Active<Inventory>())
        {
            var entity = inventory.Entity!;
            // Only copied when there's something to use, so an inventory that's just carried makes no garbage.
            List<string>? asked = inventory.UseRequests.Count == 0 ? null : [.. inventory.UseRequests];
            inventory.UseRequests.Clear();
            if (Game.Waiting(entity))
                continue;
            if (inventory.NumberKeys)
            {
                var input = Game.InputOf(entity);
                IReadOnlyList<string>? usable = null;
                for (var i = 0; i < 9; i++)
                {
                    if (!input.WasPressed((Key)((int)Key.D1 + i)))
                        continue;
                    usable ??= inventory.Usable;
                    if (i < usable.Count)
                        (asked ??= []).Add(usable[i]);
                }
            }

            if (asked is null)
                continue;
            foreach (var item in asked)
                UseItem(inventory, item);
        }
    }

    // Uses one up, unless there's none left or all it would do is heal someone who isn't hurt.
    private void UseItem(Inventory inventory, string item)
    {
        var entity = inventory.Entity!;
        var effects = inventory.EffectsOf(item);
        var health = Health.Of(entity);
        var hurt = health is { IsDead: false } && health.Current < health.Max;
        if (effects.Count == 0 || !inventory.Has(item) || Game.Waiting(entity) || (!hurt && effects.All(e => e.Verb == "heal")))
            return;

        inventory.Take(item);
        var at = Game.Flat(entity.Transform.WorldPosition);
        foreach (var (verb, argument) in effects)
        {
            switch (verb)
            {
                case "heal" when health is not null && float.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount):
                    health.Heal(amount);
                    break;
                case "spawn":
                    Game.TrySpawn(argument, inventory, $"its {item} item's spawn effect", at);
                    break;
                case "send":
                    Game.Send(argument, entity, from: entity);
                    break;
            }
        }

        Game.PlayIfSet(inventory.UseSound);
        Game.UseAction(entity);
        inventory.RaiseUsed(item.Trim());
    }

    internal void RunPickupSpin()
    {
        foreach (var pickup in Game.Moving<Pickup>())
        {
            if (pickup.Spin != 0)
                pickup.Entity!.Transform.Rotation += new Vector3(0, 0, pickup.Spin * Time.Delta);
        }
    }

    // Touches from this frame's physics; each pickup goes to the first thing that reaches it.
    internal void RunPickups()
    {
        if (Game.Touches.Count == 0 || Game.Active<Pickup>().Count == 0)
            return;
        foreach (var (source, target) in Game.Touches.ToList())
        {
            // Nothing paused picks up or is picked up (see Entity.IsPaused).
            if (source.Scene != Game.Scene || target.Scene != Game.Scene || !Game.Runs(source) || source.GetComponent<Pickup>() is not { IsCollected: false } pickup ||
                source.IsPaused || target.IsPaused)
                continue;
            if (Game.BodyOwner(target) is not { } collector || !pickup.AcceptsLayer(Game.LayerOf(target)) || source.IsSelfOrDescendantOf(collector))
                continue;
            if (pickup.Heal > 0 && string.IsNullOrWhiteSpace(pickup.Item) && Health.Of(collector) is { } health && health.Current >= health.Max)
                continue;
            if (!string.IsNullOrWhiteSpace(pickup.Price) && !Pay(collector, pickup.Price))
                continue;
            Collect(pickup, collector);
        }
    }

    internal void Collect(Pickup pickup, Entity collector)
    {
        pickup.IsCollected = true;
        var entity = pickup.Entity!;
        var position = entity.Transform.WorldPosition;
        if (!string.IsNullOrWhiteSpace(pickup.Sound))
            Game.PlayShared(pickup.Sound);

        if (!string.IsNullOrWhiteSpace(pickup.Effect))
        {
            var effect = Game.TrySpawn(pickup.Effect, pickup, "its Effect", new Vector2(position.X, position.Y));
            if (effect is not null && pickup.TintEffect && entity.GetComponent<Sprite>() is { } sprite)
            {
                foreach (var emitter in effect.SelfAndDescendants().SelectMany(e => e.GetComponents<ParticleEmitter>()))
                {
                    emitter.StartColor = sprite.Tint;
                    emitter.EndColor = sprite.Tint with { A = 0 };
                }
            }
        }

        GiveItem(pickup, collector);
        if (pickup.Heal > 0)
            Health.Of(collector)?.Heal(pickup.Heal);
        Game.Notify(collector, behavior => behavior.RunPickup(entity));
        pickup.RaiseCollected(collector);
        if (!string.IsNullOrWhiteSpace(pickup.Message))
            Game.Send(pickup.Message.Trim(), pickup.Amount, from: pickup.Entity);
        Game.Destroy(entity);
    }

    // The collector carries the pickup's item from now on: in its items or its team's, or an inventory of its own if it
    // had none.
    internal void GiveItem(Pickup pickup, Entity collector)
    {
        if (string.IsNullOrWhiteSpace(pickup.Item) || pickup.Amount == 0)
            return;
        Game.Give(collector, pickup.Item.Trim(), pickup.Amount);
    }

    // The first inventory in the scene (in the same part of it as `near`: a minigame being played, or outside them), the
    // player's in practice, even while they're away respawning, or else the first thing holding that item (a team's
    // faction, a card hand's mana; see Items). In a network game, this machine's player's own.
    internal int ItemCount(string item, Entity? near = null)
    {
        item = item.Trim();
        var inventories = Game.Scene.AllNow<Inventory>();
        var inventory = Game.Network.IsOnline ? inventories.FirstOrDefault(i => Game.Network.IsMine(i.Entity) && Networked.OwnerIdOf(i.Entity) != 0) : null;
        if (inventory is null)
        {
            foreach (var each in inventories)
            {
                if (Scene.SamePart(each.Entity!, near))
                {
                    inventory = each;
                    break;
                }
            }
        }

        inventory ??= inventories.FirstOrDefault();
        if (inventory is not null)
            return inventory.Count(item);
        foreach (var holder in Game.Scene.AllNow<IHoldsItems>())
        {
            if (holder.Keeps(item))
                return holder.Count(item);
        }

        return 0;
    }
}
