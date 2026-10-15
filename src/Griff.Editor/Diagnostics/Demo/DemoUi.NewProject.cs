#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Griff.Editor.Features.Projects;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Diagnostics.Demo;

// New Project: the window's name, folder, template list and buttons, and whether they're in view.
internal sealed partial class DemoUi
{
    /// <summary>The open New Project window, if any.</summary>
    public static NewProjectWindow? NewProject =>
        Application.Current.Windows.OfType<NewProjectWindow>().FirstOrDefault(w => w.IsVisible);

    private static NewProjectWindow OpenNewProject => NewProject ?? throw new InvalidOperationException("The New Project window isn't open.");

    /// <summary>Waits for the New Project window to open (after File > New Project... or Ctrl+Shift+N).</summary>
    public static async Task<NewProjectWindow> WaitForNewProject()
    {
        var window = await WaitFor(() => NewProject, "the New Project window");
        await Settle(400);
        return window;
    }

    /// <summary>
    /// A part of the open New Project window: name, location, browse, templates (the list), template:Empty (a template's
    /// row, by its name or title; scrolled to if it isn't showing), preview (the folder it makes, or what's wrong), cancel,
    /// create; or the whole window.
    /// </summary>
    public static FrameworkElement NewProjectPart(string part) => NewProjectPart(part, scroll: true)
        ?? throw new InvalidOperationException($"No {part} in the New Project window.");

    /// <summary>As <see cref="NewProjectPart(string)"/>, but with <paramref name="scroll"/> off a template whose row isn't
    /// in the list yet (scrolled far off) is null rather than scrolled to.</summary>
    public static FrameworkElement? NewProjectPart(string part, bool scroll)
    {
        var window = OpenNewProject;
        var (kind, name) = part.IndexOf(':') is var at and >= 0 ? (part[..at].Trim(), part[(at + 1)..].Trim()) : (part.Trim(), "");
        FrameworkElement Named(string x) => (FrameworkElement)window.FindName(x);
        return kind.ToLowerInvariant() switch
        {
            "" or "window" => (FrameworkElement)window.Content,
            "name" => Named("NameBox"),
            "location" => Named("LocationBox"),
            "browse" => Named("BrowseButton"),
            "templates" => Named("TemplateList"),
            "template" => TemplateRow(window, name, scroll),
            "preview" => Named("PreviewPanel"),
            "cancel" => Named("CancelButton"),
            "create" => Named("CreateButton"),
            _ => throw new FormatException($"Unknown New Project part \"{part}\" (name, location, browse, templates, template:, preview, cancel, create)."),
        };
    }

    /// <summary>A template's option in New Project, by its name ("TopDown") or title ("Top-down"), ignoring case, spaces and dashes.</summary>
    public static NewProjectViewModel.TemplateOption NewProjectTemplate(string name)
    {
        var model = (NewProjectViewModel)OpenNewProject.DataContext;
        var template = Griff.Projects.ProjectTemplates.Get(name);
        return model.Templates.First(t => t.Template == template);
    }

    private static ListBoxItem? TemplateRow(NewProjectWindow window, string name, bool scroll)
    {
        var option = NewProjectTemplate(name);
        var list = (ListBox)window.FindName("TemplateList");
        if (list.ItemContainerGenerator.ContainerFromItem(option) is ListBoxItem { IsVisible: true } row)
            return row;
        if (!scroll)
            return null;
        list.ScrollIntoView(option);
        list.UpdateLayout();
        return list.ItemContainerGenerator.ContainerFromItem(option) as ListBoxItem;
    }

    /// <summary>
    /// Whether something is wholly in view: showing, inside every scrolling area it's in (a row in a list), inside its
    /// window, and its window wholly on its screen's work area.
    /// </summary>
    public static bool InView(FrameworkElement element)
    {
        if (!element.IsVisible || Window.GetWindow(element) is not { Content: FrameworkElement content } window)
            return false;
        var shown = ScreenRect(element);
        var inside = ScreenRect(content);
        if (!Contains(inside, shown))
            return false;
        if (ChromeWindow.WorkArea(window) is { } area && !Contains(new Rect(area.X, area.Y, area.Width, area.Height), inside))
            return false;
        for (var up = VisualTreeHelper.GetParent(element); up is not null; up = VisualTreeHelper.GetParent(up))
        {
            if (up is ScrollContentPresenter viewport && !Contains(ScreenRect(viewport), shown))
                return false;
        }

        return true;
    }

    /// <summary>The title of the template New Project has picked.</summary>
    public static string NewProjectSelected => ((NewProjectViewModel)OpenNewProject.DataContext).SelectedTemplate.Template.Title;

    // A pixel of slack for rounding at the edges.
    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 1 && inner.Top >= outer.Top - 1 && inner.Right <= outer.Right + 1 && inner.Bottom <= outer.Bottom + 1;

    /// <summary>
    /// Rests the mouse on a list and turns the wheel until <paramref name="item"/>'s row is wholly in view, the way a
    /// person scrolls to something further down (or up).
    /// </summary>
    public async Task WheelTo(ListBox list, object item, string what)
    {
        var viewer = Descendants<ScrollViewer>(list).First();
        bool InView() => list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem { IsVisible: true } row && Contains(ScreenRect(viewer), ScreenRect(row));
        await input.MoveTo(Center(list));
        var down = list.Items.IndexOf(item) > Math.Max(0, (int)viewer.VerticalOffset);
        for (var i = 0; i < 40 && !InView(); i++)
        {
            await input.Scroll(down ? -1 : 1);
            await Settle(30);
        }

        if (!InView())
        {
            list.ScrollIntoView(item);
            Log?.Invoke($"FORCED scroll to {what}");
            await Settle(100);
        }
    }
}
#endif
