using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>
/// The names a scene gives without code that resolve to nothing: an action a trigger, button or card does that what it's
/// done on can't do, an output a counter, bar or wire reads that what it reads hasn't got, and a name picked from what its
/// component lists that isn't listed. The Inspector flags them, <c>scene_tree</c> lists them, the loader reports them and a
/// game says so in Output as it starts, so a renamed action or output (see <see cref="FormerNameAttribute"/>) never leaves
/// a trigger silently doing nothing.
/// </summary>
public static class NameChecks
{
    /// <summary>
    /// Why <paramref name="component"/>'s <paramref name="property"/> names an action or output that isn't there (or a choice
    /// its component doesn't list), as the Inspector words it; null when it resolves or isn't such a name.
    /// </summary>
    public static string? Problem(Component component, PropertyDescriptor property)
    {
        if (property.ActionName is not null && ActionNames.Unmatched(component, property) is { Count: > 0 } actions)
            return ActionNames.Problem(actions);
        if (property.OutputName is not null && OutputNames.Unmatched(component, property) is { Count: > 0 } outputs)
            return OutputNames.Problem(outputs);
        return null;
    }

    /// <summary>Every such name in <paramref name="scene"/>: the component, "Trigger.Do" and why.</summary>
    public static IEnumerable<(Component Component, string Label, string Problem)> In(Scene scene, ComponentRegistry registry)
    {
        foreach (var component in scene.Entities.SelectMany(e => e.Components))
        {
            if (component is MissingComponent || !registry.IsRegistered(component.GetType()))
                continue;
            foreach (var property in registry.Get(component.GetType()).Properties)
            {
                var problem = Problem(component, property) ??
                    (property.Editor is ChoiceOfAttribute choice ? choice.Problem(component, property.GetValue(component)) : null);
                if (problem is not null)
                    yield return (component, $"{component.GetType().Name}.{property.Name}", problem);
            }
        }
    }

    /// <summary>The same, each as one line for Output or the loader: "Door #3ab Trigger.Do: ...".</summary>
    public static IEnumerable<string> Lines(Scene scene, ComponentRegistry registry) =>
        In(scene, registry).Select(p => $"{p.Component.Entity!.Name}: {p.Label}: {p.Problem}");
}
