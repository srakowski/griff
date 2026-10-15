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
