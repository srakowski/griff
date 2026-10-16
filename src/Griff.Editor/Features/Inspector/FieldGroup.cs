using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Scenes;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// A subheading in a component's section (from <see cref="GroupAttribute"/>), "Dash" or "Wall", over the fields that
/// go under it. Folding it shut hides them, for that component on every entity, and it's remembered next time.
/// </summary>
public sealed class FieldGroup : ObservableObject
{
    private readonly ComponentSection _section;
    private readonly InspectorPanel _owner;

    public FieldGroup(string name, IReadOnlyList<PropertyField> fields, ComponentSection section, InspectorPanel owner)
    {
        Name = name;
        Fields = fields;
        _section = section;
        _owner = owner;
        foreach (var field in fields)
            field.Group = this;
    }

    public string Name { get; }

    public IReadOnlyList<PropertyField> Fields { get; }

    // Kept with the components' folded sections, under "Type/Group", which no type's name can be.
    private string Key => $"{_section.TypeKey}/{Name}";

    /// <summary>Open, showing its fields.</summary>
    public bool IsExpanded
    {
        get => !_owner.IsCollapsed(Key);
        set => _owner.SetCollapsed(Key, !value);
    }

    /// <summary>True when every field under it is faded, as a dash's are with Dash off (see <see cref="PropertyField.IsFaded"/>).</summary>
    public bool IsFaded => Fields.All(f => f.IsFaded);

    /// <summary>True while the Inspector's filter hides every field under it, which hides the subheading too.</summary>
    public bool IsFilteredOut => Fields.All(f => f.IsFilteredOut);

    /// <summary>Says what folding does, and when it's shut, how many fields it hides.</summary>
    public string Tip => IsExpanded ? $"Fold {Name} shut" : $"Open {Name}: {Fields.Count} field{(Fields.Count == 1 ? "" : "s")}";

    /// <summary>Folds it shut, or opens it: clicking its name.</summary>
    public IRelayCommand ToggleCommand => _toggle ??= new RelayCommand(() => IsExpanded = !IsExpanded);

    private IRelayCommand? _toggle;

    public void Refresh() => OnPropertyChanged(string.Empty);
}
