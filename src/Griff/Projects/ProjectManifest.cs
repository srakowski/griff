using System.Text.Json.Serialization;

namespace Griff.Projects;

/// <summary>Contents of a project's <c>project.griff</c> file.</summary>
/// <param name="StartScene">Scene opened first and run when the game starts, relative to the project folder.</param>
/// <param name="Controls">
/// The game's own input actions, set in the editor's Controls window: each action's keys and buttons by name, e.g.
/// <c>"Jump": ["Space", "W", "Gamepad A"]</c>. Only actions that differ from Griff's built-in ones are listed.
/// </param>
/// <param name="StepRate">
/// How many steps a second the game takes (see <see cref="Runtime.Time.StepRate"/>), set with Game &gt; Step Rate; null for
/// the usual 60.
/// </param>
/// <param name="NewRandomEachGame">
/// Whether each new game's random numbers (<see cref="Rand"/>) start from a seed taken from the clock, set with Game &gt; New
/// Random Numbers Each Game; null for the usual, where every game played by one running editor or player starts from the same.
/// </param>
/// <param name="SaveVersion">
/// The version of the game's saved games (see <see cref="Runtime.Game.SaveVersion"/>), set with Game &gt; Save Version;
/// null for 0, as a game starts.
/// </param>
public sealed record ProjectManifest(
    string Name,
    int FormatVersion,
    DateTimeOffset CreatedAt,
    string? StartScene = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, string[]>? Controls = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? StepRate = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? NewRandomEachGame = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SaveVersion = null)
{
    public const int CurrentFormatVersion = 1;
}
