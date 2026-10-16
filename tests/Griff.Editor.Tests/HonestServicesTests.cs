using System.IO;
using System.Numerics;
using Griff.Editing;
using Griff.Editor.Core;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Entities;
using Griff.Editor.Features.Scripts.Editor;
using Griff.Scenes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The editor's presets and services say what they are. Everything the Entity menu creates is an <see cref="EntityPreset"/>
/// in Griff (so templates, tests and agents without the editor have it too), the editor's services take what they use in
/// their constructors (no asking an <see cref="IServiceProvider"/> at run time, no casting the application's main window), its
/// commands are reached by their class (not an id string), the workspace is four small faces rather than one of 35 members,
/// and the script editor's font size is in the settings. The editor's Debug-only drivers (the snapshot runner and the demo
/// player in Diagnostics) are tools around the editor, not services in it, and drive it by command id as agents do; so the
/// rules about ids, the main window and the workspace as a whole leave them out.
/// </summary>
public sealed class HonestServicesTests
{
    [Fact]
    public void EveryThingTheEntityMenuCreates_IsAPresetInGriff()
    {
        // No hand-written create command is left in the editor, whatever it's called.
        var offenders = Sources().SelectMany(s => s.Root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(c => c.Identifier.Text.StartsWith("Create", StringComparison.Ordinal) && c.Identifier.Text.EndsWith("Command", StringComparison.Ordinal))
            .Select(c => $"{s.Name}: {c.Identifier.Text}")).ToList();
        Assert.True(offenders.Count == 0, "Entity menu things are EntityPresets in Griff (Griff/Editing/Presets), not command classes:\n" + string.Join("\n", offenders));

        string[] basics = ["Entity.CreateEmpty", "Entity.CreateSprite", "Entity.CreateCamera", "Entity.CreateText", "Entity.CreateTilemap", "Entity.CreateParticles",
            "Entity.CreateButton", "Entity.CreateSlider", "Entity.CreateToggle", "Entity.CreateBar", "Entity.CreateStars", "Entity.CreateDialogueBox",
            "Entity.CreateSwitch", "Entity.CreatePanel", "Entity.CreateChoice", "Entity.CreatePlatformerPlayer", "Entity.CreateTopDownPlayer",
            "Entity.CreateGridPlayer", "Entity.CreateGridEnemy", "Entity.CreateTurnOrder", "Entity.CreateLadder", "Entity.CreateSpring", "Entity.CreateCrate",
            "Entity.CreateLight", "Entity.CreateWeather", "Entity.CreateMusic", "Entity.CreateSoundSource", "Entity.CreateSoundPlayer", "Entity.CreateBeatClock",
            "Entity.CreateCalibration", "Entity.CreateRoom"];
        var ids = EntityPreset.All.Select(p => p.Id).ToHashSet();
        Assert.True(basics.All(ids.Contains), string.Join(", ", basics.Where(id => !ids.Contains(id))));
    }

    [Fact]
    public void ThePresets_RunWithNoEditor()
    {
        // Griff alone makes each one's entity in a scene of its own, as a template, a test or an agent would.
        foreach (var preset in EntityPreset.All)
        {
            var scene = new Scene();
            var entity = new Entity(preset.EntityName(scene));
            preset.Configure(entity, scene);
            var children = preset.CreateChildren(scene).ToList();
            preset.Arrange(entity, children, null);
            Assert.False(string.IsNullOrWhiteSpace(entity.Name), preset.Id);
        }
    }

    [Fact]
    public void AMenuControl_DrawsAboveThePanelItIsMadeOn()
    {
        var preset = EntityPreset.All.Single(p => p.Id == "Entity.CreateButton");
        var panel = new Entity("Panel");
        panel.AddComponent<Sprite>().Order = 300;
        var button = new Entity("Button");
        preset.Configure(button, new Scene());
        preset.Arrange(button, [], panel);
        Assert.Equal(310, button.GetComponent<Sprite>()!.Order);

        var alone = new Entity("Button");
        preset.Configure(alone, new Scene());
        preset.Arrange(alone, [], null);
        Assert.Equal(100, alone.GetComponent<Sprite>()!.Order);
    }

    [Fact]
    public void TheTurnOrder_DrawsItsEndTurnButtonAboveItsText_AndGridMoversJoinIn() => EditorHost.Run(services =>
    {
        var preset = EntityPreset.All.Single(p => p.Id == "Entity.CreateTurnOrder");
        var scene = new Scene();
        var turns = new Entity(preset.EntityName(scene));
        preset.Configure(turns, scene);
        var children = preset.CreateChildren(scene).ToList();
        var under = new Entity("Backdrop");
        under.AddComponent<Sprite>().Order = 400;
        preset.Arrange(turns, children, under);
        Assert.Equal(410, turns.GetComponent<Griff.Scenes.Text>()!.Order);
        Assert.Equal(410, children[0].GetComponent<Sprite>()!.Order);
        Assert.Equal(411, children[0].GetComponent<Griff.Scenes.Text>()!.Order);

        // The same one through the editor's command is one undo step.
        var command = services.GetRequiredService<CommandRegistry>().Get(preset.Id);
        Assert.IsType<PresetCommand>(command);
        var gesture = services.GetRequiredService<CommandRegistry>().Get("Entity.CreateEmpty").Gesture;
        Assert.Equal((System.Windows.Input.Key.N, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt), (gesture?.Key, gesture?.Modifiers));
    });

    [Fact]
    public void NoServiceAsksAnIServiceProvider()
    {
        // Composition (the catalog the agents' ui_ tools reflect over, and the Lazy a service asks for) is the exception.
        var allowed = new[] { "ServiceCatalog", "LazyService" };
        var found = new List<string>();
        foreach (var source in Sources().Where(s => !s.InDiagnostics))
        {
            foreach (var type in source.Root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var parameters = type.ParameterList?.Parameters.AsEnumerable() ?? [];
                parameters = parameters.Concat(type.Members.OfType<ConstructorDeclarationSyntax>().SelectMany(c => c.ParameterList.Parameters));
                if (parameters.Any(p => p.Type?.ToString().TrimEnd('?') is "IServiceProvider" or "System.IServiceProvider"))
                    found.Add($"{source.Name}: {type.Identifier.Text}");
            }
        }

        Assert.True(found.All(f => allowed.Any(a => f.EndsWith(": " + a, StringComparison.Ordinal))),
            "Services take what they use in their constructors (a Lazy<T> or Func<T> when it can't be made first), not an IServiceProvider:\n" + string.Join("\n", found));
    }

    [Fact]
    public void NoServiceCastsTheMainWindow()
    {
        var found = new List<string>();
        foreach (var source in Sources().Where(s => !s.InDiagnostics && s.Name is not ("EditorWindow.cs" or "App.xaml.cs")))
        {
            found.AddRange(source.Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Where(m => m.Name.Identifier.Text == "MainWindow").Select(m => $"{source.Name}: {m}"));
        }

        Assert.True(found.Count == 0, "Take EditorWindow (its Main is the main window) instead of Application.Current.MainWindow:\n" + string.Join("\n", found));
    }

    [Fact]
    public void CommandsAreReachedByTheirClass_NotByIdStrings() => EditorHost.Run(services =>
    {
        var ids = services.GetRequiredService<CommandRegistry>().All.Select(c => c.Id).ToHashSet();
        var found = new List<string>();
        // The agents' tools and the settings name commands by id by design (an id is what agents call, and what was last made is kept by it).
        foreach (var source in Sources().Where(s => !s.InDiagnostics && !s.Path.Contains(Path.Combine("Features", "Agent"), StringComparison.Ordinal)))
        {
            foreach (var literal in source.Root.DescendantNodes().OfType<LiteralExpressionSyntax>().Where(l => l.IsKind(SyntaxKind.StringLiteralExpression)))
            {
                if (!ids.Contains(literal.Token.ValueText))
                    continue;
                // The command's own Id is the one place its string is written.
                if (literal.Parent is ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax { Identifier.Text: "Id" } })
                    continue;
                found.Add($"{source.Name}: \"{literal.Token.ValueText}\"");
            }
        }

        Assert.True(found.Count == 0, "Reach a command with CommandRegistry.Get<TheCommand>() (or take it in the constructor):\n" + string.Join("\n", found));
        Assert.Same(services.GetRequiredService<CommandRegistry>().Get("Entity.Hide"), services.GetRequiredService<CommandRegistry>().Get<HideEntityCommand>());
    });

    [Fact]
    public void TheWorkspaceIsFourSmallFaces_AndNothingTakesItWhole() => EditorHost.Run(services =>
    {
        Assert.Null(typeof(Core.Workspace.Workspace).Assembly.GetType("Griff.Editor.Core.Workspace.IWorkspace"));
        foreach (var face in new[] { typeof(IProjectSession), typeof(ISceneDocuments), typeof(IPlaySession), typeof(IScriptSession) })
        {
            var members = face.GetMembers().Count(m => m.MemberType is not System.Reflection.MemberTypes.NestedType && !(m is System.Reflection.MethodInfo { IsSpecialName: true }));
            Assert.True(members <= 16, $"{face.Name} has {members} members: split it.");
            Assert.Same(services.GetRequiredService<Core.Workspace.Workspace>(), services.GetRequiredService(face));
        }

        // No service takes the Workspace itself, only the faces it uses.
        var found = new List<string>();
        foreach (var source in Sources().Where(s => !s.InDiagnostics && s.Name is not ("Workspace.cs" or "App.xaml.cs")))
        {
            foreach (var type in source.Root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var parameters = type.ParameterList?.Parameters.AsEnumerable() ?? [];
                parameters = parameters.Concat(type.Members.OfType<ConstructorDeclarationSyntax>().SelectMany(c => c.ParameterList.Parameters));
                if (parameters.Any(p => p.Type?.ToString() == "Workspace"))
                    found.Add($"{source.Name}: {type.Identifier.Text}");
            }
        }

        Assert.True(found.Count == 0, "Take IProjectSession, ISceneDocuments, IPlaySession or IScriptSession, whichever it uses:\n" + string.Join("\n", found));
    });

    [Fact]
    public void TheScriptEditorsFontSize_IsInTheSettings_AndSharedByEveryEditor()
    {
        var mutableStatics = typeof(CodeEditor).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Where(f => !f.IsLiteral && !f.IsInitOnly).Select(f => f.Name).ToList();
        Assert.True(mutableStatics.Count == 0, string.Join(", ", mutableStatics));
        Assert.Equal(CodeTheme.FontSize, new EditorSettings().CodeFontSize);
        Assert.Equal(20, System.Text.Json.JsonSerializer.Deserialize<EditorSettings>("""{"CodeFontSize":20}""")!.CodeFontSize);
    }

    private sealed record Source(string Path, string Name, bool InDiagnostics, SyntaxNode Root);

    private static IEnumerable<Source> Sources()
    {
        var editor = Path.Combine(RepoRoot(), "src", "Griff.Editor");
        foreach (var path in Directory.EnumerateFiles(editor, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(editor, path);
            if (relative.Split(Path.DirectorySeparatorChar)[0] is "bin" or "obj")
                continue;
            yield return new Source(path, Path.GetFileName(path), relative.StartsWith("Diagnostics" + Path.DirectorySeparatorChar, StringComparison.Ordinal),
                CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot());
        }
    }

    private static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "griff.slnx")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("The tests run from inside the Griff repository.");
    }
}
