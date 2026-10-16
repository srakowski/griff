using System.Globalization;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

// Items used with no code: an inventory's number keys (or a script) use what it holds, as its Item Uses say: potions that
// heal, bombs that spawn an explosion, scrolls that send a message. Taking turns, using one is an action. And items given,
// counted and paid for the way every genre does (see Items): from what holds them on an entity, or its team's.
public sealed partial class Game
{
    /// <summary>
    /// How many of <paramref name="item"/> <paramref name="entity"/> has to pay with: in what holds items on it or the
    /// entity it's inside (an <see cref="Inventory"/>, a card hand's mana), else its team's (see <see cref="Items"/>).
    /// </summary>
    public int CountItems(Entity? entity, string item) => Items.Count(entity, item.Trim(), TeamOf(entity));

    /// <summary>True if <paramref name="entity"/> can pay <paramref name="cost"/>, e.g. "150 Gold, 50 Wood" (see <see cref="CountItems"/>).</summary>
    public bool CanPay(Entity? entity, string? cost) => Items.CanPay(entity, cost, TeamOf(entity));

    /// <summary>Pays <paramref name="cost"/> from <paramref name="entity"/>'s items (or its team's) if it can; returns whether it did.</summary>
    public bool Pay(Entity? entity, string? cost) => Items.Pay(entity, cost, TeamOf(entity));

    /// <summary>
    /// Gives <paramref name="entity"/> <paramref name="count"/> of <paramref name="item"/> (takes them, for less than 0): into
    /// what holds that item for it or its team, or an <see cref="Inventory"/> added to it if nothing does.
    /// </summary>
    public void Give(Entity entity, string item, int count = 1)
    {
        item = item.Trim();
        if (item.Length == 0 || count == 0)
            return;
        var holder = Items.HolderOf(entity, item, TeamOf(entity)) ?? entity.AddComponent<Inventory>();
        if (count > 0)
            holder.Add(item, count);
        else
            holder.Take(item, -count);
    }

    private void RunItemUses()
    {
        foreach (var inventory in Active<Inventory>())
        {
            var entity = inventory.Entity!;
            if (Runs(entity))
                KeepSaved(inventory);
            // Only copied when there's something to use, so an inventory that's just carried makes no garbage.
            List<string>? asked = inventory.UseRequests.Count == 0 ? null : [.. inventory.UseRequests];
            inventory.UseRequests.Clear();
            if (Waiting(entity))
                continue;
            if (inventory.NumberKeys)
            {
                var input = InputOf(entity);
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

    // An inventory with a Save As starts with what was saved (once: not again after a rewind), and saves what it holds
    // whenever that changes, its rows one per line.
    private void KeepSaved(Inventory inventory)
    {
        var key = inventory.SaveAs?.Trim();
        if (!inventory.Loaded)
        {
            inventory.Loaded = true;
            if (!string.IsNullOrEmpty(key) && SaveData.GetString(key) is { } saved)
                inventory.Items = saved.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            inventory.SavedVersion = inventory.Version;
            return;
        }

        if (string.IsNullOrEmpty(key) || inventory.SavedVersion == inventory.Version)
            return;
        inventory.SavedVersion = inventory.Version;
        SaveData.Set(key, string.Join('\n', inventory.Items));
    }

    // Uses one up, unless there's none left or all it would do is heal someone who isn't hurt.
    private void UseItem(Inventory inventory, string item)
    {
        var entity = inventory.Entity!;
        var effects = inventory.EffectsOf(item);
        var health = Health.Of(entity);
        var hurt = health is { IsDead: false } && health.Current < health.Max;
        if (effects.Count == 0 || !inventory.Has(item) || Waiting(entity) || (!hurt && effects.All(e => e.Verb == "heal")))
            return;

        inventory.Take(item);
        var at = Flat(entity.Transform.WorldPosition);
        foreach (var (verb, argument) in effects)
        {
            switch (verb)
            {
                case "heal" when health is not null && float.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount):
                    health.Heal(amount);
                    break;
                case "spawn":
                    Spawn(argument, at);
                    break;
                case "send":
                    Send(argument, entity, from: entity);
                    break;
            }
        }

        PlayIfSet(inventory.UseSound);
        UseAction(entity);
        inventory.RaiseUsed(item.Trim());
    }
}
