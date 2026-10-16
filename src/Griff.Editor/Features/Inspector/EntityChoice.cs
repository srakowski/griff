using Griff.Scenes;

namespace Griff.Editor.Features.Inspector;

/// <summary>An entity an entity field can name, how deep it is in the Hierarchy, and whether the field names it now.</summary>
public sealed record EntityChoice(Entity Entity, int Depth, bool IsNamed)
{
    /// <summary>Where it is ("Gate/Hint"), and whether it's named now: what agents see of it in a list.</summary>
    public string Label
    {
        get
        {
            var path = Entity.Name;
            for (var parent = Entity.Parent; parent is not null; parent = parent.Parent)
                path = $"{parent.Name}/{path}";
            return IsNamed ? $"{path} (named)" : path;
        }
    }
}
