using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Editor.Tests;

/// <summary>
/// Guards the other half of the agent contract (see CLAUDE.md): a capability that only exists in a view's code (a click or
/// key handler) is invisible to agents, so no view runs an edit or starts a process, and no key handler undoes, redoes or
/// reaches a history; it calls a command instead (Ctrl+Z is Edit &gt; Undo's, which goes to whichever document is active).
/// A view is any class of the editor's that is a WPF element (a window, a panel's view, a control), found by its base
/// type, whatever its file is called. The list of exceptions below is empty and should stay so: a view that does any of
/// it fails.
/// </summary>
public sealed class CodeBehindTests
{
    // "<file>: <member> runs an edit", "<file>: <member> starts a process" or "<file>: <member> edits from a key"
    private static readonly string[] Allowed = [];

    [Fact]
    public void CodeBehind_RunsNoEdits_AndStartsNoProcesses()
    {
        var found = Violations().ToHashSet();

        var added = found.Except(Allowed).Order().ToList();
        var gone = Allowed.Except(found).Order().ToList();

        Assert.True(added.Count == 0,
            "Views must not run edits, start processes or undo from their key handlers, where agents can't reach them. Make " +
            "it an EditorCommand (or a view model method) and call that from the handler; Ctrl+Z and Ctrl+Y are Edit > Undo " +
            "and Redo's:\n" + string.Join("\n", added));
        Assert.True(gone.Count == 0, "Fixed, so take these off CodeBehindTests.Allowed:\n" + string.Join("\n", gone));
    }

    [Fact]
    public void Views_AreFoundByTheirBaseType_NotTheirFileName()
    {
        var files = Views().Select(v => Path.GetFileName(v.Path)).ToHashSet();
        // Windows and panels' views with XAML, and elements written in code alone (the script editor among them).
        Assert.Contains("MainWindow.xaml.cs", files);
        Assert.Contains("CodeEditor.cs", files);
        Assert.True(files.Count > 30, $"Only {files.Count} files hold views.");
    }

    // A key handler that undoes or reaches a history is doing what Edit > Undo is for.
    [Fact]
    public void AKeyHandlerThatUndoes_IsCaught()
    {
        const string view = """
            namespace Griff.Editor.Features.Art;
            public partial class PainterWindow
            {
                private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
                {
                    if (e.Key == Key.Z)
                        Model.Undo();
                    else if (e.Key == Key.Y)
                        Model.History.Redo();
                }

                private void OnClick(object sender, RoutedEventArgs e) => Model.Undo();
            }
            """;
        var root = CSharpSyntaxTree.ParseText(view).GetRoot();
        var found = Check("PainterWindow.cs", root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single()).ToList();
        Assert.Equal(["PainterWindow.cs: OnKeyDown edits from a key", "PainterWindow.cs: OnKeyDown edits from a key", "PainterWindow.cs: OnKeyDown edits from a key"], found);
    }

    private static IEnumerable<string> Violations() =>
        Views().SelectMany(v => Check(Path.GetFileName(v.Path), v.Class));

    // Every class in the editor's source that is a WPF element (by its type in the editor's assembly), with its file.
    private static IEnumerable<(string Path, ClassDeclarationSyntax Class)> Views()
    {
        var elements = typeof(App).Assembly.GetTypes()
            .Where(t => typeof(System.Windows.UIElement).IsAssignableFrom(t) && t.FullName is not null)
            .Select(t => t.FullName!)
            .ToHashSet();
        var editor = Path.Combine(RepoRoot(), "src", "Griff.Editor");
        foreach (var path in Directory.EnumerateFiles(editor, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetRelativePath(editor, path).Split(Path.DirectorySeparatorChar)[0] is "bin" or "obj")
                continue;
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
            foreach (var type in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (elements.Contains(FullName(type)))
                    yield return (path, type);
            }
        }
    }

    // "Griff.Editor.Shell.MainWindow", or "Outer+Inner" for a nested class, as reflection names it.
    private static string FullName(ClassDeclarationSyntax type)
    {
        var name = type.Identifier.Text + (type.TypeParameterList is { Parameters.Count: > 0 } list ? $"`{list.Parameters.Count}" : "");
        for (var outer = type.Parent; outer is not null; outer = outer.Parent)
        {
            if (outer is TypeDeclarationSyntax container)
                name = container.Identifier.Text + "+" + name;
            else if (outer is BaseNamespaceDeclarationSyntax ns)
                name = ns.Name + "." + name;
        }

        return name;
    }

    private static IEnumerable<string> Check(string file, ClassDeclarationSyntax view)
    {
        foreach (var node in view.DescendantNodes())
        {
            // A nested class is checked as a view of its own, if it is one.
            if (node.Ancestors().OfType<ClassDeclarationSyntax>().First() != view)
                continue;
            var what = node switch
            {
                // Process.Start(...)
                MemberAccessExpressionSyntax { Expression: var target, Name.Identifier.Text: "Start" }
                    when target.ToString() is "Process" or "System.Diagnostics.Process" => "starts a process",
                // new SomethingEdit(...): an IEdit built to be run
                BaseObjectCreationExpressionSyntax creation when CreatesEdit(creation) => "runs an edit",
                // document.Execute(edit) or history.BeginGroup(...); a command's Execute is what a handler should call (an
                // EditorCommand, or one of WPF's: ApplicationCommands.Copy, EditingCommands.Delete)
                InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Execute" or "BeginGroup" } call }
                    when !IsCommand(call.Expression) => "runs an edit",
                // In a key handler: x.Undo(), x.Redo() or x.History, which Ctrl+Z and Ctrl+Y through Edit > Undo and Redo are for.
                InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Undo" or "Redo" } } when InKeyHandler(node) => "edits from a key",
                MemberAccessExpressionSyntax { Name.Identifier.Text: "History" } when InKeyHandler(node) => "edits from a key",
                _ => null,
            };
            if (what is not null)
                yield return $"{file}: {MemberOf(node)} {what}";
        }
    }

    // In a method, local function or lambda that takes the keyboard's event (a key down or up).
    private static bool InKeyHandler(SyntaxNode node) => node.Ancestors().Any(a => a switch
    {
        BaseMethodDeclarationSyntax method => method.ParameterList.Parameters.Any(IsKeyArgs),
        ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters.Any(IsKeyArgs),
        LocalFunctionStatementSyntax local => local.ParameterList.Parameters.Any(IsKeyArgs),
        _ => false,
    });

    private static bool IsKeyArgs(ParameterSyntax parameter) => parameter.Type is { } type && TypeName(type) is "KeyEventArgs";

    private static bool IsCommand(ExpressionSyntax target) =>
        target.ToString().EndsWith("Command", StringComparison.Ordinal)
        || target is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: var holder } } && holder.EndsWith("Commands", StringComparison.Ordinal);

    private static bool CreatesEdit(BaseObjectCreationExpressionSyntax creation) => creation switch
    {
        ObjectCreationExpressionSyntax { Type: var type } => TypeName(type).EndsWith("Edit", StringComparison.Ordinal),
        _ => false,
    };

    private static string TypeName(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
        SimpleNameSyntax simple => simple.Identifier.Text,
        _ => type.ToString(),
    };

    private static string MemberOf(SyntaxNode node) => node.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault() switch
    {
        MethodDeclarationSyntax method => method.Identifier.Text,
        PropertyDeclarationSyntax property => property.Identifier.Text,
        ConstructorDeclarationSyntax => "constructor",
        _ => "(field)",
    };

    private static string RepoRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "griff.slnx")))
            folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("The tests run from inside the Griff repository.");
    }
}
