using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Scripting;

/// <summary>
/// Puts breakpoint probes into scripts as the editor builds them: before the first statement of each line (and into each
/// expression-bodied member and lambda whose body starts a line), a test of that line's own switch, which calls
/// <see cref="Griff.Debugging.ScriptProbes.Hit"/> with the variables in scope only once a breakpoint turns it on. Probes go
/// in on the same line as the code they stand before, so lines (in errors and stack traces) stay as written. A line's
/// variables are the locals and parameters of the method, lambda or local function it's in that are certainly set there,
/// and, in a method, the object it runs on: never a variable from around a lambda, which would make it capture what it
/// didn't before. They're found from the code itself, which is quick; Roslyn's semantic analysis, which costs about as
/// much as compiling again, is asked only where the code can't say (a variable that may not be set yet), and for a probe
/// that turned out not to compile.
/// </summary>
public static class ScriptInstrumenter
{
    /// <summary>The class of switches a build with probes gets, one static bool a probe ("P0", "P1"...).</summary>
    public const string SwitchesClass = "__GriffProbes";

    private const string Hit = "global::Griff.Debugging.ScriptProbes.Hit";

    /// <summary>The lines (from 1) of <paramref name="text"/> that get a probe: where a breakpoint can go.</summary>
    public static IReadOnlyList<int> ProbeLines(string text) =>
        Sites(CSharpSyntaxTree.ParseText(text, ScriptCompiler.ParseOptions).GetRoot()).Select(s => s.Line).ToList();

    /// <summary>The line (from 1) a breakpoint asked for on <paramref name="line"/> stops at: it, or the next with code.</summary>
    public static int? ProbeLineAtOrAfter(string text, int line) => ProbeLines(text).Where(l => l >= line).Cast<int?>().FirstOrDefault();

    // A place a probe goes: a statement, or an expression body; the first that starts each line.
    private static IEnumerable<(SyntaxNode Node, int Line)> Sites(SyntaxNode root)
    {
        var taken = new HashSet<int>();
        foreach (var node in root.DescendantNodes())
        {
            if (!IsSite(node))
                continue;
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (taken.Add(line))
                yield return (node, line);
        }
    }

    private static bool IsSite(SyntaxNode node) => node switch
    {
        BlockSyntax or LocalFunctionStatementSyntax or EmptyStatementSyntax => false,
        StatementSyntax statement => statement.Parent is not GlobalStatementSyntax,
        RefExpressionSyntax => false,
        ExpressionSyntax expression => expression.Parent is ArrowExpressionClauseSyntax { Parent: not null }
            || expression.Parent is LambdaExpressionSyntax lambda && lambda.ExpressionBody == expression,
        _ => false,
    };

    // ----- Planning -----

    // A text change: at Offset, take out Length characters and put in Text. At one offset, closings (0) come before
    // openings (1), which come before probes (2).
    internal readonly record struct Edit(int Offset, int Order, string Text, int Length = 0);

    // One probe, where it goes and the changes it makes.
    internal sealed record Site(ScriptProbe Probe, SyntaxNode Node, IReadOnlyList<Edit> Edits, int FirstLine, int LastLine);

    /// <summary>Where the probes go in <paramref name="compilation"/>'s scripts (every tree but <paramref name="skip"/>), and what each passes.</summary>
    internal static Plan Analyze(CSharpCompilation compilation, string skip)
    {
        var sites = new List<Site>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (tree.FilePath == skip)
                continue;
            var model = new Lazy<SemanticModel>(() => compilation.GetSemanticModel(tree));
            foreach (var (node, line) in Sites(tree.GetRoot()))
            {
                if (Build(model, node, line, sites.Count, tree.FilePath, precise: false) is { } site)
                    sites.Add(site);
            }
        }

        return new Plan(compilation, sites);
    }

    /// <summary>
    /// Where a build's probes go: <see cref="Build"/> makes the compilation with them. A probe that doesn't compile is
    /// looked at again closely (<see cref="Refine"/>), or left out.
    /// </summary>
    internal sealed class Plan(CSharpCompilation compilation, List<Site> sites)
    {
        public int Count => sites.Count;

        /// <summary>The compilation with the probes in (but those numbered in <paramref name="leaveOut"/>), its switches' class and their table.</summary>
        public (CSharpCompilation Compilation, ProbeTable Probes) Build(IReadOnlySet<int>? leaveOut = null)
        {
            var kept = sites.Where(s => leaveOut?.Contains(s.Probe.Id) != true).ToList();
            var result = compilation;
            foreach (var tree in compilation.SyntaxTrees)
            {
                var edits = kept.Where(s => s.Probe.Path == tree.FilePath).SelectMany(s => s.Edits).ToList();
                if (edits.Count == 0)
                    continue;
                var text = Apply(tree.GetText().ToString(), edits);
                result = result.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8));
            }

            var switches = new StringBuilder($"internal static class {SwitchesClass} {{");
            foreach (var site in kept)
                switches.Append(CultureInfo.InvariantCulture, $" public static bool P{site.Probe.Id};");
            switches.Append(" }");
            result = result.AddSyntaxTrees(CSharpSyntaxTree.ParseText(switches.ToString(), compilation.SyntaxTrees.First().Options as CSharpParseOptions,
                "<griff probes>", Encoding.UTF8));
            return (result, new ProbeTable(kept.Select(s => s.Probe).ToList()));
        }

        /// <summary>The probes an error at <paramref name="line"/> of <paramref name="path"/> could come from: those starting there, or else the innermost around it.</summary>
        public IEnumerable<int> Around(string path, int line)
        {
            var here = sites.Where(s => s.Probe.Path == path && s.FirstLine <= line && line <= s.LastLine).ToList();
            var starting = here.Where(s => s.FirstLine == line || s.LastLine == line).ToList();
            return (starting.Count > 0 ? starting : here.OrderByDescending(s => s.FirstLine).Take(1)).Select(s => s.Probe.Id);
        }

        /// <summary>Works out the probes numbered <paramref name="ids"/> again with Roslyn's semantic analysis.</summary>
        public void Refine(IEnumerable<int> ids)
        {
            var models = new Dictionary<SyntaxTree, Lazy<SemanticModel>>();
            foreach (var id in ids)
            {
                var index = sites.FindIndex(s => s.Probe.Id == id);
                if (index < 0)
                    continue;
                var site = sites[index];
                var tree = site.Node.SyntaxTree;
                if (!models.TryGetValue(tree, out var model))
                    models[tree] = model = new Lazy<SemanticModel>(() => compilation.GetSemanticModel(tree));
                if (ScriptInstrumenter.Build(model, site.Node, site.Probe.Line, id, site.Probe.Path, precise: true) is { } refined)
                    sites[index] = refined;
            }
        }
    }

    private static string Apply(string text, IEnumerable<Edit> edits)
    {
        var builder = new StringBuilder(text.Length + 1024);
        var at = 0;
        foreach (var edit in edits.OrderBy(e => e.Offset).ThenBy(e => e.Order))
        {
            if (edit.Offset < at)
                continue;
            builder.Append(text, at, edit.Offset - at).Append(edit.Text);
            at = edit.Offset + edit.Length;
        }

        return builder.Append(text, at, text.Length - at).ToString();
    }

    // `precise` asks Roslyn about every variable's type and whether it's set, rather than reading the code.
    private static Site? Build(Lazy<SemanticModel> model, SyntaxNode node, int line, int id, string path, bool precise)
    {
        try
        {
            return node switch
            {
                StatementSyntax statement => Statement(model, statement, line, id, path, precise),
                ExpressionSyntax { Parent: ArrowExpressionClauseSyntax arrow } body => Arrow(model, arrow, body, line, id, path, precise),
                ExpressionSyntax { Parent: LambdaExpressionSyntax lambda } body => Lambda(model.Value, lambda, body, line, id, path),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Roslyn can't analyze this one (broken code, say): it goes without.
            return null;
        }
    }

    private static Site Statement(Lazy<SemanticModel> model, StatementSyntax statement, int line, int id, string path, bool precise)
    {
        var function = FunctionOf(statement);
        List<string> names = function is null ? [] : InScope(model, statement, function, precise).Distinct().ToList();
        var probe = new ScriptProbe(id, path, line, Where(statement), names, function is not null && CanPassThis(function))
        {
            Function = function?.SpanStart ?? -1,
            Entry = function is not null && IsEntry(statement, function),
        };
        var call = Call(probe);
        var edits = statement.Parent is BlockSyntax or SwitchSectionSyntax
            ? new[] { new Edit(statement.SpanStart, 2, call + " ") }
            // An if's or a loop's statement on a line of its own goes in a block, with its probe.
            : [new Edit(statement.SpanStart, 1, "{ "), new Edit(statement.SpanStart, 2, call + " "), new Edit(statement.Span.End, 0, " }")];
        return new Site(probe, statement, edits, line, LineOf(statement, statement.Span.End));
    }

    // The parameters and locals of `function` in scope and certainly set as `statement` starts, in the order they were
    // declared. A local declared with a value (or by a loop, a using or a catch) is set after its declaration; the rest
    // (declared without one, out parameters, variables a pattern or an `out var` declares) ask Roslyn's flow analysis.
    private static IEnumerable<string> InScope(Lazy<SemanticModel> model, StatementSyntax statement, SyntaxNode function, bool precise)
    {
        var set = new List<(int At, string Name)>();
        var maybe = new List<(int At, SyntaxNode Declared)>();
        foreach (var (parameter, name, isOut) in ParametersOf(function))
        {
            if (isOut)
                maybe.Add((-1, parameter));
            else if (!precise || Passable(model.Value.GetDeclaredSymbol(parameter)))
                set.Add((-1, name));
        }

        void Local(SyntaxNode declared, string name, TypeSyntax? type, ExpressionSyntax? value, bool certain)
        {
            if (!certain)
                maybe.Add((declared.SpanStart, declared));
            else if (precise ? Passable(model.Value.GetDeclaredSymbol(declared)) : Passable(type, value))
                set.Add((declared.SpanStart, name));
        }

        // Variables declared by patterns and `out var` in an expression (not in a lambda or a switch arm inside it).
        void Designations(SyntaxNode? expression)
        {
            if (expression is null)
                return;
            foreach (var designation in expression.DescendantNodes(n => n is not (AnonymousFunctionExpressionSyntax or SwitchExpressionArmSyntax or QueryExpressionSyntax))
                         .OfType<SingleVariableDesignationSyntax>())
                maybe.Add((designation.SpanStart, designation));
        }

        // What a statement before this one in its block declares for the rest of the block.
        void Before(StatementSyntax earlier)
        {
            switch (earlier)
            {
                case LocalDeclarationStatementSyntax declaration:
                    foreach (var variable in declaration.Declaration.Variables)
                    {
                        Local(variable, variable.Identifier.ValueText, declaration.Declaration.Type, variable.Initializer?.Value, certain: variable.Initializer is not null);
                        Designations(variable.Initializer);
                    }

                    break;
                case ExpressionStatementSyntax expression:
                    Designations(expression.Expression);
                    break;
                case IfStatementSyntax ifStatement:
                    Designations(ifStatement.Condition);
                    break;
                case LabeledStatementSyntax labeled:
                    Before(labeled.Statement);
                    break;
            }
        }

        SyntaxNode node = statement;
        for (var parent = statement.Parent; parent is not null && parent != function; node = parent, parent = parent.Parent)
        {
            switch (parent)
            {
                case BlockSyntax block:
                    foreach (var earlier in block.Statements.TakeWhile(s => s != node))
                        Before(earlier);
                    break;
                case SwitchSectionSyntax section:
                    foreach (var earlier in section.Statements.TakeWhile(s => s != node))
                        Before(earlier);
                    foreach (var label in section.Labels.OfType<CasePatternSwitchLabelSyntax>())
                        Designations(label);
                    break;
                case ForStatementSyntax loop when node == loop.Statement:
                    foreach (var variable in loop.Declaration?.Variables ?? default)
                        Local(variable, variable.Identifier.ValueText, loop.Declaration!.Type, variable.Initializer?.Value, certain: variable.Initializer is not null);
                    Designations(loop.Condition);
                    break;
                case ForEachStatementSyntax loop when node == loop.Statement:
                    Local(loop, loop.Identifier.ValueText, loop.Type, null, certain: true);
                    break;
                case ForEachVariableStatementSyntax loop when node == loop.Statement:
                    Designations(loop.Variable);
                    break;
                case UsingStatementSyntax usingStatement when node == usingStatement.Statement:
                    foreach (var variable in usingStatement.Declaration?.Variables ?? default)
                        Local(variable, variable.Identifier.ValueText, usingStatement.Declaration!.Type, variable.Initializer?.Value, certain: true);
                    Designations(usingStatement.Expression);
                    break;
                case CatchClauseSyntax { Declaration: { } caught } when caught.Identifier.ValueText.Length > 0:
                    Local(caught, caught.Identifier.ValueText, caught.Type, null, certain: true);
                    break;
                case IfStatementSyntax ifStatement:
                    Designations(ifStatement.Condition);
                    break;
                case ElseClauseSyntax { Parent: IfStatementSyntax ifStatement }:
                    Designations(ifStatement.Condition);
                    break;
                case WhileStatementSyntax loop:
                    Designations(loop.Condition);
                    break;
                case LockStatementSyntax lockStatement:
                    Designations(lockStatement.Expression);
                    break;
                case SwitchStatementSyntax switchStatement:
                    Designations(switchStatement.Expression);
                    break;
            }
        }

        if (maybe.Count > 0 && model.Value.AnalyzeDataFlow(statement) is { Succeeded: true } flow)
        {
            foreach (var (at, declared) in maybe)
            {
                if (model.Value.GetDeclaredSymbol(declared) is { } symbol && Passable(symbol)
                    && flow.DefinitelyAssignedOnEntry.Contains(symbol, SymbolEqualityComparer.Default))
                    set.Add((at, symbol.Name));
            }
        }

        return set.Where(s => s.At < statement.SpanStart).OrderBy(s => s.At).Select(s => s.Name);
    }

    // `=> body;` of a method, local function, operator, constructor, accessor, property or indexer becomes a block.
    private static Site? Arrow(Lazy<SemanticModel> model, ArrowExpressionClauseSyntax arrow, ExpressionSyntax body, int line, int id, string path, bool precise)
    {
        var member = arrow.Parent!;
        var (semicolon, returns) = member switch
        {
            MethodDeclarationSyntax m => (m.SemicolonToken, Returns(m.ReturnType, m.Modifiers)),
            LocalFunctionStatementSyntax f => (f.SemicolonToken, Returns(f.ReturnType, f.Modifiers)),
            OperatorDeclarationSyntax o => (o.SemicolonToken, Returns(o.ReturnType, o.Modifiers)),
            ConversionOperatorDeclarationSyntax c => (c.SemicolonToken, Returns(c.Type, c.Modifiers)),
            BaseMethodDeclarationSyntax m => (m.SemicolonToken, false),
            AccessorDeclarationSyntax a => (a.SemicolonToken, a.Keyword.IsKind(SyntaxKind.GetKeyword)
                ? Returns(a.Parent?.Parent is BasePropertyDeclarationSyntax owner ? owner.Type : null, default) : false),
            PropertyDeclarationSyntax p => (p.SemicolonToken, Returns(p.Type, default)),
            IndexerDeclarationSyntax i => (i.SemicolonToken, Returns(i.Type, default)),
            _ => (default(SyntaxToken), (bool?)null),
        };
        if (returns is null || semicolon.IsKind(SyntaxKind.None) || semicolon.IsMissing)
            return null;

        var self = member is not LocalFunctionStatementSyntax && CanPassThis(member);
        var names = ParametersOf(member)
            .Where(p => !p.IsOut && (!precise || Passable(model.Value.GetDeclaredSymbol(p.Node))))
            .Select(p => p.Name).ToList();
        var probe = new ScriptProbe(id, path, line, Where(body), names, self) { Function = member.SpanStart, Entry = true };
        var call = Call(probe);
        var getter = member is PropertyDeclarationSyntax or IndexerDeclarationSyntax;
        var open = (getter ? "{ get { " : "{ ") + call + (returns == false || body is ThrowExpressionSyntax ? " " : " return ");
        return new Site(probe, body, [new Edit(arrow.ArrowToken.SpanStart, 1, open, arrow.ArrowToken.Span.Length), new Edit(semicolon.Span.End, 0, getter ? " } }" : " }")],
            line, LineOf(arrow, semicolon.Span.End));
    }

    // Whether a member of this type returns a value from its body (true), returns nothing (false), or returns by ref, which
    // gets no probe (null).
    private static bool? Returns(TypeSyntax? type, SyntaxTokenList modifiers) => type switch
    {
        null or RefTypeSyntax => null,
        PredefinedTypeSyntax predefined when predefined.Keyword.IsKind(SyntaxKind.VoidKeyword) => false,
        _ when modifiers.Any(SyntaxKind.AsyncKeyword) && RightmostName(type) is IdentifierNameSyntax { Identifier.ValueText: "Task" or "ValueTask" } => false,
        _ => true,
    };

    // `x => body` becomes `x => { probe; return body; }` (or without the return, when it returns nothing).
    private static Site? Lambda(SemanticModel model, LambdaExpressionSyntax lambda, ExpressionSyntax body, int line, int id, string path)
    {
        if (model.GetSymbolInfo(lambda).Symbol is not IMethodSymbol symbol || symbol.ReturnsByRef || symbol.ReturnsByRefReadonly)
            return null;
        // A lambda made into an expression tree can't hold statements.
        if (model.GetTypeInfo(lambda).ConvertedType is INamedTypeSymbol { Name: "Expression", ContainingNamespace: { } ns } && ns.ToDisplayString() == "System.Linq.Expressions")
            return null;

        var names = symbol.Parameters.Where(p => !p.IsDiscard && p.Name is not ("" or "_") && p.RefKind != RefKind.Out && Passable(p)).Select(p => p.Name).ToList();
        var probe = new ScriptProbe(id, path, line, Where(body), names, false) { Function = lambda.SpanStart, Entry = true };
        var nothing = symbol.ReturnsVoid || symbol.IsAsync && symbol.ReturnType is INamedTypeSymbol { Arity: 0 } || body is ThrowExpressionSyntax;
        var open = "{ " + Call(probe) + (nothing ? " " : " return ");
        return new Site(probe, body, [new Edit(body.SpanStart, 2, open), new Edit(body.Span.End, 0, "; }")], line, LineOf(body, body.Span.End));
    }

    private static string Call(ScriptProbe probe)
    {
        var id = probe.Id.ToString(CultureInfo.InvariantCulture);
        var values = string.Join(", ", probe.Names.Select(n => "@" + n));
        return $"if (global::{SwitchesClass}.P{id}) {Hit}(typeof(global::{SwitchesClass}), {id}, {(probe.HasThis ? "this" : "null")}, new object[] {{ {values} }});";
    }

    private static int LineOf(SyntaxNode node, int offset) =>
        node.SyntaxTree.GetLineSpan(new Microsoft.CodeAnalysis.Text.TextSpan(offset, 0)).StartLinePosition.Line + 1;

    // ----- Reading the code -----

    // The method, accessor, lambda or local function a statement is in.
    private static SyntaxNode? FunctionOf(SyntaxNode node)
    {
        for (var at = node.Parent; at is not null; at = at.Parent)
        {
            switch (at)
            {
                case AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax or AccessorDeclarationSyntax:
                    return at;
                case MemberDeclarationSyntax:
                    return null;
            }
        }

        return null;
    }

    // Whether `statement` is the first its function runs each time it's called: the first statement of its body (a local
    // function declared there isn't run).
    private static bool IsEntry(StatementSyntax statement, SyntaxNode function)
    {
        var body = function switch
        {
            BaseMethodDeclarationSyntax method => method.Body,
            AccessorDeclarationSyntax accessor => accessor.Body,
            LocalFunctionStatementSyntax local => local.Body,
            AnonymousFunctionExpressionSyntax anonymous => anonymous.Block,
            _ => null,
        };
        return body is not null && statement.Parent == body && body.Statements.FirstOrDefault(s => s is not LocalFunctionStatementSyntax) == statement;
    }

    // A function's parameters that can be passed (a setter's `value` stands as the accessor itself), and whether each is an
    // out parameter (not set at the start).
    private static IEnumerable<(SyntaxNode Node, string Name, bool IsOut)> ParametersOf(SyntaxNode function)
    {
        var list = function switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList.Parameters,
            LocalFunctionStatementSyntax local => local.ParameterList.Parameters,
            ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters,
            SimpleLambdaExpressionSyntax lambda => SyntaxFactory.SingletonSeparatedList(lambda.Parameter),
            AnonymousMethodExpressionSyntax anonymous => anonymous.ParameterList?.Parameters ?? default,
            AccessorDeclarationSyntax { Parent.Parent: IndexerDeclarationSyntax indexer } => indexer.ParameterList.Parameters,
            IndexerDeclarationSyntax indexer => indexer.ParameterList.Parameters,
            _ => default,
        };

        // A parameter called _ is one the code doesn't use (or a discard, which can't be read).
        foreach (var parameter in list)
        {
            var name = parameter.Identifier.ValueText;
            if (name is "" or "_" || parameter.Modifiers.Any(SyntaxKind.ScopedKeyword) || !Passable(parameter.Type, null))
                continue;
            yield return (parameter, name, parameter.Modifiers.Any(SyntaxKind.OutKeyword));
        }

        if (function is AccessorDeclarationSyntax accessor && !accessor.Keyword.IsKind(SyntaxKind.GetKeyword)
            && accessor.Parent?.Parent is BasePropertyDeclarationSyntax owner && Passable(owner.Type, null))
            yield return (accessor, "value", false);
    }

    // Whether `this` can be passed: in an instance method, constructor or accessor of a class or struct (not a ref struct).
    private static bool CanPassThis(SyntaxNode function)
    {
        SyntaxTokenList? modifiers = function switch
        {
            OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax => null,
            BaseMethodDeclarationSyntax method => method.Modifiers,
            AccessorDeclarationSyntax { Parent.Parent: MemberDeclarationSyntax owner } => owner.Modifiers,
            BasePropertyDeclarationSyntax property => property.Modifiers,
            _ => null,
        };
        if (modifiers is not { } list || list.Any(SyntaxKind.StaticKeyword))
            return false;
        return function.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault() switch
        {
            StructDeclarationSyntax structure => !structure.Modifiers.Any(SyntaxKind.RefKeyword),
            RecordDeclarationSyntax record => !record.Modifiers.Any(SyntaxKind.RefKeyword),
            ClassDeclarationSyntax or InterfaceDeclarationSyntax => true,
            _ => false,
        };
    }

    // What can be handed over as an object: not a ref struct (a Span) or a pointer. From the code: a declared type, or for
    // `var`, what it's set to.
    private static bool Passable(TypeSyntax? type, ExpressionSyntax? value) => type switch
    {
        null => true,
        PointerTypeSyntax or FunctionPointerTypeSyntax or ScopedTypeSyntax => false,
        RefTypeSyntax reference => Passable(reference.Type, value),
        IdentifierNameSyntax { IsVar: true } => value is not (StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax),
        _ => RightmostName(type) is not GenericNameSyntax { Identifier.ValueText: "Span" or "ReadOnlySpan" },
    };

    private static bool Passable(ISymbol? symbol) => ((symbol as ILocalSymbol)?.Type ?? (symbol as IParameterSymbol)?.Type) switch
    {
        null or IErrorTypeSymbol => false,
        { IsRefLikeType: true } => false,
        { TypeKind: TypeKind.Pointer or TypeKind.FunctionPointer } => false,
        _ => true,
    };

    private static SimpleNameSyntax? RightmostName(TypeSyntax? type) => type switch
    {
        SimpleNameSyntax name => name,
        QualifiedNameSyntax qualified => qualified.Right,
        AliasQualifiedNameSyntax alias => alias.Name,
        NullableTypeSyntax nullable => RightmostName(nullable.ElementType),
        _ => null,
    };

    // "Player.Update", "Player.Speed", "Player constructor", "Player.Update (in a lambda)", "Player.Update > Helper".
    private static string Where(SyntaxNode node)
    {
        var inner = "";
        foreach (var at in node.Ancestors())
        {
            switch (at)
            {
                case AnonymousFunctionExpressionSyntax when inner.Length == 0:
                    inner = " (in a lambda)";
                    break;
                case LocalFunctionStatementSyntax local when inner.Length == 0:
                    inner = $" > {local.Identifier.ValueText}";
                    break;
                case MemberDeclarationSyntax member and not BaseTypeDeclarationSyntax:
                    var type = member.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "";
                    var name = member switch
                    {
                        MethodDeclarationSyntax method => method.Identifier.ValueText,
                        PropertyDeclarationSyntax property => property.Identifier.ValueText,
                        EventDeclarationSyntax e => e.Identifier.ValueText,
                        IndexerDeclarationSyntax => "this[]",
                        OperatorDeclarationSyntax o => $"operator {o.OperatorToken.ValueText}",
                        ConversionOperatorDeclarationSyntax c => $"operator {c.Type}",
                        DestructorDeclarationSyntax => $"~{type}",
                        _ => null,
                    };
                    return (name is null ? $"{type} constructor" : type.Length == 0 ? name : $"{type}.{name}") + inner;
            }
        }

        return inner.TrimStart();
    }
}
