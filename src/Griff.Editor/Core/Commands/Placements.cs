namespace Griff.Editor.Core.Commands;

/// <summary>Positions a command within a top-level menu.</summary>
/// <param name="Menu">Id of a registered <see cref="MenuDefinition"/>, usually one of <see cref="Menus"/>.</param>
/// <param name="Group">Commands are grouped by this value; separators are drawn between groups.</param>
/// <param name="Order">Sort order within the group.</param>
public sealed record MenuPlacement(string Menu, int Group = 0, int Order = 0);

/// <summary>Positions a command on the main toolbar. Separators are drawn between groups.</summary>
public sealed record ToolbarPlacement(int Group = 0, int Order = 0);

/// <summary>A top-level menu. Menus with no commands are not shown.</summary>
public sealed record MenuDefinition(string Id, string Text, int Order);

public static class Menus
{
    public const string File = "File";
    public const string Edit = "Edit";
    public const string View = "View";
    public const string Entity = "Entity";
    public const string Game = "Game";
    public const string Build = "Build";
    public const string Debug = "Debug";
    public const string Help = "Help";
}
