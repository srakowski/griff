using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// What a component exposes is worked out once per type, in its <c>ComponentModel</c>, and everything else asks that: no
/// code outside the model looks up a component type's properties, methods or fields by reflection (a component's
/// <c>GetType()</c>, a <c>typeof</c> of one, or a descriptor's or model's <c>Type</c>), each with a filter of its own that
/// could disagree with the rest (a card once raised any public number by name, one never saved included).
/// </summary>
public sealed class ComponentReflectionTests
{
    [Fact]
    public void NoCodeOutsideTheModel_ReflectsOverAComponent()
    {
        var (core, coreTrees) = LayerTests.Core();
        var found = Reflected(core, coreTrees);
        foreach (var project in Directory.GetDirectories(Path.Combine(LayerTests.RepoRoot(), "src")).Where(p => Path.GetFileName(p) != "Griff"))
        {
            // Each project bound with the core's source, so its components resolve; what else it uses (WPF, the browser) needn't.
            var trees = LayerTests.Parse(project).ToList();
            found.AddRange(Reflected(core.AddSyntaxTrees(trees), trees));
        }

        Assert.True(found.Count == 0,
            "Code outside ComponentModel reflects over a component; ask ComponentModel.Of(type) for what it exposes " +
            "(Saved, Settable, Sent, Live, Property(name)...):\n" + string.Join("\n", found.Order()));
    }

    // The detector itself: each way of reaching a component's type is caught, inside the model and on other types isn't.
    [Fact]
    public void TheCheck_CatchesEachWayOfReachingAComponentsType()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            using System;
            namespace Griff.Scenes
            {
                public class Component { }
                public sealed class Health : Component { }
                public sealed class ComponentDescriptor { public Type Type => typeof(Health); }
                public sealed class ComponentModel { public Type Type => typeof(Health); public object A(Component c) => c.GetType().GetProperties(); }
            }

            public static class Sample
            {
                public static object A(Griff.Scenes.Component c) => c.GetType().GetProperty("Max");
                public static object B() => typeof(Griff.Scenes.Health).GetProperties();
                public static object C(Griff.Scenes.ComponentDescriptor d) => d.Type.GetMethods();
                public static object D(Griff.Scenes.Component? c) => c?.GetType().GetProperty("Max");
                public static object E(Griff.Scenes.ComponentModel m) => m.Type.GetFields();
                public static object F(object o) => o.GetType().GetProperties();
                public static object G() => typeof(string).GetProperties();
                public static object H(Griff.Scenes.Component c) => c.GetType().Name;
                public static object I() => typeof(Griff.Scenes.Component).GetProperties();
            }
            """, path: "Sample.cs");
        var compilation = CSharpCompilation.Create("Sample", [tree], LayerTests.PlatformReferences());

        Assert.Equal(["Sample.cs:12", "Sample.cs:13", "Sample.cs:14", "Sample.cs:15", "Sample.cs:16"],
            Reflected(compilation, [tree]).Select(f => f[..f.IndexOf(':', f.IndexOf(':') + 1)]).Order());
    }

    private static readonly HashSet<string> Lookups =
        ["GetProperty", "GetProperties", "GetMethod", "GetMethods", "GetField", "GetFields", "GetMember", "GetMembers", "GetEvent", "GetEvents"];

    // Each place in `trees` outside ComponentModel where a component's type has its members looked up: "file:line: code".
    private static List<string> Reflected(CSharpCompilation compilation, IEnumerable<SyntaxTree> trees)
    {
        var found = new List<string>();
        foreach (var tree in trees)
        {
            var calls = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Where(c => Called(c) is { } name && Lookups.Contains(name))
                .ToList();
            if (calls.Count == 0)
                continue;
            var model = compilation.GetSemanticModel(tree);
            foreach (var call in calls)
            {
                if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol { ContainingType: { Name: "Type", ContainingNamespace.Name: "System" } } ||
                    model.GetEnclosingSymbol(call.SpanStart)?.ContainingType is { Name: "ComponentModel" } ||
                    Receiver(call) is not { } receiver || !IsComponentsType(model, receiver))
                    continue;
                var line = call.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                found.Add($"{Path.GetFileName(tree.FilePath)}:{line}: {call}");
            }
        }

        return found;
    }

    private static string? Called(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
        _ => null,
    };

    // What the lookup is called on: `x` of `x.GetProperty(...)`, or what's before the `?.` of `c?.GetType().GetProperty(...)`.
    private static ExpressionSyntax? Receiver(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax access => access.Expression,
        MemberBindingExpressionSyntax => call.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>()?.Expression,
        _ => null,
    };

    // A component's GetType(), a typeof of a kind of component, or a descriptor's or model's Type.
    private static bool IsComponentsType(SemanticModel model, ExpressionSyntax receiver) => receiver switch
    {
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "GetType" } access } =>
            IsComponent(model.GetTypeInfo(access.Expression).Type),
        InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax { Name.Identifier.Text: "GetType" } } call =>
            call.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>() is { } c && IsComponent(model.GetTypeInfo(c.Expression).Type),
        ConditionalAccessExpressionSyntax { WhenNotNull: InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax { Name.Identifier.Text: "GetType" } } } c =>
            IsComponent(model.GetTypeInfo(c.Expression).Type),
        // A kind of component: the base class's own plumbing (what every component keeps) isn't what one exposes.
        TypeOfExpressionSyntax typeOf => model.GetTypeInfo(typeOf.Type).Type is { BaseType: { } below } && IsComponent(below),
        MemberAccessExpressionSyntax { Name.Identifier.Text: "Type" } member =>
            model.GetSymbolInfo(member).Symbol is IPropertySymbol { ContainingType.Name: "ComponentDescriptor" or "ComponentModel" },
        _ => false,
    };

    private static bool IsComponent(ITypeSymbol? type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t is { Name: "Component", ContainingNamespace: { Name: "Scenes", ContainingNamespace.Name: "Griff" } })
                return true;
        }

        return false;
    }
}
