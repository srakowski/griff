namespace Griff.Editor.Core.Commands;

/// <summary>
/// Marks a command that throws something away (deletes, cuts, clears saved data, quits), so agents are told so before they
/// run it (the tool is marked destructive).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DestructiveAttribute : Attribute;
