using Griff.Projects;

namespace Griff.Editing;

/// <summary>
/// Sets the project's step rate (see <see cref="Project.StepRate"/>) as an edit: it's saved in project.griff as it's
/// made, undone and redone like any other edit, and doesn't leave the open scene with unsaved changes.
/// <paramref name="changed"/> hears the rate each time it changes (applied, undone or redone), so a running game can
/// follow it.
/// </summary>
public sealed class SetStepRateEdit(Project project, int rate, Action<int>? changed = null) : IEdit
{
    private readonly int _before = project.StepRate;

    public string Description => $"Step Rate {Math.Clamp(rate, Runtime.Time.MinStepRate, Runtime.Time.MaxStepRate)}";

    public bool ChangesDocument => false;

    public void Apply() => Set(rate);

    public void Revert() => Set(_before);

    private void Set(int value)
    {
        project.SetStepRate(value);
        changed?.Invoke(project.StepRate);
    }
}
