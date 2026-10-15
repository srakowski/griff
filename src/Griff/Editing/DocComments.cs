using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Griff.Editing;

public enum DocRunKind
{
    Text,

    /// <summary>Code or a literal, e.g. <c>null</c> or <c>OnDeath</c>.</summary>
    Code,

    /// <summary>A reference to a type or member; <see cref="DocRun.Target"/> is the type when it could be found.</summary>
    Reference,

    /// <summary>A line of a code block; keeps its spacing.</summary>
    CodeBlock,
}

/// <summary>One piece of documentation text.</summary>
public sealed record DocRun(string Text, DocRunKind Kind = DocRunKind.Text, Type? Target = null);

/// <summary>Documentation as paragraphs of runs, ready to show with links and code styling.</summary>
public sealed partial class DocText(IReadOnlyList<IReadOnlyList<DocRun>> paragraphs)
{
    public static DocText Empty { get; } = new([]);

    public IReadOnlyList<IReadOnlyList<DocRun>> Paragraphs { get; } = paragraphs;

    public bool IsEmpty => Paragraphs.Count == 0;

    /// <summary>The text without styling, paragraphs separated by blank lines.</summary>
    public string PlainText => string.Join("\n\n", Paragraphs.Select(p => string.Concat(p.Select(r => r.Text))));

    /// <summary>The first sentence of the first paragraph, for one-line lists.</summary>
    public string FirstSentence
    {
        get
        {
            var text = Paragraphs.Count == 0 ? "" : string.Concat(Paragraphs[0].Select(r => r.Text));
            var end = SentenceEnd().Match(text);
            return end.Success ? text[..(end.Index + 1)] : text;
        }
    }

    public override string ToString() => PlainText;

    [GeneratedRegex(@"[.!?](?=\s+[A-Z(""])")]
    private static partial Regex SentenceEnd();
}

/// <summary>
/// Reads <c>///</c> documentation comments for types and properties: from the XML file next to an assembly (Griff's own
/// components), or from XML handed over with <see cref="Register"/> (scripts, which are compiled in memory).
/// </summary>
public static partial class DocComments
{
    private static readonly ConditionalWeakTable<Assembly, IReadOnlyDictionary<string, XElement>> Members = new();

    /// <summary>Supplies the documentation XML for an assembly that has no file next to it, e.g. compiled scripts.</summary>
    public static void Register(Assembly assembly, string? xml) => Members.AddOrUpdate(assembly, Parse(xml));

    public static DocText Summary(Type type) => Read(type, type, "summary");

    public static DocText Remarks(Type type) => Read(type, type, "remarks");

    public static DocText Summary(PropertyInfo property) => Read(property, property.ReflectedType ?? property.DeclaringType!, "summary");

    /// <summary>What a component's saved property does, from its <c>///</c> summary.</summary>
    public static DocText Summary(Scenes.Serialization.PropertyDescriptor property) => Summary(property.Info);

    /// <summary>What one of an enum's values means, from its <c>///</c> summary.</summary>
    public static DocText Summary(Enum value) =>
        value.GetType().GetField(value.ToString()) is { } field ? Read(field, field.DeclaringType!, "summary") : DocText.Empty;

    /// <summary>What a method does, from its <c>///</c> summary: a component's action (see <see cref="Scenes.ActionAttribute"/>).</summary>
    public static DocText Summary(MethodInfo method) => Read(method, method.ReflectedType ?? method.DeclaringType!, "summary");

    /// <summary>What a component's action does, from its method's <c>///</c> summary.</summary>
    public static DocText Summary(Scenes.ComponentAction action) => Summary(action.Method);

    private static DocText Read(MemberInfo member, Type context, string section)
    {
        // Overridden properties often say <inheritdoc/>, or nothing: use what the base says. A type only takes its base
        // type's docs when it asks to; an undocumented script isn't described by Behavior's docs.
        for (var m = member; m is not null; m = BaseOf(m))
        {
            var element = Element(m);
            if (element is null && m is Type)
                return DocText.Empty;
            if (element is null || element.Element("inheritdoc") is not null)
                continue;
            return element.Element(section) is { } found ? Convert(found, context, m.Module.Assembly) : DocText.Empty;
        }

        return DocText.Empty;
    }

    private static MemberInfo? BaseOf(MemberInfo member) => member switch
    {
        Type type => type.BaseType is { } b && b != typeof(object) ? b : null,
        PropertyInfo property => property.DeclaringType?.BaseType?.GetProperty(property.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
        MethodInfo method when method.GetBaseDefinition() is var root && root != method => root,
        _ => null,
    };

    private static XElement? Element(MemberInfo member)
    {
        var docs = Members.GetValue(member.Module.Assembly, Load);
        var id = member switch
        {
            Type type => "T:" + TypeId(type),
            PropertyInfo property => $"P:{TypeId(property.DeclaringType!)}.{property.Name}",
            FieldInfo field => $"F:{TypeId(field.DeclaringType!)}.{field.Name}",
            MethodInfo method => $"M:{TypeId(method.DeclaringType!)}.{method.Name}{Parameters(method)}",
            _ => null,
        };
        return id is not null ? docs.GetValueOrDefault(id) : null;
    }

    private static string TypeId(Type type) => type.FullName!.Replace('+', '.');

    // "(System.Single)", or nothing for a method that takes nothing, as the compiler writes a method's documentation id.
    private static string Parameters(MethodInfo method)
    {
        var parameters = method.GetParameters();
        return parameters.Length == 0 ? "" : $"({string.Join(",", parameters.Select(p => TypeId(p.ParameterType)))})";
    }

    private static IReadOnlyDictionary<string, XElement> Load(Assembly assembly)
    {
        try
        {
            var path = string.IsNullOrEmpty(assembly.Location) ? null : Path.ChangeExtension(assembly.Location, ".xml");
            return path is not null && File.Exists(path) ? Parse(File.ReadAllText(path)) : new Dictionary<string, XElement>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new Dictionary<string, XElement>();
        }
    }

    private static Dictionary<string, XElement> Parse(string? xml)
    {
        var members = new Dictionary<string, XElement>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(xml))
            return members;
        foreach (var member in XDocument.Parse(xml).Descendants("member"))
        {
            if (member.Attribute("name")?.Value is { } name)
                members[name] = member;
        }

        return members;
    }

    // ----- XML to runs -----

    private static DocText Convert(XElement section, Type context, Assembly assembly)
    {
        var builder = new Builder(context, assembly);
        builder.Add(section);
        return new DocText(builder.Finish());
    }

    private sealed class Builder(Type context, Assembly assembly)
    {
        private readonly List<IReadOnlyList<DocRun>> _paragraphs = [];
        private List<DocRun> _current = [];

        public void Add(XElement element)
        {
            foreach (var node in element.Nodes())
            {
                switch (node)
                {
                    case XText text:
                        Text(text.Value, DocRunKind.Text);
                        break;
                    case XElement e:
                        AddElement(e);
                        break;
                }
            }
        }

        public List<IReadOnlyList<DocRun>> Finish()
        {
            Break();
            return _paragraphs;
        }

        private void AddElement(XElement e)
        {
            switch (e.Name.LocalName)
            {
                case "para":
                    Break();
                    Add(e);
                    Break();
                    break;
                case "br":
                    Break();
                    break;
                case "c":
                    Text(e.Value, DocRunKind.Code);
                    break;
                case "code":
                    Break();
                    foreach (var line in Dedent(e.Value))
                        _paragraphs.Add([new DocRun(line, DocRunKind.CodeBlock)]);
                    break;
                case "see" or "seealso":
                    if (e.Attribute("langword")?.Value is { } word)
                        Text(word, DocRunKind.Code);
                    else if (e.Attribute("cref")?.Value is { } cref)
                        Reference(cref, e.Value);
                    else
                        Text(e.Value.Length > 0 ? e.Value : e.Attribute("href")?.Value ?? "", DocRunKind.Text);
                    break;
                case "paramref" or "typeparamref":
                    Text(e.Attribute("name")?.Value ?? "", DocRunKind.Code);
                    break;
                case "list":
                    Break();
                    foreach (var item in e.Elements("item"))
                    {
                        Text("• ", DocRunKind.Text);
                        Add(item.Element("description") ?? item);
                        Break();
                    }
                    break;
                default:
                    Add(e);
                    break;
            }
        }

        // "T:Griff.Scenes.Health" reads "Health"; "P:Griff.Scenes.Health.Max" reads "Health.Max", or "Max" inside Health.
        private void Reference(string cref, string text)
        {
            var kind = cref.Length > 2 && cref[1] == ':' ? cref[0] : 'T';
            var name = GenericArity().Replace(cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref, "");
            var paren = name.IndexOf('(');
            if (paren >= 0)
                name = name[..paren];

            var typeName = kind == 'T' ? name : name[..Math.Max(0, name.LastIndexOf('.'))];
            var member = kind == 'T' ? null : name[(name.LastIndexOf('.') + 1)..];
            var type = FindType(typeName);
            var shortType = type?.Name ?? typeName[(typeName.LastIndexOf('.') + 1)..];

            var shown = text.Length > 0 ? text
                : member is null ? shortType
                : type == context ? member
                : $"{shortType}.{member}";
            if (member == "#ctor")
                shown = shortType;
            AddRun(new DocRun(shown, DocRunKind.Reference, type == context && member is not null ? null : type));
        }

        private Type? FindType(string name) =>
            name.Length == 0 ? null : assembly.GetType(name) ?? typeof(DocComments).Assembly.GetType(name) ?? NestedType(name);

        // XML ids write nested types with dots; reflection wants a plus.
        private Type? NestedType(string name)
        {
            var dot = name.LastIndexOf('.');
            return dot < 0 ? null : FindType(name[..dot])?.GetNestedType(name[(dot + 1)..]);
        }

        private void Text(string text, DocRunKind kind)
        {
            if (kind == DocRunKind.Code)
            {
                AddRun(new DocRun(text.Trim(), kind));
                return;
            }

            AddRun(new DocRun(Whitespace().Replace(text, " "), kind));
        }

        private void AddRun(DocRun run)
        {
            if (run.Text.Length == 0)
                return;
            // Merge plain text, and drop spaces at the start of a paragraph.
            if (run.Kind == DocRunKind.Text && _current.Count == 0)
                run = run with { Text = run.Text.TrimStart() };
            if (run.Text.Length == 0)
                return;
            if (run.Kind == DocRunKind.Text && _current.Count > 0 && _current[^1].Kind == DocRunKind.Text)
                _current[^1] = _current[^1] with { Text = _current[^1].Text + run.Text };
            else
                _current.Add(run);
        }

        private void Break()
        {
            if (_current.Count > 0 && _current[^1].Kind == DocRunKind.Text)
                _current[^1] = _current[^1] with { Text = _current[^1].Text.TrimEnd() };
            _current.RemoveAll(r => r.Text.Length == 0);
            if (_current.Count > 0)
                _paragraphs.Add(_current);
            _current = [];
        }

        private static IEnumerable<string> Dedent(string code)
        {
            var lines = code.ReplaceLineEndings("\n").Split('\n').ToList();
            while (lines.Count > 0 && lines[0].Trim().Length == 0)
                lines.RemoveAt(0);
            while (lines.Count > 0 && lines[^1].Trim().Length == 0)
                lines.RemoveAt(lines.Count - 1);
            var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            return lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart());
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"`+\d+")]
    private static partial Regex GenericArity();
}
