using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Features.Projects;

public partial class NewProjectWindow : ChromeWindow
{
    public NewProjectWindow(NewProjectViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Created += (_, _) => DialogResult = true;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }
}
