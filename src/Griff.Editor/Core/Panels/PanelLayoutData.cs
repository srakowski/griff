namespace Griff.Editor.Core.Panels;

/// <summary>
/// A panel layout as it's kept in the settings: the last one used (put back when the editor starts) and the ones saved
/// by name. Panels are named by their ids; ones it doesn't mention go back where they start out.
/// </summary>
public sealed class PanelLayoutData
{
    /// <summary>The name it was saved under, or null for the layout the editor last had.</summary>
    public string? Name { get; set; }

    public PanelLayoutPart Root { get; set; } = new();

    public List<FloatingLayoutData> Floating { get; set; } = [];

    /// <summary>The panels closed (their tabs gone until View > Panels opens them again).</summary>
    public List<string> Closed { get; set; } = [];

    /// <summary>Where the editor's own window was, for the layout it last had (a saved layout leaves the window be).</summary>
    public EditorWindowData? Window { get; set; }
}

/// <summary>Where the editor's window was on screen, and whether it filled it.</summary>
public sealed class EditorWindowData
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool Maximized { get; set; }
}

/// <summary>A part of a kept layout: "split" (its <see cref="Children"/>), "tabs" (its <see cref="Panels"/>) or "documents".</summary>
public sealed class PanelLayoutPart
{
    public const string SplitKind = "split";
    public const string TabsKind = "tabs";
    public const string DocumentsKind = "documents";

    public string Kind { get; set; } = DocumentsKind;

    /// <summary>For a split: side by side, or one above another.</summary>
    public bool Horizontal { get; set; }

    public List<PanelLayoutPart>? Children { get; set; }

    public List<string>? Panels { get; set; }

    public string? Selected { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}

/// <summary>A floating window in a kept layout: its tabs and where it was on screen.</summary>
public sealed class FloatingLayoutData
{
    public List<string> Panels { get; set; } = [];

    public string? Selected { get; set; }

    /// <summary>Null when it hadn't been placed yet: it opens near the middle of the editor.</summary>
    public double? Left { get; set; }

    public double? Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}
