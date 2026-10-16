using CommunityToolkit.Mvvm.Input;
using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Something a component needs on its entity and doesn't have: "Needs a Body (what it pushes), which Player doesn't
/// have", with a button that adds one (with what that needs in turn).
/// </summary>
public sealed class NeedRow
{
    public NeedRow(ComponentLink requirement, int lacking, IReadOnlyList<Component> components, InspectorPanel owner, ComponentRegistry registry)
    {
        Requirement = requirement;
        var name = requirement.Type.Name;
        var note = requirement.Note is null ? "" : $" ({requirement.Note})";
        Message = components.Count == 1
            ? $"Needs {Griff.Editing.ComponentCatalog.Article(name)}{note}, which {components[0].Entity?.Name} doesn't have."
            : $"Needs {Griff.Editing.ComponentCatalog.Article(name)}{note}, which {lacking} of the {components.Count} don't have.";
        var satisfier = Griff.Editing.ComponentCatalog.Satisfier(registry, requirement.Type);
        AddLabel = satisfier is null ? "" : $"Add {InspectorPanel.Humanize(satisfier.DisplayName)}";
        var options = Griff.Editing.ComponentCatalog.Options(registry, requirement.Type);
        Tip = satisfier is null ? null
            : options.Count > 1 ? $"Any kind of {name} works ({string.Join(" or ", options.Select(o => InspectorPanel.Humanize(o.DisplayName)))}); this adds {Griff.Editing.ComponentCatalog.Article(InspectorPanel.Humanize(satisfier.DisplayName))}."
            : $"Adds {Griff.Editing.ComponentCatalog.Article(InspectorPanel.Humanize(satisfier.DisplayName))}{(components.Count == 1 ? "" : " to each that doesn't have one")}.";
        AddCommand = new RelayCommand(() => owner.AddRequirement(components, requirement), () => satisfier is not null);
    }

    /// <summary>The component it needs (a base type, like Collider, takes any kind).</summary>
    public ComponentLink Requirement { get; }

    /// <summary>The type it needs, by name: what a demo or agent asks for it by.</summary>
    public string Name => Requirement.Type.Name;

    public string Message { get; }

    /// <summary>"Add Body", or for a kind of thing, the kind it adds ("Add Box Collider").</summary>
    public string AddLabel { get; }

    public string? Tip { get; }

    public IRelayCommand AddCommand { get; }
}
