using System.Globalization;
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
            var asked = inventory.UseRequests.ToList();
            inventory.UseRequests.Clear();
            if (Waiting(entity))
                continue;
            if (inventory.NumberKeys)
            {
                var input = InputOf(entity);
                var usable = inventory.Usable;
                for (var i = 0; i < Math.Min(9, usable.Count); i++)
                {
                    if (input.WasPressed((Key)((int)Key.D1 + i)))
                        asked.Add(usable[i]);
                }
            }

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
                    Send(argument, entity);
                    break;
            }
        }

        PlayIfSet(inventory.UseSound);
        UseAction(entity);
        inventory.RaiseUsed(item.Trim());
    }
}
