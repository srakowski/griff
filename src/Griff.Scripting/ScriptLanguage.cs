using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace Griff.Scripting;

/// <summary>A compiler message for a span of a script being edited.</summary>
public sealed record ScriptProblem(int Start, int Length, int Line, int Column, string Id, string Message, bool IsError);

/// <summary>A classified span of script text, e.g. "keyword" or "method name" (Roslyn's classification names).</summary>
public readonly record struct ScriptSpan(int Start, int Length, string Kind);

/// <summary>A place in a script, for go to definition and find references.</summary>
public sealed record ScriptLocation(string Path, int Start, int Length, int Line, int Column, string LineText);

/// <summary>One overload in signature help, split into parts so the current parameter can be emphasized.</summary>
public sealed record ScriptSignature(IReadOnlyList<TaggedText> Prefix, IReadOnlyList<IReadOnlyList<TaggedText>> Parameters, IReadOnlyList<TaggedText> Suffix, string? Summary, IReadOnlyList<string?> ParameterDocs);

public sealed record ScriptSignatureHelp(IReadOnlyList<ScriptSignature> Signatures, int Selected, int Argument, int ArgumentListStart);

/// <summary>The member a property row in the editor jumps to: its name and where it's declared.</summary>
public sealed record ScriptMember(string Name, int Start, int Length);

/// <summary>A class declared in a script and the members it declares.</summary>
public sealed record ScriptClass(string Name, int Start, int Length, IReadOnlyList<ScriptMember> Properties);

/// <summary>
/// The C# language service behind the script editor: completion, quick info, signature help, live diagnostics,
/// semantic highlighting, go to definition, references, rename and formatting. It holds every script in the project
/// as Roslyn documents, set to the editor's unsaved text for open files, compiled the same way as
/// <see cref="ScriptCompiler"/>. Methods are safe to call from any thread; each works on a snapshot.
/// </summary>
public sealed class ScriptLanguage
{
    private static readonly Lazy<MefHostServices> Host = new(() => MefHostServices.Create(MefHostServices.DefaultAssemblies));

    private readonly AdhocWorkspace _workspace;
    private readonly ProjectId _projectId;
    private readonly object _gate = new();
    private Solution _solution;

    public ScriptLanguage()
    {
        _workspace = new AdhocWorkspace(Host.Value);
        _projectId = ProjectId.CreateNewId();
        var project = ProjectInfo.Create(
            _projectId, VersionStamp.Create(), "Scripts", "GriffScripts", LanguageNames.CSharp,
            compilationOptions: ScriptCompiler.CompilationOptions,
            parseOptions: ScriptCompiler.ParseOptions,
            metadataReferences: ScriptCompiler.MetadataReferences);
        _solution = _workspace.CurrentSolution
            .AddProject(project)
            .AddDocument(DocumentId.CreateNewId(_projectId), ScriptCompiler.ImplicitUsingsPath, SourceText.From(ScriptCompiler.ImplicitUsings), filePath: ScriptCompiler.ImplicitUsingsPath);
    }

    /// <summary>Warms up Roslyn (its first completion takes a moment) so the editor's first one is quick.</summary>
    public Task WarmUpAsync() => Task.Run(async () =>
    {
        const string probe = "class __Warm : Behavior { void M() { Transform.Position.ToString(); } }";
        var document = Snapshot().GetProject(_projectId)!.AddDocument("__warm.cs", SourceText.From(probe));
        var position = probe.IndexOf("Position", StringComparison.Ordinal);
        if (CompletionService.GetService(document) is { } completion)
            await completion.GetCompletionsAsync(document, position).ConfigureAwait(false);
        if (QuickInfoService.GetService(document) is { } quickInfo)
            await quickInfo.GetQuickInfoAsync(document, position).ConfigureAwait(false);
        await Classifier.GetClassifiedSpansAsync(document, new TextSpan(0, probe.Length)).ConfigureAwait(false);
        (await document.GetSemanticModelAsync().ConfigureAwait(false))?.GetDiagnostics();
    });

    /// <summary>Replaces every script with <paramref name="sources"/> (e.g. the project's files on disk).</summary>
    public void Load(IEnumerable<ScriptSource> sources, IReadOnlyDictionary<string, string>? openTexts = null)
    {
        lock (_gate)
        {
            var solution = _solution;
            foreach (var document in solution.GetProject(_projectId)!.Documents.Where(d => d.FilePath != ScriptCompiler.ImplicitUsingsPath).ToList())
                solution = solution.RemoveDocument(document.Id);
            foreach (var source in sources)
            {
                var text = openTexts?.GetValueOrDefault(Normalize(source.Path)) ?? source.Text;
                solution = solution.AddDocument(DocumentId.CreateNewId(_projectId), Path.GetFileName(source.Path), SourceText.From(text), filePath: source.Path);
            }

            if (openTexts is not null)
            {
                foreach (var (path, text) in openTexts)
                {
                    if (Find(solution, path) is null)
                        solution = solution.AddDocument(DocumentId.CreateNewId(_projectId), Path.GetFileName(path), SourceText.From(text), filePath: path);
                }
            }

            _solution = solution;
        }
    }

    /// <summary>Sets a script's text (adding it if it's new), e.g. on every edit in the editor.</summary>
    public void SetText(string path, string text)
    {
        lock (_gate)
        {
            _solution = Find(_solution, path) is { } document
                ? _solution.WithDocumentText(document.Id, SourceText.From(text))
                : _solution.AddDocument(DocumentId.CreateNewId(_projectId), Path.GetFileName(path), SourceText.From(text), filePath: path);
        }
    }

    public void Remove(string path)
    {
        lock (_gate)
        {
            if (Find(_solution, path) is { } document)
                _solution = _solution.RemoveDocument(document.Id);
        }
    }

    // ----- Highlighting and diagnostics -----

    public Task<IReadOnlyList<ScriptSpan>> ClassifyAsync(string path, CancellationToken cancel = default) =>
        On(path, d => ClassifyAsync(d, cancel), []);

    private static async Task<IReadOnlyList<ScriptSpan>> ClassifyAsync(Document document, CancellationToken cancel)
    {
        var text = await document.GetTextAsync(cancel).ConfigureAwait(false);
        var spans = await Classifier.GetClassifiedSpansAsync(document, new TextSpan(0, text.Length), cancel).ConfigureAwait(false);
        return [.. spans.Select(s => new ScriptSpan(s.TextSpan.Start, s.TextSpan.Length, s.ClassificationType))];
    }

    /// <summary>Errors and warnings in one script, as the compiler would report them on save.</summary>
    public Task<IReadOnlyList<ScriptProblem>> DiagnoseAsync(string path, CancellationToken cancel = default) =>
        On(path, d => DiagnoseAsync(d, cancel), []);

    private static async Task<IReadOnlyList<ScriptProblem>> DiagnoseAsync(Document document, CancellationToken cancel)
    {
        if (await document.GetSemanticModelAsync(cancel).ConfigureAwait(false) is not { } model)
            return [];
        return [.. model.GetDiagnostics(cancellationToken: cancel)
            .Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning && !d.IsSuppressed && d.Location.IsInSource)
            .Select(d =>
            {
                var span = d.Location.SourceSpan;
                var line = d.Location.GetLineSpan().StartLinePosition;
                return new ScriptProblem(span.Start, span.Length, line.Line + 1, line.Character + 1, d.Id, d.GetMessage(), d.Severity == DiagnosticSeverity.Error);
            })
            .OrderBy(p => p.Start)];
    }

    // ----- Completion -----

    /// <summary>True if typing <paramref name="typed"/> just before <paramref name="position"/> should open completion.</summary>
    public bool ShouldComplete(string path, int position, char typed)
    {
        if (Document(path) is not { } document || CompletionService.GetService(document) is not { } service)
            return false;
        var text = document.GetTextAsync().Result;
        return service.ShouldTriggerCompletion(text, position, CompletionTrigger.CreateInsertionTrigger(typed));
    }

    public Task<ScriptCompletions?> CompleteAsync(string path, int position, char? typed, CancellationToken cancel = default) =>
        On(path, d => CompleteAsync(d, position, typed, cancel), null);

    private static async Task<ScriptCompletions?> CompleteAsync(Document document, int position, char? typed, CancellationToken cancel)
    {
        if (CompletionService.GetService(document) is not { } service)
            return null;
        var trigger = typed is { } c ? CompletionTrigger.CreateInsertionTrigger(c) : CompletionTrigger.Invoke;
        var list = await service.GetCompletionsAsync(document, position, trigger, cancellationToken: cancel).ConfigureAwait(false);
        if (list.ItemsList.Count == 0)
            return null;
        return new ScriptCompletions(document, service, list, position);
    }

    // ----- Quick info -----

    /// <summary>What's under <paramref name="position"/>: its signature and documentation, as tagged text.</summary>
    public Task<ScriptQuickInfo?> QuickInfoAsync(string path, int position, CancellationToken cancel = default) =>
        On(path, d => QuickInfoAsync(d, position, cancel), null);

    private static async Task<ScriptQuickInfo?> QuickInfoAsync(Document document, int position, CancellationToken cancel)
    {
        if (QuickInfoService.GetService(document) is not { } service)
            return null;
        var info = await service.GetQuickInfoAsync(document, position, cancel).ConfigureAwait(false);
        if (info is null || info.Sections.IsEmpty)
            return null;
        var sections = info.Sections
            .Where(s => s.Kind is not QuickInfoSectionKinds.DocumentationComments || s.TaggedParts.Length > 0)
            .Select(s => new ScriptQuickInfoSection(s.Kind, s.TaggedParts))
            .ToList();
        return new ScriptQuickInfo(info.Span.Start, info.Span.Length, info.Tags, sections);
    }

    // ----- Signature help -----

    /// <summary>The overloads of the call the caret is in, which one fits, and which argument the caret is on.</summary>
    public Task<ScriptSignatureHelp?> SignatureHelpAsync(string path, int position, CancellationToken cancel = default) =>
        On(path, d => SignatureHelpAsync(d, position, cancel), null);

    private static async Task<ScriptSignatureHelp?> SignatureHelpAsync(Document document, int position, CancellationToken cancel)
    {
        var root = await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancel).ConfigureAwait(false);
        if (root is null || model is null)
            return null;

        var token = root.FindToken(Math.Max(0, position - 1));
        var arguments = token.Parent?.AncestorsAndSelf().OfType<ArgumentListSyntax>()
            .FirstOrDefault(a => a.OpenParenToken.Span.End <= position &&
                                 (a.CloseParenToken.IsMissing || position <= a.CloseParenToken.SpanStart));
        if (arguments is null)
            return null;

        IEnumerable<IMethodSymbol> methods;
        ISymbol? chosen;
        switch (arguments.Parent)
        {
            case InvocationExpressionSyntax invocation:
                methods = model.GetMemberGroup(invocation.Expression, cancel).OfType<IMethodSymbol>();
                var info = model.GetSymbolInfo(invocation, cancel);
                chosen = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
                break;
            case BaseObjectCreationExpressionSyntax creation:
                var type = model.GetTypeInfo(creation, cancel).Type as INamedTypeSymbol;
                methods = type?.InstanceConstructors.Where(c => model.IsAccessible(position, c)) ?? [];
                var created = model.GetSymbolInfo(creation, cancel);
                chosen = created.Symbol ?? created.CandidateSymbols.FirstOrDefault();
                break;
            case ConstructorInitializerSyntax initializer:
                var target = model.GetSymbolInfo(initializer, cancel);
                chosen = target.Symbol ?? target.CandidateSymbols.FirstOrDefault();
                methods = (chosen?.ContainingType as INamedTypeSymbol)?.InstanceConstructors ?? [];
                break;
            default:
                return null;
        }

        var overloads = methods.Distinct<IMethodSymbol>(SymbolEqualityComparer.Default).OrderBy(m => m.Parameters.Length).ToList();
        if (overloads.Count == 0)
            return null;

        var argument = arguments.Arguments.GetSeparators().Count(s => s.SpanStart < position);
        var selected = chosen is IMethodSymbol m ? overloads.FindIndex(o => SymbolEqualityComparer.Default.Equals(o, m.OriginalDefinition) || SymbolEqualityComparer.Default.Equals(o, m)) : -1;
        if (selected < 0)
            selected = Math.Max(0, overloads.FindIndex(o => o.Parameters.Length > argument || o.Parameters.LastOrDefault()?.IsParams == true));

        return new ScriptSignatureHelp([.. overloads.Select(o => Describe(o, model, position))], selected, argument, arguments.SpanStart);
    }

    private static ScriptSignature Describe(IMethodSymbol method, SemanticModel model, int position)
    {
        var format = SymbolDisplayFormat.MinimallyQualifiedFormat
            .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName |
                                  SymbolDisplayParameterOptions.IncludeDefaultValue | SymbolDisplayParameterOptions.IncludeParamsRefOut |
                                  SymbolDisplayParameterOptions.IncludeOptionalBrackets)
            .WithMemberOptions(SymbolDisplayMemberOptions.IncludeType | SymbolDisplayMemberOptions.IncludeContainingType);
        var prefix = new List<TaggedText>();
        if (method.MethodKind == MethodKind.Constructor)
        {
            prefix.AddRange(Tagged(method.ContainingType.ToMinimalDisplayParts(model, position)));
        }
        else
        {
            prefix.AddRange(Tagged(method.ReturnType.ToMinimalDisplayParts(model, position)));
            prefix.Add(new TaggedText(TextTags.Space, " "));
            prefix.AddRange(Tagged(method.ContainingType.ToMinimalDisplayParts(model, position)));
            prefix.Add(new TaggedText(TextTags.Punctuation, "."));
            prefix.Add(new TaggedText(TextTags.Method, method.Name));
        }

        prefix.Add(new TaggedText(TextTags.Punctuation, "("));
        var parameters = method.Parameters.Select(p => (IReadOnlyList<TaggedText>)Tagged(p.ToMinimalDisplayParts(model, position, format))).ToList();
        var docs = Documentation.Parse(method.GetDocumentationCommentXml());
        return new ScriptSignature(prefix, parameters, [new TaggedText(TextTags.Punctuation, ")")], docs.Summary,
            [.. method.Parameters.Select(p => docs.Parameters.GetValueOrDefault(p.Name))]);
    }

    private static List<TaggedText> Tagged(ImmutableArray<SymbolDisplayPart> parts) =>
        [.. parts.Select(p => new TaggedText(TagOf(p.Kind), p.ToString()))];

    private static string TagOf(SymbolDisplayPartKind kind) => kind switch
    {
        SymbolDisplayPartKind.Keyword => TextTags.Keyword,
        SymbolDisplayPartKind.ClassName or SymbolDisplayPartKind.RecordClassName => TextTags.Class,
        SymbolDisplayPartKind.StructName or SymbolDisplayPartKind.RecordStructName => TextTags.Struct,
        SymbolDisplayPartKind.InterfaceName => TextTags.Interface,
        SymbolDisplayPartKind.EnumName => TextTags.Enum,
        SymbolDisplayPartKind.EnumMemberName => TextTags.EnumMember,
        SymbolDisplayPartKind.DelegateName => TextTags.Delegate,
        SymbolDisplayPartKind.TypeParameterName => TextTags.TypeParameter,
        SymbolDisplayPartKind.MethodName or SymbolDisplayPartKind.ExtensionMethodName => TextTags.Method,
        SymbolDisplayPartKind.PropertyName => TextTags.Property,
        SymbolDisplayPartKind.FieldName => TextTags.Field,
        SymbolDisplayPartKind.ParameterName => TextTags.Parameter,
        SymbolDisplayPartKind.LocalName => TextTags.Local,
        SymbolDisplayPartKind.NumericLiteral => TextTags.NumericLiteral,
        SymbolDisplayPartKind.StringLiteral => TextTags.StringLiteral,
        SymbolDisplayPartKind.Punctuation => TextTags.Punctuation,
        SymbolDisplayPartKind.Operator => TextTags.Operator,
        SymbolDisplayPartKind.Space => TextTags.Space,
        SymbolDisplayPartKind.NamespaceName => TextTags.Namespace,
        _ => TextTags.Text,
    };

    // ----- Navigation -----

    /// <summary>Where the symbol at <paramref name="position"/> is declared in the scripts, if it is.</summary>
    public Task<ScriptLocation?> DefinitionAsync(string path, int position, CancellationToken cancel = default) =>
        On(path, d => DefinitionAsync(d, position, cancel), null);

    private static async Task<ScriptLocation?> DefinitionAsync(Document document, int position, CancellationToken cancel)
    {
        if (await SymbolAtAsync(document, position, cancel).ConfigureAwait(false) is not { } symbol)
            return null;
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource && l.SourceTree?.FilePath != ScriptCompiler.ImplicitUsingsPath);
        return location is null ? null : ToLocation(location);
    }

    /// <summary>Every use of the symbol at <paramref name="position"/> in the scripts, its declaration included.</summary>
    public Task<IReadOnlyList<ScriptLocation>> ReferencesAsync(string path, int position, bool thisFileOnly, CancellationToken cancel = default) =>
        On(path, d => ReferencesAsync(d, position, thisFileOnly, cancel), []);

    private static async Task<IReadOnlyList<ScriptLocation>> ReferencesAsync(Document document, int position, bool thisFileOnly, CancellationToken cancel)
    {
        var path = document.FilePath!;
        if (await SymbolAtAsync(document, position, cancel).ConfigureAwait(false) is not { } symbol)
            return [];
        var documents = thisFileOnly ? ImmutableHashSet.Create(document) : null;
        var found = await SymbolFinder.FindReferencesAsync(symbol, document.Project.Solution, documents, cancel).ConfigureAwait(false);
        var locations = found
            // A property's accessors come back as definitions too, at their get and set keywords.
            .SelectMany(r => r.Definition.Locations.Where(l => l.IsInSource && r.Definition is not IMethodSymbol { AssociatedSymbol: not null })
                .Concat(r.Locations.Select(l => l.Location)))
            .Where(l => l.IsInSource && l.SourceTree?.FilePath != ScriptCompiler.ImplicitUsingsPath)
            .Where(l => !thisFileOnly || SamePath(l.SourceTree?.FilePath, path))
            .Select(ToLocation)
            // The same place can be found through more than one symbol.
            .DistinctBy(l => (l.Path, l.Start))
            .OrderBy(l => l.Path, StringComparer.OrdinalIgnoreCase).ThenBy(l => l.Start)
            .ToList();
        return locations;
    }

    /// <summary>The name to rename at <paramref name="position"/>, if it's something the scripts declare.</summary>
    public Task<string?> RenameTargetAsync(string path, int position, CancellationToken cancel = default) =>
        On(path, async d => await SymbolAtAsync(d, position, cancel).ConfigureAwait(false) is { } symbol && symbol.Locations.Any(l => l.IsInSource)
            ? symbol.Name
            : null, null);

    /// <summary>Renames the symbol at <paramref name="position"/> everywhere. Returns each changed script's new text.</summary>
    public Task<IReadOnlyDictionary<string, string>> RenameAsync(string path, int position, string newName, CancellationToken cancel = default) =>
        On(path, d => RenameAsync(d, position, newName, cancel), new Dictionary<string, string>());

    private static async Task<IReadOnlyDictionary<string, string>> RenameAsync(Document document, int position, string newName, CancellationToken cancel)
    {
        if (await SymbolAtAsync(document, position, cancel).ConfigureAwait(false) is not { } symbol || !symbol.Locations.Any(l => l.IsInSource))
            return new Dictionary<string, string>();
        var solution = document.Project.Solution;
        var renamed = await Renamer.RenameSymbolAsync(solution, symbol, new SymbolRenameOptions(), newName, cancel).ConfigureAwait(false);
        var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in renamed.GetChanges(solution).GetProjectChanges().SelectMany(p => p.GetChangedDocuments()))
        {
            var changed = renamed.GetDocument(id)!;
            if (changed.FilePath is { } file)
                changes[file] = (await changed.GetTextAsync(cancel).ConfigureAwait(false)).ToString();
        }

        return changes;
    }

    /// <summary>The script laid out by the C# formatting rules, as edits to apply in order from last to first.</summary>
    public Task<IReadOnlyList<TextChange>> FormatAsync(string path, CancellationToken cancel = default) =>
        On(path, d => FormatAsync(d, cancel), []);

    private static async Task<IReadOnlyList<TextChange>> FormatAsync(Document document, CancellationToken cancel)
    {
        var formatted = await Formatter.FormatAsync(document, cancellationToken: cancel).ConfigureAwait(false);
        var changes = await formatted.GetTextChangesAsync(document, cancel).ConfigureAwait(false);
        return [.. changes.OrderByDescending(c => c.Span.Start)];
    }

    /// <summary>Where a class the scripts declare is, by its full name (e.g. "Ship"), or null if it isn't one of theirs.</summary>
    public Task<ScriptLocation?> FindTypeAsync(string fullName, CancellationToken cancel = default) => Task.Run(async () =>
    {
        var project = Snapshot().GetProject(_projectId)!;
        var compilation = await project.GetCompilationAsync(cancel).ConfigureAwait(false);
        var type = compilation?.Assembly.GetTypeByMetadataName(fullName);
        var location = type?.Locations.FirstOrDefault(l => l.IsInSource);
        return location is null ? null : ToLocation(location);
    }, cancel);

    // ----- Structure -----

    /// <summary>The classes a script declares and their properties, from its current text.</summary>
    public static IReadOnlyList<ScriptClass> Outline(string text)
    {
        var root = CSharpSyntaxTree.ParseText(text, ScriptCompiler.ParseOptions).GetRoot();
        return [.. root.DescendantNodes().OfType<ClassDeclarationSyntax>().Select(c => new ScriptClass(
            c.Identifier.Text, c.Identifier.SpanStart, c.Identifier.Span.Length,
            [.. c.Members.OfType<PropertyDeclarationSyntax>().Select(p => new ScriptMember(p.Identifier.Text, p.Identifier.SpanStart, p.Identifier.Span.Length))]))];
    }

    /// <summary>"Ship › Update" for the member around <paramref name="position"/>.</summary>
    public static (string? Type, string? Member, string? Property) Breadcrumb(string text, int position)
    {
        var root = CSharpSyntaxTree.ParseText(text, ScriptCompiler.ParseOptions).GetRoot();
        var node = root.FindToken(Math.Clamp(position, 0, Math.Max(0, text.Length - 1))).Parent;
        var type = node?.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text;
        var member = node?.AncestorsAndSelf().OfType<MemberDeclarationSyntax>().FirstOrDefault(m => m is not BaseTypeDeclarationSyntax);
        var name = member switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text + "()",
            PropertyDeclarationSyntax p => p.Identifier.Text,
            ConstructorDeclarationSyntax c => c.Identifier.Text + "()",
            FieldDeclarationSyntax f => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text,
            EventDeclarationSyntax e => e.Identifier.Text,
            _ => null,
        };
        return (type, name, (member as PropertyDeclarationSyntax)?.Identifier.Text);
    }

    // ----- Helpers -----

    private Solution Snapshot()
    {
        lock (_gate)
            return _solution;
    }

    private Document? Document(string path) => Find(Snapshot(), path);

    private static Document? Find(Solution solution, string path) =>
        solution.Projects.SelectMany(p => p.Documents).FirstOrDefault(d => SamePath(d.FilePath, path));

    // Takes the script as it is now, then does the work off the UI thread.
    private Task<T> On<T>(string path, Func<Document, Task<T>> work, T fallback) =>
        Document(path) is { } document ? Task.Run(() => work(document)) : Task.FromResult(fallback);

    private static async Task<ISymbol?> SymbolAtAsync(Document document, int position, CancellationToken cancel)
    {
        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, cancel).ConfigureAwait(false);
        if (symbol is null && position > 0)
            symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position - 1, cancel).ConfigureAwait(false);
        return symbol is IMethodSymbol { ReducedFrom: { } reduced } ? reduced : symbol;
    }

    private static ScriptLocation ToLocation(Location location)
    {
        var span = location.GetLineSpan();
        var text = location.SourceTree!.GetText();
        var line = text.Lines[span.StartLinePosition.Line];
        return new ScriptLocation(span.Path, location.SourceSpan.Start, location.SourceSpan.Length,
            span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1, line.ToString().Trim());
    }

    private static bool SamePath(string? a, string? b) =>
        a is not null && b is not null && string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.StartsWith('<') ? path : Path.GetFullPath(path);
}

/// <summary>A completion list for one position, with what's needed to describe and commit its items.</summary>
public sealed class ScriptCompletions(Document document, CompletionService service, CompletionList list, int position)
{
    public IReadOnlyList<CompletionItem> Items => list.ItemsList;

    /// <summary>The text the list replaces: the partial word before the caret.</summary>
    public TextSpan Span => list.Span;

    public int Position => position;

    /// <summary>True when the list is only suggestions (e.g. naming something new), so typing shouldn't commit an item.</summary>
    public bool IsSuggestionMode => list.SuggestionModeItem is not null;

    public async Task<CompletionDescription> DescribeAsync(CompletionItem item, CancellationToken cancel = default) =>
        await service.GetDescriptionAsync(document, item, cancel).ConfigureAwait(false) ?? CompletionDescription.Empty;

    /// <summary>The edit committing <paramref name="item"/> makes, against the text the list was made for.</summary>
    public async Task<CompletionChange> ChangeAsync(CompletionItem item, char? commitCharacter, CancellationToken cancel = default) =>
        await service.GetChangeAsync(document, item, commitCharacter, cancel).ConfigureAwait(false);
}

public sealed record ScriptQuickInfoSection(string Kind, ImmutableArray<TaggedText> Parts);

public sealed record ScriptQuickInfo(int Start, int Length, ImmutableArray<string> Tags, IReadOnlyList<ScriptQuickInfoSection> Sections);

/// <summary>The summary and parameter descriptions from a member's XML documentation, as plain text.</summary>
internal sealed record Documentation(string? Summary, IReadOnlyDictionary<string, string> Parameters)
{
    public static Documentation Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return new Documentation(null, new Dictionary<string, string>());
        try
        {
            var element = System.Xml.Linq.XElement.Parse(xml);
            return new Documentation(
                Plain(element.Element("summary")),
                element.Elements("param").Where(p => p.Attribute("name") is not null)
                    .GroupBy(p => p.Attribute("name")!.Value)
                    .ToDictionary(g => g.Key, g => Plain(g.First()) ?? ""));
        }
        catch (System.Xml.XmlException)
        {
            return new Documentation(null, new Dictionary<string, string>());
        }
    }

    // <see cref="T:Griff.Body"/> becomes "Body"; whitespace collapses.
    private static string? Plain(System.Xml.Linq.XElement? element)
    {
        if (element is null)
            return null;
        var text = string.Concat(element.Nodes().Select(n => n switch
        {
            System.Xml.Linq.XText t => t.Value,
            System.Xml.Linq.XElement { Name.LocalName: "see" or "seealso" } e =>
                e.Attribute("cref")?.Value.Split(':', 2)[^1].Split('.')[^1].Split('(')[0] ?? e.Attribute("langword")?.Value ?? e.Value,
            System.Xml.Linq.XElement { Name.LocalName: "paramref" or "typeparamref" } e => e.Attribute("name")?.Value ?? "",
            System.Xml.Linq.XElement e => e.Value,
            _ => "",
        }));
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length == 0 ? null : collapsed;
    }
}
