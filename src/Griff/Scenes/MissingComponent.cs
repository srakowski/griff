using System.Text.Json;

namespace Griff.Scenes;

/// <summary>
/// Stands in for a component whose type couldn't be found when the scene was loaded
/// (for example, a script that was deleted or renamed). Its saved data is kept and written back
/// unchanged, so restoring the type restores the component.
/// </summary>
public sealed class MissingComponent : Component
{
    public MissingComponent(string typeName, JsonElement data)
    {
        TypeName = typeName;
        Data = data.Clone();
    }

    /// <summary>The component type name recorded in the scene file.</summary>
    [DoNotSerialize]
    public string TypeName { get; }

    /// <summary>The component's saved JSON, including its "type" field.</summary>
    [DoNotSerialize]
    public JsonElement Data { get; }
}
