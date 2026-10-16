#if DEBUG
using System.Windows;
using Griff.Editor.Core.Modules;
using Griff.Runtime;
using Vector2 = System.Numerics.Vector2;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// What a genre adds to the demo player: the .demo commands that drive its windows and panels (the Move List, the Chart,
/// the Dialogue Editor, the Clip Editor's boxes), their parts to point at, and what it knows of a running game of its kind
/// (a strategy game's HUD and standings, a sandbox's tools). Each genre that has any keeps one in its folder
/// (Genres/&lt;Genre&gt;, see <see cref="GenreEditors"/>), found as the demo player starts, so <see cref="DemoFile"/> names no genre.
/// </summary>
internal abstract class DemoGenre
{
    private static IReadOnlyList<DemoGenre>? _all;

    /// <summary>Every genre's.</summary>
    public static IReadOnlyList<DemoGenre> All => _all ??= [.. GenreEditors.Instances<DemoGenre>()];

    /// <summary>Plays a .demo line of this genre's ("moves:", "noteclick: 4.5, 3"): true if it's one of its commands.</summary>
    public virtual Task<bool> Play(DemoFile demo, string command, string argument) => Task.FromResult(false);

    /// <summary>
    /// A part of a window or panel to point at, "<paramref name="kind"/>:<paramref name="name"/>": this genre's own ("moves",
    /// "chart", "dialogue"), or what it adds to another's ("clips": the Clip Editor's boxes); null if it isn't this genre's.
    /// </summary>
    public virtual FrameworkElement? Part(string kind, string name) => null;

    /// <summary>The parts of the running game's HUD this genre names (hud:minimap), for the list an unknown one gets.</summary>
    public virtual IEnumerable<string> HudParts => [];

    /// <summary>
    /// Where a part of the running game's HUD is, in world units, top left and bottom right (a strategy game's minimap);
    /// null if <paramref name="part"/> isn't this genre's.
    /// </summary>
    public virtual (Vector2 TopLeft, Vector2 BottomRight)? HudArea(Game game, string part) => null;

    /// <summary>
    /// The kind of the HUD's buttons a part of the HUD is (a sandbox's hotbar: its "Slot"s), whose buttons together are the
    /// part; null if <paramref name="part"/> isn't this genre's.
    /// </summary>
    public virtual string? HudButtons(string part) => null;

    /// <summary>Where the game's minimap shows <paramref name="world"/>, in world units; null if the game has none of this genre's.</summary>
    public virtual Vector2? OnMinimap(Game game, Vector2 world) => null;

    /// <summary>True when this genre's HUD covers <paramref name="world"/>.</summary>
    public virtual bool Covers(Game game, Vector2 world) => false;

    /// <summary>What a game of this kind shows that the log should say after a click: the player's resources, the tool held.</summary>
    public virtual string Describe(Game game) => "";

    /// <summary>How the sides of a game of this kind stand, for a long wait's log; empty for none.</summary>
    public virtual string Standings(Game game) => "";

    /// <summary>
    /// Turns what the player looks through as moving the mouse <paramref name="x"/> pixels would have, when the game didn't
    /// take the mouse: true if this genre's game has something to turn.
    /// </summary>
    public virtual bool Look(Game game, double x) => false;
}
#endif
