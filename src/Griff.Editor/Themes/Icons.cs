using System.Windows;
using System.Windows.Controls;

namespace Griff.Editor.Themes;

/// <summary>
/// Icon glyphs from Segoe Fluent Icons (Windows 11), falling back to Segoe MDL2 Assets (Windows 10).
/// Reference these constants instead of raw code points so icons stay consistent and searchable.
/// In XAML: <c>Text="{x:Static theme:Icons.Save}" Style="{StaticResource Style.Icon}"</c>.
/// </summary>
public static class Icons
{
    // Files
    public const string New = "\uE8A5";
    public const string Open = "\uE8E5";
    public const string Save = "\uE74E";
    public const string SaveAs = "\uE792";
    public const string Audio = "\uE8D6";
    public const string Music = "\uEC4F";
    public const string SceneFile = "\uE80A";
    public const string Folder = "\uE8B7";
    public const string NewProject = "\uE8F4";
    public const string Package = "\uE7B8";
    public const string Globe = "\uE774";
    public const string Exit = "\uE7E8";

    // Editing
    public const string Add = "\uE710";
    public const string Copy = "\uE8C8";
    public const string Undo = "\uE7A7";
    public const string Redo = "\uE7A6";
    public const string Delete = "\uE74D";
    public const string Clear = "\uE894";
    public const string Search = "\uE721";
    public const string Library = "\uE8F1";
    public const string Link = "\uE71B";
    public const string Shuffle = "\uE8B1";
    public const string Lock = "\uE72E";
    public const string Show = "\uE890";
    public const string Hide = "\uED1A";
    public const string Rename = "";

    // Playback
    public const string Play = "\uE768";
    public const string Pause = "\uE769";
    public const string Step = "\uE893";
    public const string Stop = "\uE71A";
    public const string Record = "\uE7C8";
    public const string Bug = "\uEBE8";
    public const string StepOver = "\uE72A";
    public const string StepInto = "\uE74B";
    public const string StepOut = "\uE74A";
    public const string Volume = "\uE767";
    public const string Mute = "\uE74F";

    // Tools and panels
    public const string Hierarchy = "\uE8FD";
    public const string Inspector = "\uE9E9";
    public const string Image = "\uEB9F";
    public const string Font = "\uE8D2";
    public const string Camera = "\uE722";
    public const string Settings = "\uE713";
    public const string Palette = "\uE790";
    public const string Code = "\uE943";
    public const string Console = "\uE756";
    public const string Gamepad = "\uE7FC";
    public const string Profiler = "\uE9D9";
    public const string Table = "\uF0E2";
    public const string Simulation = "\uE8B1";
    public const string Chart = "";
    public const string Draw = "";

    // Things to create (the Entity menu and the Add Entity palette); a module's preset names one (see Named)
    public const string Box = "\uE739";
    public const string Runner = "\uE726";
    public const string Walker = "\uE805";
    public const string Person = "\uE77B";
    public const string Robot = "\uE99A";
    public const string Bolt = "\uE945";
    public const string Turns = "\uE895";
    public const string Tiles = "\uE80A";
    public const string Frame = "\uE9A6";
    public const string Climb = "\uE8CB";
    public const string Lever = "\uE707";
    public const string Cube = "\uF158";
    public const string Bounce = "\uE752";
    public const string Tap = "\uE815";
    public const string Checkbox = "\uE73A";
    public const string Sliders = "\uE9E9";
    public const string LeftRight = "\uE8AB";
    public const string Battery = "\uE83F";
    public const string Window = "\uE737";
    public const string Speech = "\uE8BD";
    public const string Speaker = "\uE7F5";
    public const string Stopwatch = "\uE916";
    public const string Lightbulb = "\uEA80";
    public const string Confetti = "\uEC24";
    public const string Cloud = "\uE753";
    public const string Key = "\uE8D7";
    public const string Blocks = "\uECA5";
    public const string Star = "\uE735";
    public const string Flag = "\uE7C1";
    public const string Arrow = "\uE8F0";
    public const string Card = "\uE8A1";
    public const string Zone = "\uE8A9";
    public const string Recent = "\uE823";

    // Tables
    public const string Cut = "\uE8C6";
    public const string Paste = "\uE77F";
    public const string SortUp = "\uE74A";
    public const string SortDown = "\uE74B";
    public const string Filter = "\uE71C";
    public const string Import = "\uE896";
    public const string Export = "\uE898";
    public const string MoveUp = "\uE74A";
    public const string MoveDown = "\uE74B";
    public const string MoveLeft = "\uE72B";
    public const string MoveRight = "\uE72A";
    public const string FillDown = "\uE7C3";

    // Status
    public const string Error = "\uEA39";
    public const string Warning = "\uE7BA";
    public const string Info = "\uE946";

    // Glyphs used by control templates
    public const string CheckMark = "\uE73E";
    public const string ChevronDown = "\uE70D";
    public const string ChevronRight = "\uE76C";
    public const string ChromeMinimize = "\uE921";
    public const string ChromeMaximize = "\uE922";
    public const string ChromeRestore = "\uE923";
    public const string ChromeClose = "\uE8BB";

    /// <summary>
    /// The glyph of the icon called <paramref name="name"/> (one of these constants, e.g. "Shuffle"), for things outside
    /// the editor that name theirs (a module's presets and actions); null for none or an unknown name.
    /// </summary>
    public static string? Named(string? name) =>
        name is null ? null : typeof(Icons).GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) as string;

    /// <summary>Creates an icon element for use as menu, button or tab content.</summary>
    public static TextBlock Create(string glyph, double? size = null)
    {
        var icon = new TextBlock { Text = glyph };
        icon.SetResourceReference(FrameworkElement.StyleProperty, "Style.Icon");
        if (size is { } s)
            icon.FontSize = s;
        return icon;
    }
}
