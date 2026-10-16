using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

public static class CopyValues
{
    /// <summary>
    /// Edits that make <paramref name="target"/>'s components match <paramref name="source"/>'s values
    /// (name, enabled state and every saved property). Components are matched by position and type;
    /// components only one side has are left alone. Used to keep changes made while playing.
    /// </summary>
    public static IReadOnlyList<IEdit> Between(Entity source, Entity target, ComponentRegistry registry)
    {
        var edits = new List<IEdit>();
        if (source.Name != target.Name)
            edits.Add(new RenameEntityEdit(target, source.Name));
        if (source.IsEnabled != target.IsEnabled)
            edits.Add(new SetEntityEnabledEdit(target, source.IsEnabled));

        var count = Math.Min(source.Components.Count, target.Components.Count);
        for (var i = 0; i < count; i++)
        {
            var from = source.Components[i];
            var to = target.Components[i];
            if (from.GetType() != to.GetType() || !registry.IsRegistered(from.GetType()))
                continue;

            foreach (var property in registry.Get(from.GetType()).Properties)
            {
                var value = property.GetValue(from);
                if (!ValueCodec.AreEqual(value, property.GetValue(to)))
                    edits.Add(new SetPropertyEdit(to, property, value));
            }
        }

        return edits;
    }
}
