using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards against reaching a type by its name in a string: code outside a module that compares a type's name with a
/// string ("Hotspot") names that type all the same, but where the layer and module rules can't see it, and breaks
/// silently when it's renamed. A type is reached through what it is instead: an interface it implements, an attribute it
/// carries (as KnownNames finds the places things arrive at, an adventure's hotspots, by the IArrivalSpot they implement).
/// Comparing with a name someone gave (a component an agent names) is fine; comparing with a name written in the code isn't.
/// </summary>
public sealed class TypeNameTests
{
    [Fact]
    public void NoCodeOutsideAModule_ComparesATypesNameWithAString()
    {
        var (core, coreTrees) = LayerTests.Core();
        var found = Compared(core, coreTrees.Where(t => t.FilePath.Length > 0 && !LayerTests.IsModuleFile(t.FilePath)));
        foreach (var (_, trees) in LayerTests.OtherProjects().Where(p => !p.Name.StartsWith("Modules/", StringComparison.Ordinal)))
        {
            // Each project bound with the engine's source, so its names resolve; what else it uses (WPF, the browser) needn't.
            found.AddRange(Compared(core.AddSyntaxTrees(trees), trees));
        }

        Assert.True(found.Count == 0,
            "Code outside a module compares a type's name with a string; reach the type through an interface or attribute " +
            "it has instead:\n" + string.Join("\n", found.Order()));
    }

    // The detector itself: each way of comparing a type's name with a string is caught, and comparing with a variable isn't.
    [Fact]
    public void TheCheck_CatchesEachWayOfComparing()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            using System;
            public static class Sample
            {
                public static bool A(object o) => o.GetType().Name == "Hotspot";
                public static bool B(Type t) => "Hotspot" != t.FullName;
                public static bool C(object o) => o.GetType().Name.Equals("Hotspot", StringComparison.Ordinal);
                public static bool D(Type t) => t.Name is "Hotspot";
                public static bool E(Type t) => string.Equals(t.Name, "Hotspot");
                public static bool F(object o, string wanted) => o.GetType().Name == wanted;
                public static bool G(Type t) => t.Name.EndsWith("Template", StringComparison.Ordinal);
            }
            """, path: "Sample.cs");
        var compilation = CSharpCompilation.Create("Sample", [tree], LayerTests.PlatformReferences());

        Assert.Equal(["Sample.cs:4", "Sample.cs:5", "Sample.cs:6", "Sample.cs:7", "Sample.cs:8"],
            Compared(compilation, [tree]).Select(f => f[..f.IndexOf(':', f.IndexOf(':') + 1)]).Order());
    }

    // Each place in `trees` where a type's Name or FullName is compared with a string written in the code: "file:line: code".
    private static List<string> Compared(CSharpCompilation compilation, IEnumerable<SyntaxTree> trees)
    {
        var found = new List<string>();
        foreach (var tree in trees)
        {
            var names = tree.GetRoot().DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Where(m => m.Name.Identifier.Text is "Name" or "FullName")
                .ToList();
            if (names.Count == 0)
                continue;
            var model = compilation.GetSemanticModel(tree);
            foreach (var name in names)
            {
                if (!IsTypesName(model, name) || ComparedWith(model, name) is not { } comparison)
                    continue;
                var line = comparison.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                found.Add($"{Path.GetFileName(tree.FilePath)}:{line}: {comparison}");
            }
        }

        return found;
    }

    // A type's name: Type.Name or Type.FullName (or MemberInfo.Name, on a type), or anything's .Name on GetType() or typeof.
    private static bool IsTypesName(SemanticModel model, MemberAccessExpressionSyntax name)
    {
        if (name.Expression is TypeOfExpressionSyntax ||
            name.Expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "GetType" } or IdentifierNameSyntax { Identifier.Text: "GetType" } })
            return true;
        return model.GetSymbolInfo(name).Symbol is IPropertySymbol { ContainingType: var owner } &&
               owner.ToDisplayString() is "System.Type" or "System.Reflection.MemberInfo" or "System.Reflection.TypeInfo" &&
               model.GetTypeInfo(name.Expression).Type?.ToDisplayString() is "System.Type" or "System.Reflection.TypeInfo";
    }

    // The comparison `name` is in with a constant string, if it is in one.
    private static SyntaxNode? ComparedWith(SemanticModel model, ExpressionSyntax name)
    {
        switch (name.Parent)
        {
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression):
                return IsConstantString(model, binary.Left == name ? binary.Right : binary.Left) ? binary : null;
            case IsPatternExpressionSyntax pattern:
                return pattern.Pattern.DescendantNodesAndSelf().OfType<ConstantPatternSyntax>().Any(c => IsConstantString(model, c.Expression)) ? pattern : null;
            case MemberAccessExpressionSyntax { Name.Identifier.Text: "Equals", Parent: InvocationExpressionSyntax call } access when access.Expression == name:
                return call.ArgumentList.Arguments.Count > 0 && IsConstantString(model, call.ArgumentList.Arguments[0].Expression) ? call : null;
            case ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } list }
                when call.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Equals" }:
                return list.Arguments.Any(a => a.Expression != name && IsConstantString(model, a.Expression)) ? call : null;
            case SwitchStatementSyntax or SwitchExpressionSyntax:
                return name.Parent.DescendantNodes().OfType<CaseSwitchLabelSyntax>().Any(c => IsConstantString(model, c.Value)) ||
                       name.Parent.DescendantNodes().OfType<ConstantPatternSyntax>().Any(c => IsConstantString(model, c.Expression))
                    ? name.Parent
                    : null;
            default:
                return null;
        }
    }

    private static bool IsConstantString(SemanticModel model, ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } ||
        model.GetConstantValue(expression) is { HasValue: true, Value: string };
}
