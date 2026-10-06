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
    public const string New = "";
    public const string Open = "";
    public const string Save = "";
    public const string SaveAs = "";
    public const string Folder = "";
    public const string Exit = "";

    // Editing
    public const string Undo = "";
    public const string Redo = "";
    public const string Delete = "";
    public const string Clear = "";
    public const string Search = "";

    // Playback
    public const string Play = "";
    public const string Pause = "";
    public const string Stop = "";

    // Tools and panels
    public const string Settings = "";
    public const string Palette = "";
    public const string Console = "";

    // Status
    public const string Error = "";
    public const string Warning = "";
    public const string Info = "";

    // Glyphs used by control templates
    public const string CheckMark = "";
    public const string ChevronDown = "";
    public const string ChevronRight = "";
    public const string ChromeMinimize = "";
    public const string ChromeMaximize = "";
    public const string ChromeRestore = "";
    public const string ChromeClose = "";

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
