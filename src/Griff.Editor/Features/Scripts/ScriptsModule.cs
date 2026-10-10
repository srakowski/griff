using System.IO;
using System.Text.RegularExpressions;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Scripts;

public sealed partial class ScriptsModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ScriptEditors>();
        services.AddSingleton<ScriptTabs>();
        services.AddCommand<NewScriptCommand>();
    }
}

/// <summary>
/// Creates a behavior script in Assets/Scripts, attaches it to the selected entity,
/// and opens it in the script editor.
/// </summary>
public sealed partial class NewScriptCommand(
    IWorkspace workspace,
    ISelection selection,
    IDialogService dialogs,
    ScriptEditors editors,
    ILogger<NewScriptCommand> logger) : EditorCommand
{
    private const string Template = """
        public class {0} : Behavior
        {{
            public float Speed {{ get; set; }} = 5;

            protected override void Start()
            {{
            }}

            protected override void Update()
            {{
                // Move with WASD or the arrow keys. Save this file while the game runs to see changes live.
                Transform.Position += new Vector3(Input.Horizontal, Input.Vertical, 0) * Speed * Time.Delta;
            }}
        }}

        """;

    public override string Id => "Script.New";
    public override string Text => "New _Script...";
    public override string? Icon => Icons.Code;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 2, Order: 0);

    protected override bool CanExecuteCore() => workspace.Project is not null && !workspace.IsPlaying;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Project is not { } project)
            return Task.CompletedTask;

        var folder = Path.Combine(project.AssetsPath, "Scripts");
        var name = dialogs.Prompt("New Script", "Class name", SuggestName(folder), n => Validate(n, folder));
        if (name is null)
            return Task.CompletedTask;

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".cs");
        File.WriteAllText(path, string.Format(Template, name).Replace("\n", Environment.NewLine));
        logger.LogInformation("Created {Path}", project.GetRelativePath(path));

        // Compile now (rather than waiting for the file watcher) so it can be attached straight away.
        var result = workspace.ReloadScripts();
        if (result?.Success == true &&
            selection.Entity is { } entity &&
            workspace.Scene is { } document &&
            workspace.Components.Find(name) is { } descriptor)
        {
            document.Execute(new AddComponentEdit(entity, descriptor.CreateInstance()));
        }

        editors.Open(path)?.NavigateToLine(12, 9);
        return Task.CompletedTask;
    }

    private string? Validate(string name, string folder)
    {
        if (!Identifier().IsMatch(name))
            return "Use letters, digits and underscores, starting with a letter, e.g. PlayerController.";
        if (File.Exists(Path.Combine(folder, name + ".cs")))
            return $"{name}.cs already exists.";
        if (workspace.Components.Find(name) is not null)
            return $"A component named {name} already exists.";
        return null;
    }

    private string SuggestName(string folder)
    {
        var name = "NewBehavior";
        for (var i = 2; Validate(name, folder) is not null; i++)
            name = $"NewBehavior{i}";
        return name;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
}
