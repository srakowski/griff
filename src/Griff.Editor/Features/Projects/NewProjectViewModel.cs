using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Core.Settings;
using Griff.Projects;

namespace Griff.Editor.Features.Projects;

/// <summary>
/// Name + parent location, with a live preview of the folder that will be created.
/// Project settings (template, render pipeline, ...) will be added here as they're defined.
/// </summary>
public sealed class NewProjectViewModel : ObservableObject
{
    private const string DefaultName = "My Project";

    private readonly IProjectSession _projectSession;
    private readonly IDialogService _dialogs;
    private string _name;
    private string _location;
    private string? _error;

    public NewProjectViewModel(IProjectSession projectSession, IDialogService dialogs, ISettingsStore settings)
    {
        _projectSession = projectSession;
        _dialogs = dialogs;
        _location = settings.Settings.LastProjectLocation ?? DefaultLocation;
        _name = UniqueName(_location);

        BrowseCommand = new RelayCommand(Browse);
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => Error is null);
        CancelCommand = new RelayCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
        Validate();
    }

    /// <summary>Raised after the project has been created; the view closes in response.</summary>
    public event EventHandler? Created;

    /// <summary>Raised by Cancel; the view closes without making anything.</summary>
    public event EventHandler? Cancelled;

    public static string DefaultLocation =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Griff Projects");

    public string Name
    {
        get => _name;
        set { if (SetProperty(ref _name, value)) Validate(); }
    }

    public string Location
    {
        get => _location;
        set { if (SetProperty(ref _location, value)) Validate(); }
    }

    /// <summary>The folder that will be created.</summary>
    public string TargetPath => Project.GetProjectPath(Location.Trim(), Name);

    public string? Error
    {
        get => _error;
        private set
        {
            if (SetProperty(ref _error, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => Error is not null;

    public sealed record TemplateOption(ProjectTemplate Template, string Name, string Description);

    /// <summary>Every template Griff has, in the order they're listed (each says where, with its Order).</summary>
    public IReadOnlyList<TemplateOption> Templates { get; } =
        ProjectTemplates.All.Select(t => new TemplateOption(t, t.Title, t.Description)).ToList();

    private TemplateOption? _selectedTemplate;

    /// <summary>Starts on Platformer: a jam should begin with something playable.</summary>
    public TemplateOption SelectedTemplate
    {
        get => _selectedTemplate ??= Templates[0];
        set => SetProperty(ref _selectedTemplate, value);
    }

    public IRelayCommand BrowseCommand { get; }

    public IAsyncRelayCommand CreateCommand { get; }

    /// <summary>Closes the window without making a project.</summary>
    public IRelayCommand CancelCommand { get; }

    private void Browse()
    {
        if (_dialogs.ShowFolderDialog(Location) is { } folder)
            Location = folder;
    }

    private async Task CreateAsync()
    {
        try
        {
            await _projectSession.CreateProjectAsync(Location.Trim(), Name, SelectedTemplate.Template);
            Created?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Keep the dialog open so the user can fix the name or location.
            Error = ex.Message;
        }
    }

    private void Validate()
    {
        Error = ProjectNames.Validate(Name) ?? ValidateLocation();
        OnPropertyChanged(nameof(TargetPath));
        CreateCommand.NotifyCanExecuteChanged();
    }

    private string? ValidateLocation()
    {
        var location = Location.Trim();
        if (location.Length == 0)
            return "Choose a location.";

        if (!Path.IsPathFullyQualified(location) || location.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return "Location must be a full folder path, like C:\\Games.";

        if (IsOccupied(TargetPath))
            return $"A folder named \"{Name.Trim()}\" already exists here.";

        return null;
    }

    // An existing empty folder is fine to create into.
    private static bool IsOccupied(string path) =>
        Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();

    private static string UniqueName(string location)
    {
        var name = DefaultName;
        for (var i = 2; IsOccupied(Project.GetProjectPath(location, name)); i++)
            name = $"{DefaultName} {i}";
        return name;
    }
}
