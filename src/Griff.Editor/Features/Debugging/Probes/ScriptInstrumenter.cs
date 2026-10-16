using System.Globalization;
using System.Text;
using Griff.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Editor.Features.Debugging.Probes;

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
/// <para>
/// It also puts in guards: each stretch of a method's code goes in a <c>try</c> whose filter notes an exception coming up
/// through it, with the variables in scope there (<see cref="Griff.Debugging.ScriptProbes.Threw"/>), and never catches it,
/// so the editor can pause at the line that threw and show them. A guard costs nothing until something is thrown.
/// </para>
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
    // openings (1), which come before probes (2); among closings, the one of the innermost thing first (the lowest Rank).
    internal readonly record struct Edit(int Offset, int Order, string Text, int Length = 0, int Rank = 0);

    // One probe or guard (numbered from the same count), where it goes and the changes it makes. A probe of an expression
    // body or a lambda is a guard too, of the same number. A guard of its own can be made again with Roslyn's help (Remake).
    internal sealed record Site(int Id, string Path, ScriptProbe? Probe, SyntaxNode Node, IReadOnlyList<Edit> Edits, int FirstLine, int LastLine)
    {
        public ScriptGuard? Guard { get; init; }

        public Func<Lazy<SemanticModel>, Site?>? Remake { get; init; }
    }

    /// <summary>
    /// Where the probes and guards go in <paramref name="compilation"/>'s scripts (every tree but <paramref name="skip"/>),
    /// and what each passes.
    /// </summary>
    internal static Plan Analyze(CSharpCompilation compilation, string skip)
    {
        var sites = new List<Site>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (tree.FilePath == skip)
                continue;
            var model = new Lazy<SemanticModel>(() => compilation.GetSemanticModel(tree));
            var first = sites.Count;
            foreach (var (node, line) in Sites(tree.GetRoot()))
            {
                if (Build(model, node, line, sites.Count, tree.FilePath, precise: false) is { } site)
                    sites.Add(site);
            }

            PlanGuards(model, tree.GetRoot(), tree.FilePath, sites, first);
        }

        return new Plan(compilation, sites);
    }

    /// <summary>
    /// Where a build's probes and guards go: <see cref="Build"/> makes the compilation with them. One that doesn't compile
    /// is looked at again closely (<see cref="Refine"/>), or left out.
    /// </summary>
    internal sealed class Plan(CSharpCompilation compilation, List<Site> sites)
    {
        public int Count => sites.Count;

        /// <summary>
        /// The compilation with the probes and guards in (but those numbered in <paramref name="leaveOut"/>), its switches'
        /// class and their table.
        /// </summary>
        public (CSharpCompilation Compilation, ProbeTable Probes) Build(IReadOnlySet<int>? leaveOut = null)
        {
            var kept = sites.Where(s => leaveOut?.Contains(s.Id) != true).ToList();
            var result = compilation;
            foreach (var tree in compilation.SyntaxTrees)
            {
                var edits = kept.Where(s => s.Path == tree.FilePath).SelectMany(s => s.Edits).ToList();
                if (edits.Count == 0)
                    continue;
                var text = Apply(tree.GetText().ToString(), edits);
                result = result.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)tree.Options, tree.FilePath, Encoding.UTF8));
            }

            var switches = new StringBuilder($"internal static class {SwitchesClass} {{");
            foreach (var site in kept.Where(s => s.Probe is not null))
                switches.Append(CultureInfo.InvariantCulture, $" public static bool P{site.Id};");
            switches.Append(" }");
            result = result.AddSyntaxTrees(CSharpSyntaxTree.ParseText(switches.ToString(), compilation.SyntaxTrees.First().Options as CSharpParseOptions,
                "<griff probes>", Encoding.UTF8));
            return (result, new ProbeTable(kept.Where(s => s.Probe is not null).Select(s => s.Probe!).ToList(),
                kept.Where(s => s.Guard is not null).Select(s => s.Guard!).ToList()));
        }

        /// <summary>
        /// The probes and guards an error at <paramref name="line"/> of <paramref name="path"/> could come from: the probes
        /// starting there, or else the innermost around it, and the guards opening or closing there (where their code goes).
        /// </summary>
        public IEnumerable<int> Around(string path, int line)
        {
            var probes = sites.Where(s => s.Probe is not null && s.Path == path && s.FirstLine <= line && line <= s.LastLine).ToList();
            var starting = probes.Where(s => s.FirstLine == line || s.LastLine == line).ToList();
            var guards = sites.Where(s => s.Probe is null && s.Path == path && (s.FirstLine == line || s.LastLine == line));
            return (starting.Count > 0 ? starting : probes.OrderByDescending(s => s.FirstLine).Take(1)).Concat(guards).Select(s => s.Id);
        }

        /// <summary>Works out the probes and guards numbered <paramref name="ids"/> again with Roslyn's semantic analysis.</summary>
        public void Refine(IEnumerable<int> ids)
        {
            var models = new Dictionary<SyntaxTree, Lazy<SemanticModel>>();
            foreach (var id in ids)
            {
                var index = sites.FindIndex(s => s.Id == id);
                if (index < 0)
                    continue;
                var site = sites[index];
                var tree = site.Node.SyntaxTree;
                if (!models.TryGetValue(tree, out var model))
                    models[tree] = model = new Lazy<SemanticModel>(() => compilation.GetSemanticModel(tree));
                var refined = site.Remake is { } remake ? Safely(() => remake(model))
                    : site.Probe is { } probe ? ScriptInstrumenter.Build(model, site.Node, probe.Line, id, site.Path, precise: true)
                    : null;
                if (refined is not null)
                    sites[index] = refined;
            }
        }
    }

    private static string Apply(string text, IEnumerable<Edit> edits)
    {
        var builder = new StringBuilder(text.Length + 1024);
        var at = 0;
        foreach (var edit in edits.OrderBy(e => e.Offset).ThenBy(e => e.Order).ThenBy(e => e.Rank))
        {
            if (edit.Offset < at)
                continue;
            builder.Append(text, at, edit.Offset - at).Append(edit.Text);
            at = edit.Offset + edit.Length;
        }

        return builder.Append(text, at, text.Length - at).ToString();
    }

    // `precise` asks Roslyn about every variable's type and whether it's set, rather than reading the code.
    private static Site? Build(Lazy<SemanticModel> model, SyntaxNode node, int line, int id, string path, bool precise) =>
        Safely(() => node switch
        {
            StatementSyntax statement => Statement(model, statement, line, id, path, precise),
            ExpressionSyntax { Parent: ArrowExpressionClauseSyntax arrow } body => Arrow(model, arrow, body, line, id, path, precise),
            ExpressionSyntax { Parent: LambdaExpressionSyntax lambda } body => Lambda(model.Value, lambda, body, line, id, path),
            _ => null,
        });

    private static Site? Safely(Func<Site?> build)
    {
        try
        {
            return build();
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
            : [new Edit(statement.SpanStart, 1, "{ "), new Edit(statement.SpanStart, 2, call + " "), new Edit(statement.Span.End, 0, " }", Rank: int.MinValue)];
        return new Site(id, path, probe, statement, edits, line, LineOf(statement, statement.Span.End));
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
        var last = LineOf(arrow, semicolon.Span.End);
        var guard = new ScriptGuard(id, path, line, last, probe.Where, names, self);
        var call = Call(probe);
        var getter = member is PropertyDeclarationSyntax or IndexerDeclarationSyntax;
        var open = (getter ? "{ get { " : "{ ") + "try { " + call + (returns == false || body is ThrowExpressionSyntax ? " " : " return ");
        return new Site(id, path, probe, body,
            [new Edit(arrow.ArrowToken.SpanStart, 1, open, arrow.ArrowToken.Span.Length), new Edit(semicolon.Span.End, 0, Catch(guard) + (getter ? " } }" : " }"))],
            line, last) { Guard = guard };
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
        var last = LineOf(body, body.Span.End);
        var guard = new ScriptGuard(id, path, line, last, probe.Where, names, false);
        var nothing = symbol.ReturnsVoid || symbol.IsAsync && symbol.ReturnType is INamedTypeSymbol { Arity: 0 } || body is ThrowExpressionSyntax;
        var open = "{ try { " + Call(probe) + (nothing ? " " : " return ");
        return new Site(id, path, probe, body, [new Edit(body.SpanStart, 2, open), new Edit(body.Span.End, 0, ";" + Catch(guard) + " }")], line, last) { Guard = guard };
    }

    private static string Call(ScriptProbe probe)
    {
        var id = probe.Id.ToString(CultureInfo.InvariantCulture);
        var values = string.Join(", ", probe.Names.Select(n => "@" + n));
        return $"if (global::{SwitchesClass}.P{id}) {Hit}(typeof(global::{SwitchesClass}), {id}, {(probe.HasThis ? "this" : "null")}, new object[] {{ {values} }});";
    }

    // ----- Guards -----

    // What closes a guard's `try {`: a filter that notes an exception coming up through the code it covers, with the values in
    // scope there, and never catches it.
    private static string Catch(ScriptGuard guard)
    {
        var id = guard.Id.ToString(CultureInfo.InvariantCulture);
        var values = string.Join(", ", guard.Names.Select(n => "@" + n));
        return $" }} catch (global::System.Exception __griffError) when ({Probes}.Watches(__griffError) && {Probes}.Threw(__griffError, " +
            $"typeof(global::{SwitchesClass}), {id}, {(guard.HasThis ? "this" : "null")}, new object[] {{ {values} }})) {{ throw; }}";
    }

    private const string Probes = "global::Griff.Debugging.ScriptProbes";

    // Guards for a script's code: each method's (lambda's, local function's) statements are covered by guards, so an
    // exception thrown by one is noted by the innermost guard around it, with the variables in scope there. A guard covers
    // a block from a statement to its end, starting a new one wherever more variables are in scope (after a declaration,
    // say) than the guard around it passes, unless a local function or label it would take in is named before it. In an
    // iterator a guard can't hold a `yield return`, so in a block with one each other statement gets a guard of its own
    // (unless it declares a variable for the statements after it), and a declaration is split so its value is set in one
    // (`T x; try { x = ...`). The rest of an iterator's lines are covered by the coroutine running it (see Coroutine).
    private static void PlanGuards(Lazy<SemanticModel> model, SyntaxNode root, string path, List<Site> sites, int first)
    {
        var probes = new Dictionary<SyntaxNode, ScriptProbe>();
        for (var i = first; i < sites.Count; i++)
        {
            if (sites[i] is { Probe: { } probe, Node: StatementSyntax })
                probes[sites[i].Node] = probe;
        }

        // The names the innermost guard around each statement passes (null for none).
        var covered = new Dictionary<StatementSyntax, IReadOnlyList<string>?>();
        var iterators = new Dictionary<SyntaxNode, bool>();
        foreach (var block in root.DescendantNodes().OfType<BlockSyntax>())
        {
            if (FunctionOf(block) is not { } function)
                continue;
            if (!iterators.TryGetValue(function, out var iterator))
                iterators[function] = iterator = Yields(function);

            var statements = block.Statements;
            var active = Enclosing(block, function, covered);
            if (!iterator || !statements.Any(HasYield))
            {
                // Local functions at the end of the block stay outside its guards (they're seen all through the block anyway), so
                // the code before them can use them.
                var tail = statements.Count;
                while (tail > 0 && statements[tail - 1] is LocalFunctionStatementSyntax)
                    tail--;
                // Unless one uses a variable of the block, which it couldn't see from outside the guard declaring it.
                if (tail < statements.Count && UsesVariablesOf(statements, tail))
                    tail = statements.Count;
                var closeAt = tail < statements.Count ? statements[tail].SpanStart : block.CloseBraceToken.SpanStart;
                var reached = ReachedFromBefore(statements, tail);
                for (var i = 0; i < statements.Count; i++)
                {
                    var statement = statements[i];
                    if (i < tail && probes.TryGetValue(statement, out var probe) && (active is null || !probe.Names.SequenceEqual(active)) && !reached[i]
                        && Safely(() => Guard(model, statement, closeAt, sites.Count, path, probe.Names, probe.HasThis, precise: false)) is { } guard)
                    {
                        sites.Add(guard);
                        active = probe.Names;
                    }

                    covered[statement] = active;
                }

                continue;
            }

            foreach (var statement in statements)
            {
                covered[statement] = null;
                if (!probes.TryGetValue(statement, out var probe) || statement is LocalFunctionStatementSyntax or LabeledStatementSyntax || HasYield(statement)
                    || statement is not LocalDeclarationStatementSyntax && DeclaresForLater(statement))
                    continue;
                var guard = statement is LocalDeclarationStatementSyntax declaration
                    ? Safely(() => Split(model, declaration, sites.Count, path, probe.Names, probe.HasThis, precise: false))
                    : Safely(() => Guard(model, statement, statement.Span.End, sites.Count, path, probe.Names, probe.HasThis, precise: false));
                if (guard is null)
                    continue;
                sites.Add(guard);
                if (statement is not LocalDeclarationStatementSyntax)
                    covered[statement] = probe.Names;
            }
        }
    }

    // For each statement of a block, whether a guard can't start there: a local function or a label from there on (which
    // would be inside the guard) is named by a statement before it (outside, where it couldn't be seen or jumped to). The
    // statements from `end` on (local functions at the end) stay outside every guard.
    private static bool[] ReachedFromBefore(SyntaxList<StatementSyntax> statements, int end)
    {
        var reached = new bool[statements.Count];
        var declared = new Dictionary<string, int>();
        for (var i = 0; i < end; i++)
        {
            switch (statements[i])
            {
                case LocalFunctionStatementSyntax local:
                    declared.TryAdd(local.Identifier.ValueText, i);
                    break;
                case LabeledStatementSyntax label:
                    declared.TryAdd(label.Identifier.ValueText, i);
                    break;
            }
        }

        if (declared.Count == 0)
            return reached;
        for (var i = 0; i < statements.Count; i++)
        {
            foreach (var name in statements[i].DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                // Named at i, declared at `at` later: no guard can start after i up to `at`.
                if (declared.TryGetValue(name.Identifier.ValueText, out var at) && at > i)
                {
                    for (var j = i + 1; j <= at; j++)
                        reached[j] = true;
                }
            }
        }

        return reached;
    }

    // Whether the local functions from `tail` on name a variable declared in the statements before them.
    private static bool UsesVariablesOf(SyntaxList<StatementSyntax> statements, int tail)
    {
        var declared = new HashSet<string>();
        for (var i = 0; i < tail; i++)
        {
            foreach (var node in statements[i].DescendantNodes())
            {
                var name = node switch
                {
                    VariableDeclaratorSyntax variable => variable.Identifier.ValueText,
                    SingleVariableDesignationSyntax designation => designation.Identifier.ValueText,
                    ForEachStatementSyntax loop => loop.Identifier.ValueText,
                    CatchDeclarationSyntax caught => caught.Identifier.ValueText,
                    _ => null,
                };
                if (name is { Length: > 0 })
                    declared.Add(name);
            }
        }

        for (var i = tail; i < statements.Count; i++)
        {
            if (statements[i].DescendantNodes().OfType<IdentifierNameSyntax>().Any(n => declared.Contains(n.Identifier.ValueText)))
                return true;
        }

        return false;
    }

    // The names the guard around `block` passes, from the statement of an outer block of the same function it's in.
    private static IReadOnlyList<string>? Enclosing(BlockSyntax block, SyntaxNode function, Dictionary<StatementSyntax, IReadOnlyList<string>?> covered)
    {
        for (SyntaxNode node = block; node != function && node.Parent is { } parent; node = parent)
        {
            if (parent is BlockSyntax && node is StatementSyntax statement)
                return covered.GetValueOrDefault(statement);
        }

        return null;
    }

    // Whether a statement declares a variable the statements after it can use (`var (a, b) = ...;`, an `out var` in a call,
    // a pattern in an if's condition), which it couldn't in a guard of its own: a variable in its own expressions, not in
    // the statements inside it or in a lambda.
    private static bool DeclaresForLater(StatementSyntax statement) =>
        statement.DescendantNodes(n => n == statement || n is not (StatementSyntax or AnonymousFunctionExpressionSyntax))
            .Any(n => n is SingleVariableDesignationSyntax or ParenthesizedVariableDesignationSyntax);

    // Whether a statement holds a `yield`, not counting the functions inside it (a local function's are its own).
    private static bool HasYield(StatementSyntax statement) => statement is not LocalFunctionStatementSyntax && Yields(statement);

    // Whether `node` (a function, or a statement) holds a `yield`, not counting the functions inside it.
    private static bool Yields(SyntaxNode node) =>
        node.DescendantNodesAndSelf(n => n == node || n is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)).OfType<YieldStatementSyntax>().Any();

    // A guard from `start` to `closeAt` (the end of its block, or of the statement): `try {` before the statement, and the
    // filter at the end. With `precise`, the names it passes are worked out again with Roslyn's help.
    private static Site Guard(Lazy<SemanticModel> model, StatementSyntax start, int closeAt, int id, string path, IReadOnlyList<string> names, bool hasThis, bool precise)
    {
        var function = FunctionOf(start)!;
        if (precise)
            (names, hasThis) = (InScope(model, start, function, precise: true).Distinct().ToList(), CanPassThis(function));
        var guard = new ScriptGuard(id, path, LineOf(start, start.SpanStart), LineOf(start, closeAt), Where(start), names, hasThis);
        return new Site(id, path, null, start, [new Edit(start.SpanStart, 1, "try { "), new Edit(closeAt, 0, Catch(guard), Rank: -start.SpanStart)], guard.Line, guard.EndLine)
        {
            Guard = guard,
            Remake = m => Guard(m, start, closeAt, id, path, names, hasThis, precise: true),
        };
    }

    // A declaration in an iterator, `T x = value;`, split so its value is set in a guard: `T x; try { x = value; }` (with
    // `var` written out as the type). Only one variable, with a value, of a type that can be named.
    private static Site? Split(Lazy<SemanticModel> model, LocalDeclarationStatementSyntax declaration, int id, string path, IReadOnlyList<string> names, bool hasThis, bool precise)
    {
        if (declaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword) || declaration.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword) || declaration.IsConst
            || declaration.Modifiers.Count > 0 || declaration.Declaration.Variables is not [{ Initializer.Value: var value } variable]
            || value is RefExpressionSyntax or StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax)
            return null;
        var type = declaration.Declaration.Type;
        if (type is RefTypeSyntax or ScopedTypeSyntax || !Passable(type, value))
            return null;

        var function = FunctionOf(declaration)!;
        if (precise)
            (names, hasThis) = (InScope(model, declaration, function, precise: true).Distinct().ToList(), CanPassThis(function));
        var edits = new List<Edit>();
        if (type is IdentifierNameSyntax { IsVar: true })
        {
            if (model.Value.GetTypeInfo(type).Type is not { } named || !Nameable(named))
                return null;
            edits.Add(new Edit(type.SpanStart, 3, named.ToDisplayString(TypeName), type.Span.Length));
        }

        var guard = new ScriptGuard(id, path, LineOf(declaration, declaration.SpanStart), LineOf(declaration, declaration.Span.End), Where(declaration), names, hasThis);
        edits.Add(new Edit(variable.Identifier.Span.End, 3, "; try { @" + variable.Identifier.ValueText));
        edits.Add(new Edit(declaration.Span.End, 0, Catch(guard), Rank: -declaration.SpanStart));
        return new Site(id, path, null, declaration, edits, guard.Line, guard.EndLine)
        {
            Guard = guard,
            Remake = m => Split(m, declaration, id, path, names, hasThis, precise: true),
        };
    }

    private static readonly SymbolDisplayFormat TypeName =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    // Whether a type can be written out in code: not anonymous, an error, a pointer or a ref struct, nor made of one.
    private static bool Nameable(ITypeSymbol type) => type switch
    {
        IErrorTypeSymbol or IPointerTypeSymbol or IFunctionPointerTypeSymbol or IDynamicTypeSymbol => false,
        { IsAnonymousType: true } or { IsRefLikeType: true } => false,
        IArrayTypeSymbol array => Nameable(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.All(Nameable) && (named.ContainingType is null || Nameable(named.ContainingType)),
        _ => true,
    };

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
