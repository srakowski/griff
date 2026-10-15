using System.Globalization;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

// Items used with no code: an inventory's number keys (or a script) use what it holds, as its Item Uses say: potions that
// heal, bombs that spawn an explosion, scrolls that send a message. Taking turns, using one is an action.
public sealed partial class Game
{
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
