using CommunityToolkit.Mvvm.ComponentModel;

namespace Griff.Editor.Core.Panels;

public enum PanelZone
{
    Left,
    Right,
    Bottom,
}

/// <summary>
/// View model for a tool panel (hierarchy, inspector, output, ...). Register with
/// <see cref="PanelServiceCollectionExtensions.AddPanel{TPanel, TView}"/>; the shell creates the view,
/// sets this as its DataContext and places it in <see cref="Zone"/>.
/// </summary>
public abstract class ToolPanel : ObservableObject
{
    public abstract string Id { get; }

    public abstract string Title { get; }

    /// <summary>Glyph from <see cref="Themes.Icons"/>.</summary>
    public virtual string? Icon => null;

    public abstract PanelZone Zone { get; }

    /// <summary>Tab order within the zone.</summary>
    public virtual int Order => 0;
}
