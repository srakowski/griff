using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Editor.Tests;

/// <summary>
/// Guards the other half of the agent contract (see CLAUDE.md): a capability that only exists in a code-behind click
/// handler is invisible to agents, so no *.xaml.cs runs an edit or starts a process; it calls a command instead. The
/// list of exceptions below is empty and should stay so: a handler that does either fails.
/// </summary>
public sealed class CodeBehindTests
{
    // "<file>: <member> runs an edit" or "<file>: <member> starts a process"
    private static readonly string[] Allowed = [];

    [Fact]
    public void CodeBehind_RunsNoEdits_AndStartsNoProcesses()
    {
        var found = Violations().ToHashSet();

        var added = found.Except(Allowed).Order().ToList();
        var gone = Allowed.Except(found).Order().ToList();

        Assert.True(added.Count == 0,
            "Code-behind must not run edits or start processes, where agents can't reach them. Make it an " +
            "EditorCommand (or a view model method) and call that from the handler:\n" + string.Join("\n", added));
        Assert.True(gone.Count == 0, "Fixed, so take these off CodeBehindTests.Allowed:\n" + string.Join("\n", gone));
    }

    private static IEnumerable<string> Violations()
    {
        var editor = Path.Combine(RepoRoot(), "src", "Griff.Editor");
        foreach (var path in Directory.EnumerateFiles(editor, "*.xaml.cs", SearchOption.AllDirectories))
        {
            if (Path.GetRelativePath(editor, path).Split(Path.DirectorySeparatorChar)[0] is "bin" or "obj")
                continue;

            var file = Path.GetFileName(path);
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path).GetRoot();
            foreach (var node in root.DescendantNodes())
            {
                var what = node switch
                {
                    // Process.Start(...)
                    MemberAccessExpressionSyntax { Expression: var target, Name.Identifier.Text: "Start" }
                        when target.ToString() is "Process" or "System.Diagnostics.Process" => "starts a process",
                    // new SomethingEdit(...): an IEdit built to be run
                    BaseObjectCreationExpressionSyntax creation when CreatesEdit(creation) => "runs an edit",
                    // document.Execute(edit) or history.BeginGroup(...); a command's Execute is what a handler should call
                    InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Execute" or "BeginGroup" } call }
                        when !call.Expression.ToString().EndsWith("Command", StringComparison.Ordinal) => "runs an edit",
                    _ => null,
                };
                if (what is not null)
                    yield return $"{file}: {MemberOf(node)} {what}";
            }
        }
    }

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
