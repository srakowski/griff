using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Projects;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Core.Projects;

/// <summary>Owns the project open in the editor.</summary>
public interface IProjectService : INotifyPropertyChanged
{
    Project? Current { get; }

    /// <summary>Creates a project in a new folder inside <paramref name="location"/> and makes it current.</summary>
    Task<Project> CreateAsync(string location, string name);
}

public sealed class ProjectService(
    ISettingsStore settings,
    IStatusService status,
    ILogger<ProjectService> logger) : ObservableObject, IProjectService
{
    private Project? _current;

    public Project? Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    public async Task<Project> CreateAsync(string location, string name)
    {
        var project = await Project.CreateAsync(location, name);
        logger.LogInformation("Created project {Name} at {Path}", project.Name, project.RootPath);

        settings.Settings.LastProjectLocation = location;
        settings.Save();

        Current = project;
        status.Show($"Created project {project.Name}");
        return project;
    }
}
