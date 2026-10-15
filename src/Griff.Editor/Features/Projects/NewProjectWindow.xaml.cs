using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Features.Projects;

/// <summary>
/// Name, folder and template for a new project. The template list scrolls in whatever height the window has, and keeps
/// the selected template in view however it was chosen (a click, the keys, or an agent setting SelectedTemplate).
/// </summary>
public partial class NewProjectWindow : ChromeWindow
{
    public NewProjectWindow(NewProjectViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Created += (_, _) => DialogResult = true;
        viewModel.Cancelled += (_, _) => Close();
        TemplateList.SelectionChanged += (_, _) => ShowSelected();
        Loaded += (_, _) =>
        {
            ShowSelected();
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void ShowSelected()
    {
        if (TemplateList.SelectedItem is { } selected)
            TemplateList.ScrollIntoView(selected);
    }
}
