using Griff.Editor.Core.Commands;
using Griff.Editor.Themes;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// A command on a component's menu in the Inspector (right-clicking its header), run on the component it was opened on: the
/// same component on each selected entity, as the Inspector shows it. Agents name the component (and select the entities
/// first, or name one with "entity").
/// </summary>
public abstract class ComponentMenuCommand(InspectorPanel inspector) : EditorCommand
{
    /// <summary>The menu, as agents are told it.</summary>
    public const string Menu = "a component's menu in the Inspector, right-clicking its header";

    protected abstract int Group { get; }

    protected abstract int Order { get; }

    public override ContextMenuPlacement? ContextPlacement => new(Menu, Group, Order);

    public override CommandTarget? Target => new("component",
        "Which of the selected entities' components, as the Inspector names it (\"Health\", \"Box Collider\") or by its type (\"BoxCollider\").",
        name => inspector.Section(name));

    /// <summary>Why it can't run on <paramref name="section"/> now, or null when it can.</summary>
    protected abstract string? WhyNot(ComponentSection section);

    /// <summary>Runs it on <paramref name="section"/>.</summary>
    protected abstract void Run(ComponentSection section);

    public override string? WhyNot(object? target) => target is ComponentSection section ? WhyNot(section) : "it needs a component (\"component\")";

    protected override bool CanExecuteCore() => false;

    protected override bool CanExecuteCore(object? target) => target is ComponentSection section && WhyNot(section) is null;

    protected override Task ExecuteCoreAsync() => Task.CompletedTask;

    protected override Task ExecuteCoreAsync(object? target)
    {
        if (target is ComponentSection section)
            Run(section);
        return Task.CompletedTask;
    }

    // The values' commands are for a component every selected entity has; Add to All for one only some have.
    public override bool IsOnMenuFor(object? target) => target is ComponentSection { IsPartial: false };
}

/// <summary>Sets every value of a component back to what a new one starts with (on each selected entity), as one undo step.</summary>
public sealed class ResetComponentCommand(InspectorPanel inspector) : ComponentMenuCommand(inspector)
{
    public override string Id => "Component.Reset";
    public override string Text => "Reset to Defaults";
    public override string? Icon => Icons.Undo;
    protected override int Group => 0;
    protected override int Order => 0;

    public override string? TipFor(object? target) => "Sets every value back to what a new one starts with";

    protected override string? WhyNot(ComponentSection section) => section.WhyNotValues;

    protected override void Run(ComponentSection section) => section.ResetToDefaults();
}

/// <summary>Copies a component's values (the primary selected entity's), for Paste Values on another of its type.</summary>
public sealed class CopyComponentValuesCommand(InspectorPanel inspector) : ComponentMenuCommand(inspector)
{
    public override string Id => "Component.CopyValues";
    public override string Text => "Copy Values";
    public override string? Icon => Icons.Copy;
    protected override int Group => 1;
    protected override int Order => 0;

    protected override string? WhyNot(ComponentSection section) => section.WhyNotValues;

    protected override void Run(ComponentSection section) => section.CopyValues();
}

/// <summary>Sets the values copied with Copy Values on a component of the same type (on each selected entity), as one undo step.</summary>
public sealed class PasteComponentValuesCommand(InspectorPanel inspector) : ComponentMenuCommand(inspector)
{
    public override string Id => "Component.PasteValues";
    public override string Text => "Paste Values";
    protected override int Group => 1;
    protected override int Order => 1;

    public override string TextFor(object? target) => target is ComponentSection section ? section.PasteText : Text;

    protected override string? WhyNot(ComponentSection section) => section.WhyNotPaste;

    protected override void Run(ComponentSection section) => section.PasteValues();
}

/// <summary>Moves a component one place up its entity's list (each selected entity's), as one undo step; the Transform stays first.</summary>
public sealed class MoveComponentUpCommand(InspectorPanel inspector) : ComponentMenuCommand(inspector)
{
    public override string Id => "Component.MoveUp";
    public override string Text => "Move Up";
    protected override int Group => 2;
    protected override int Order => 0;

    protected override string? WhyNot(ComponentSection section) => section.WhyNotMove(-1);

    protected override void Run(ComponentSection section) => section.Move(-1);
}

/// <summary>Moves a component one place down its entity's list (each selected entity's), as one undo step.</summary>
public sealed class MoveComponentDownCommand(InspectorPanel inspector) : ComponentMenuCommand(inspector)
{
    public override string Id => "Component.MoveDown";
    public override string Text => "Move Down";
    protected override int Group => 2;
    protected override int Order => 1;

    protected override string? WhyNot(ComponentSection section) => section.WhyNotMove(1);

    protected override void Run(ComponentSection section) => section.Move(1);
}

/// <summary>
/// With several entities selected, adds a component only some of them have to the others, as a copy of the one shown (its
/// values too, and with what it needs), as one undo step.
/// </summary>
public sealed class AddComponentToAllCommand(InspectorPanel inspector) : ComponentMenuCommand(inspector)
{
    public override string Id => "Component.AddToAll";
    public override string Text => "Add to All";
    public override string? Icon => Icons.Add;
    protected override int Group => 0;
    protected override int Order => 1;

    public override bool IsOnMenuFor(object? target) => target is ComponentSection { IsPartial: true };

    public override string? TipFor(object? target) => (target as ComponentSection)?.PartialTip;

    protected override string? WhyNot(ComponentSection section) => section.WhyNotAddToAll;

    protected override void Run(ComponentSection section) => section.AddToAll();
}

/// <summary>
/// Removes a component from each selected entity that has it, as one undo step, asking first when another component
/// needs it. The Transform stays.
/// </summary>
public sealed class RemoveComponentCommand(InspectorPanel inspector) : ComponentMenuCommand(inspector)
{
    public override string Id => "Component.Remove";
    public override string Text => "Remove Component";
    public override string? Icon => Icons.Delete;
    protected override int Group => 3;
    protected override int Order => 0;

    public override bool IsOnMenuFor(object? target) => target is ComponentSection { CanRemove: true };

    public override string? TipFor(object? target) => (target as ComponentSection)?.RemoveTip;

    protected override string? WhyNot(ComponentSection section) => section.WhyNotRemove;

    protected override void Run(ComponentSection section) => section.Remove();
}

/// <summary>
/// A command on the menu of a field a placed prefab copy changed (the mark by it in the Inspector, or right-clicking its
/// name), run on that field of the selected copies.
/// </summary>
public abstract class PrefabFieldCommand(InspectorPanel inspector) : EditorCommand
{
    /// <summary>The menu, as agents are told it.</summary>
    public const string Menu = "the menu of a field a prefab copy changed, from the mark by it in the Inspector";

    protected abstract int Order { get; }

    public override ContextMenuPlacement? ContextPlacement => new(Menu, 0, Order);

    public override CommandTarget? Target => new("field",
        "Which field of the selected copies, as \"Component.Field\" (\"Pickup.Amount\"), by the names the Inspector shows or the property's.",
        name => inspector.Field(name));

    protected abstract string? WhyNot(PropertyField field);

    protected abstract void Run(PropertyField field);

    public override string? WhyNot(object? target) => target is PropertyField field ? WhyNot(field) : "it needs a field (\"field\")";

    protected override bool CanExecuteCore() => false;

    protected override bool CanExecuteCore(object? target) => target is PropertyField field && WhyNot(field) is null;

    protected override Task ExecuteCoreAsync() => Task.CompletedTask;

    protected override Task ExecuteCoreAsync(object? target)
    {
        if (target is PropertyField field)
            Run(field);
        return Task.CompletedTask;
    }
}

/// <summary>Sets a field a placed prefab copy changed back to the prefab's value (on each selected copy that changed it), as one undo step.</summary>
public sealed class RevertFieldToPrefabCommand(InspectorPanel inspector) : PrefabFieldCommand(inspector)
{
    public override string Id => "Component.RevertField";
    public override string Text => "Revert to Prefab";
    public override string? Icon => Icons.Undo;
    protected override int Order => 0;

    protected override string? WhyNot(PropertyField field) => field.WhyNotRevert;

    protected override void Run(PropertyField field) => field.RevertToPrefab();
}

/// <summary>
/// Makes the value a placed prefab copy has in a field its prefab's: saves it into the prefab file, and brings the prefab's
/// other copies up to date (what they changed themselves stays). Not while the selected copies' values differ.
/// </summary>
public sealed class ApplyFieldToPrefabCommand(InspectorPanel inspector) : PrefabFieldCommand(inspector)
{
    public override string Id => "Component.ApplyToPrefab";
    public override string Text => "Apply to Prefab";
    public override string? Icon => Icons.Package;
    protected override int Order => 1;

    public override string? TipFor(object? target) => "Saves this value into the prefab: every copy that hasn't changed it takes it too";

    protected override string? WhyNot(PropertyField field) => field.WhyNotApply;

    protected override void Run(PropertyField field) => field.ApplyToPrefab();
}
