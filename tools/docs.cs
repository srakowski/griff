#:sdk Microsoft.NET.Sdk
#:project ../src/Griff.Editor/Griff.Editor.csproj
#:property TargetFramework=net10.0-windows
#:property UseWPF=true
#:property PublishAot=false
#:property PublishTrimmed=false
#:property ValidateExecutableReferencesMatchSelfContained=false
#:property TreatWarningsAsErrors=false
#:property EnforceCodeStyleInBuild=false
#:property AnalysisLevel=none
#:property NoWarn=$(NoWarn);CS8321

// The website's reference pages, written from the code itself so a new component or command appears by itself: the
// components (what component_describe says about each: ComponentDescription, from ComponentRegistry and the XML docs), the
// editor's commands (the CommandRegistry the MCP server projects into tools) and the scripting API (the public types
// scripts use, with their members' XML docs). Run by `npm run reference` in site/ and by the Site workflow:
//   dotnet run -p:SkipPlayers=true tools/docs.cs -- [--out <folder>]
// The output folder defaults to site/src/content/docs/reference; it is git-ignored and made again on every run.
// Windows only: it builds the editor's real services (WPF) the way tests/Griff.Editor.Tests does.
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Griff;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor;
using Griff.Editor.Core.Commands;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var root = AppContext.BaseDirectory;
var repo = FindRepo(root);
var outIndex = Array.IndexOf(args, "--out");
var outDir = outIndex >= 0 && outIndex + 1 < args.Length
    ? Path.GetFullPath(args[outIndex + 1])
    : Path.Combine(repo, "site", "src", "content", "docs", "reference");

if (Directory.Exists(outDir))
    Directory.Delete(outDir, recursive: true);
Directory.CreateDirectory(outDir);

// The editor's services, as the tests build them: every module, on a thread WPF is happy with.
Exception? failure = null;
Reference.Page[] pages = [];
var thread = new Thread(() =>
{
    try
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        App.ConfigureCoreServices(services);
        foreach (var module in App.Modules)
            module.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        pages = Reference.Build(provider).ToArray();
    }
    catch (Exception ex)
    {
        failure = ex;
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
if (failure is not null)
    ExceptionDispatchInfo.Throw(failure);

foreach (var page in pages)
{
    var path = Path.Combine(outDir, page.Path.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, page.Text, new UTF8Encoding(false));
}

Console.WriteLine($"Wrote {pages.Length} reference pages to {outDir}");
return 0;

static string FindRepo(string start)
{
    for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "griff.slnx")))
            return dir.FullName;
    }

    // Run from the repository: the current folder or the one the tool is in.
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "griff.slnx")))
            return dir.FullName;
    }

    throw new InvalidOperationException("Run this from inside the Griff repository.");
}

static class Reference
{
    public sealed record Page(string Path, string Text);

    // The namespaces scripts see without a using, and the genres' (a script can use any of them).
    private static bool InApi(Type type) =>
        type.IsPublic && !type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false) &&
        type.Namespace is { } ns &&
        (ns is "Griff" or "Griff.Input" or "Griff.Runtime" or "Griff.Scenes" or "Griff.Simulations" || ns.StartsWith("Griff.Modules.", StringComparison.Ordinal));

    // The types that have a page of their own in the component reference.
    private static HashSet<Type> componentTypes = [];

    public static IEnumerable<Page> Build(IServiceProvider services)
    {
        var apiTypes = typeof(Component).Assembly.GetTypes().Where(InApi)
            .OrderBy(t => t.Namespace, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.Ordinal).ToList();
        var slugs = Slugs(apiTypes);

        var registry = services.GetRequiredService<Griff.Editor.Core.Workspace.IProjectSession>().Components;
        componentTypes = ComponentCatalog.Build(registry).Select(e => e.Descriptor.Type).ToHashSet();
        foreach (var page in Components(registry, slugs))
            yield return page;
        yield return Commands(services);
        foreach (var page in Api(apiTypes, slugs))
            yield return page;
    }

    // ----- Components -----

    private static IEnumerable<Page> Components(ComponentRegistry registry, Dictionary<Type, string> slugs)
    {
        var entries = ComponentCatalog.Build(registry);
        var index = new StringBuilder();
        index.Append(Front("Components", "Every component Griff has: what it does, what it needs, and each property with its default and meaning. Written from the code."));
        index.AppendLine("Every component that can be added to an entity, by category: what it does and what each property takes. This page and the ones it links to are written from the code when the site is built (the same text `component_describe` gives an agent), so a new component appears here by itself.");
        index.AppendLine();
        foreach (var group in entries.GroupBy(e => e.Category))
        {
            index.AppendLine(CultureInfo.InvariantCulture, $"## {group.Key}").AppendLine();
            foreach (var entry in group)
            {
                var summary = entry.Summary.FirstSentence is { Length: > 0 } s ? Escape(s) : "(no description)";
                index.AppendLine(CultureInfo.InvariantCulture, $"- [{entry.Name}](/reference/components/{Lower(entry.Name)}/): {summary}");
            }

            index.AppendLine();
        }

        yield return new Page("components/index.md", index.ToString());

        foreach (var entry in entries)
        {
            var text = ComponentDescription.Of(registry, entry.Descriptor);
            var page = new StringBuilder();
            page.Append(Front(entry.Name, entry.Summary.FirstSentence is { Length: > 0 } s ? s : $"The {entry.Name} component."));
            page.Append(ComponentBody(text));
            if (slugs.TryGetValue(entry.Descriptor.Type, out var slug))
                page.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"Scripts use it as [`{entry.Descriptor.Type.Name}`](/reference/api/{slug}/): its methods, events and values that aren't saved with the scene.");
            yield return new Page($"components/{Lower(entry.Name)}.md", page.ToString());
        }
    }

    // component_describe's text as Markdown: the title is the page's, each line its own paragraph (or list item), the
    // property names code, and the groups' headings one level down.
    private static string ComponentBody(string text)
    {
        var result = new StringBuilder();
        var lines = text.Replace("\r", "").Split('\n');
        var previousList = false;
        var inActions = false;
        foreach (var raw in lines.Skip(1))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
                continue;
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                result.AppendLine().AppendLine("### " + Escape(line[3..])).AppendLine();
                previousList = false;
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var colon = line.IndexOf(": ", StringComparison.Ordinal);
                var space = line.IndexOf(' ', 2);
                var item = inActions && space > 2
                    ? "- `" + line[2..space].TrimEnd('.') + "`" + (line[2..space].EndsWith('.') ? "." : "") + Escape(line[space..])
                    : colon > 0 && colon < 60 && !line[2..colon].Contains(' ')
                    ? "- `" + line[2..colon] + "`: " + Escape(line[(colon + 2)..])
                    : "- " + Escape(line[2..]);
                if (!previousList)
                    result.AppendLine();
                result.AppendLine(item);
                previousList = true;
            }
            else if (line.StartsWith("Properties (key", StringComparison.Ordinal))
            {
                result.AppendLine().AppendLine("## Properties").AppendLine().AppendLine("Each is listed as its key in the scene file and the MCP tools, then its kind of value, its default and what it does.");
                previousList = false;
            }
            else if (line.StartsWith("Actions (", StringComparison.Ordinal))
            {
                result.AppendLine().AppendLine("## Actions").AppendLine().AppendLine(Escape(char.ToUpperInvariant(line[9]) + line[10..].TrimEnd(':')[..^1]) + ".");
                previousList = false;
                inActions = true;
                continue;
            }
            else if (line.StartsWith("Outputs (", StringComparison.Ordinal))
            {
                result.AppendLine().AppendLine("## Outputs").AppendLine().AppendLine(Escape(char.ToUpperInvariant(line[9]) + line[10..].TrimEnd(':')[..^1]) + ".");
                previousList = false;
                inActions = true;
                continue;
            }
            else if (line.StartsWith("While the game runs", StringComparison.Ordinal))
            {
                inActions = false;
                result.AppendLine().AppendLine("## While the game runs").AppendLine().AppendLine("Not saved with the scene; scripts read them, and `entity_get` shows them as `live` while the game plays.");
                previousList = false;
            }
            else
            {
                result.AppendLine().AppendLine(Escape(line));
                previousList = false;
            }
        }

        return result.ToString();
    }

    // ----- Commands -----

    private static Page Commands(IServiceProvider services)
    {
        var commands = services.GetRequiredService<CommandRegistry>().All.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var menus = services.GetServices<MenuDefinition>().OrderBy(m => m.Order).ToList();

        var page = new StringBuilder();
        page.Append(Front("Editor commands", "Every command in Griff's editor: its id, what it does and its shortcut. Written from the code."));
        page.AppendLine("Everything a person does in the editor's menus and toolbar is a command. Each is also a tool an agent can call over the [Model Context Protocol](/docs/agents/), named by the command's id with its dots turned to underscores (`Game.Play` is `Game_Play`). This page is written from the code when the site is built, so a new command appears here by itself.");
        page.AppendLine();

        void Table(string heading, IEnumerable<EditorCommand> list)
        {
            var rows = list.ToList();
            if (rows.Count == 0)
                return;
            page.AppendLine("## " + heading).AppendLine();
            page.AppendLine("| Command | Shortcut | Id | What it does |").AppendLine("| --- | --- | --- | --- |");
            foreach (var command in rows)
            {
                var summary = DocComments.Summary(command.DescribedBy).PlainText.Replace("\n\n", " ").Replace('\n', ' ');
                var shortcut = command.GestureText is { Length: > 0 } g ? "`" + g + "`" : "";
                // What makes things is listed by its category, as the Entity menu's submenus have it: "Characters > Grid Enemy",
                // "Genres > Match-3 > Match Board".
                var name = command.Category is { } category ? $"{CommandCategories.MenuPath(category)} > {CommandCategories.ItemText(command).Replace("_", "")}" : command.DisplayText;
                page.AppendLine(CultureInfo.InvariantCulture, $"| {Cell(name)} | {shortcut} | `{command.Id}` | {Cell(summary)} |");
            }

            page.AppendLine();
        }

        foreach (var menu in menus)
        {
            Table(menu.Text.Replace("_", "") + " menu", commands.Where(c => c.Placement?.Menu == menu.Id)
                .OrderBy(c => c.Placement!.Group)
                .ThenBy(c => c.Category is { } category ? CommandCategories.Rank(category) : -1).ThenBy(c => c.Category, StringComparer.Ordinal)
                .ThenBy(c => c.Placement!.Order).ThenBy(c => c.DisplayText, StringComparer.Ordinal));
        }

        var known = menus.Select(m => m.Id).ToHashSet();
        Table("Other menus", commands.Where(c => c.Placement is { } p && !known.Contains(p.Menu)).OrderBy(c => c.Placement!.Menu).ThenBy(c => c.DisplayText, StringComparer.Ordinal));
        Table("Toolbar only", commands.Where(c => c.Placement is null && c.Toolbar is not null));
        Table("Shortcuts and agents only", commands.Where(c => c.Placement is null && c.Toolbar is null));
        return new Page("commands.md", page.ToString());
    }

    // Page addresses are lower case (that is how the site makes them from the file names).
    private static string Lower(string name) => name.ToLowerInvariant();

    private static string Cell(string text) => Escape(text).Replace("|", "\\|");

    // ----- The scripting API -----

    private static Dictionary<Type, string> Slugs(List<Type> types)
    {
        var slugs = new Dictionary<Type, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            var name = type.Name.Contains('`') ? type.Name[..type.Name.IndexOf('`')] + "-" + type.GetGenericArguments().Length : type.Name;
            if (!used.Add(name))
                name = (type.Namespace ?? "").Replace('.', '-') + "-" + name;
            used.Add(name);
            slugs[type] = Lower(name);
        }

        return slugs;
    }

    private static IEnumerable<Page> Api(List<Type> types, Dictionary<Type, string> slugs)
    {
        var xml = LoadXml(typeof(Component).Assembly);
        var index = new StringBuilder();
        index.Append(Front("Scripting API", "The types a script uses: Game, Input, Audio, Behavior and the rest, with their members' documentation. Written from the code."));
        index.AppendLine("The public types of Griff that scripts use, with every public and protected member and what its `///` documentation says. A script sees the `Griff`, `Griff.Input`, `Griff.Runtime` and `Griff.Scenes` namespaces without a `using`; the genres' types are under `Griff.Modules`. This page and the ones it links to are written from the code when the site is built. How scripts are written is in the [scripting guide](/docs/scripting/).");
        index.AppendLine();
        foreach (var group in types.GroupBy(t => t.Namespace))
        {
            index.AppendLine("## `" + group.Key + "`").AppendLine();
            foreach (var type in group)
            {
                var summary = Doc(xml, "T:" + TypeId(type), "summary", slugs);
                var first = FirstSentence(summary);
                index.AppendLine(CultureInfo.InvariantCulture, $"- [{Escape(TypeTitle(type))}](/reference/api/{slugs[type]}/){(first.Length > 0 ? ": " + first : "")}");
            }

            index.AppendLine();
        }

        yield return new Page("api/index.md", index.ToString());

        foreach (var type in types)
            yield return new Page($"api/{slugs[type]}.md", TypePage(type, xml, slugs));
    }

    private static string TypePage(Type type, Dictionary<string, XElement> xml, Dictionary<Type, string> slugs)
    {
        var summary = Doc(xml, "T:" + TypeId(type), "summary", slugs);
        var page = new StringBuilder();
        page.Append(Front(TypeTitle(type), FirstSentence(summary) is { Length: > 0 } f ? Plain(f) : $"The {type.Name} type."));
        var kind = type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "struct" : type.IsAbstract && type.IsSealed ? "static class" : type.IsAbstract ? "abstract class" : type.IsSealed ? "sealed class" : "class";
        page.AppendLine("```csharp").AppendLine($"namespace {type.Namespace};").AppendLine();
        page.AppendLine($"public {kind} {TypeName(type, true)}{Bases(type)}").AppendLine("```").AppendLine();
        if (summary.Length > 0)
            page.AppendLine(summary).AppendLine();
        if (Doc(xml, "T:" + TypeId(type), "remarks", slugs) is { Length: > 0 } remarks)
            page.AppendLine(remarks).AppendLine();
        if (componentTypes.Contains(type))
            page.AppendLine(CultureInfo.InvariantCulture, $"This is a component: its saved properties are on its [component page](/reference/components/{Lower(type.Name)}/).").AppendLine();

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        bool Visible(MemberInfo m) => m switch
        {
            MethodBase b => (b.IsPublic || b.IsFamily || b.IsFamilyOrAssembly) && !b.IsSpecialName && !b.Name.StartsWith("<", StringComparison.Ordinal),
            FieldInfo f => (f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly) && !f.IsSpecialName,
            PropertyInfo p => (p.GetMethod ?? p.SetMethod) is { } a && (a.IsPublic || a.IsFamily || a.IsFamilyOrAssembly),
            EventInfo e => e.AddMethod is { } a && (a.IsPublic || a.IsFamily || a.IsFamilyOrAssembly),
            _ => false,
        };

        if (type.IsEnum)
        {
            page.AppendLine("## Values").AppendLine();
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var doc = Doc(xml, "F:" + TypeId(type) + "." + field.Name, "summary", slugs);
                page.AppendLine(CultureInfo.InvariantCulture, $"- `{field.Name}` = {System.Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture)}{(doc.Length > 0 ? ": " + doc : "")}");
            }

            return page.ToString();
        }

        var context = new NullabilityInfoContext();
        Section("Constructors", type.GetConstructors(all).Where(Visible).OrderBy(c => c.GetParameters().Length), m => ("Constructor", Signature((ConstructorInfo)m, context), "M:" + TypeId(type) + ".#ctor" + ParamIds((MethodBase)m)));
        Section("Properties", type.GetProperties(all).Where(Visible).OrderBy(p => p.Name, StringComparer.Ordinal), m => (m.Name, Signature((PropertyInfo)m, context), "P:" + TypeId(type) + "." + m.Name + (((PropertyInfo)m).GetIndexParameters() is { Length: > 0 } ip ? "(" + string.Join(",", ip.Select(p => DocId(p.ParameterType))) + ")" : "")));
        Section("Methods", type.GetMethods(all).Where(Visible).OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.GetParameters().Length), m => (m.Name, Signature((MethodInfo)m, context), "M:" + TypeId(type) + "." + m.Name + (((MethodInfo)m).IsGenericMethodDefinition ? "``" + ((MethodInfo)m).GetGenericArguments().Length : "") + ParamIds((MethodBase)m)));
        Section("Events", type.GetEvents(all).Where(Visible).OrderBy(e => e.Name, StringComparer.Ordinal), m => (m.Name, Signature((EventInfo)m, context), "E:" + TypeId(type) + "." + m.Name));
        Section("Fields", type.GetFields(all).Where(Visible).OrderBy(f => f.Name, StringComparer.Ordinal), m => (m.Name, Signature((FieldInfo)m, context), "F:" + TypeId(type) + "." + m.Name));
        return page.ToString();

        void Section(string heading, IEnumerable<MemberInfo> members, Func<MemberInfo, (string Name, string Signature, string Id)> describe)
        {
            var list = members.ToList();
            if (list.Count == 0)
                return;
            page.AppendLine("## " + heading).AppendLine();
            foreach (var member in list)
            {
                var (name, signature, id) = describe(member);
                page.AppendLine("### " + (name == "Constructor" ? TypeName(type, false) : Escape(name))).AppendLine();
                page.AppendLine("```csharp").AppendLine(signature).AppendLine("```").AppendLine();
                if (Doc(xml, id, "summary", slugs) is { Length: > 0 } text)
                    page.AppendLine(text).AppendLine();
                if (member is MethodBase method)
                {
                    foreach (var parameter in method.GetParameters())
                    {
                        if (Doc(xml, id, "param", slugs, parameter.Name) is { Length: > 0 } p)
                            page.AppendLine(CultureInfo.InvariantCulture, $"- `{parameter.Name}`: {p}");
                    }

                    if (method.GetParameters().Any(p => Doc(xml, id, "param", slugs, p.Name) is { Length: > 0 }))
                        page.AppendLine();
                }

                if (Doc(xml, id, "returns", slugs) is { Length: > 0 } returns)
                    page.AppendLine("Returns " + char.ToLowerInvariant(returns[0]) + returns[1..]).AppendLine();
                if (Doc(xml, id, "remarks", slugs) is { Length: > 0 } remarks)
                    page.AppendLine(remarks).AppendLine();
            }
        }
    }

    private static string Bases(Type type)
    {
        var parts = new List<string>();
        if (type.BaseType is { } b && b != typeof(object) && b != typeof(ValueType) && b != typeof(Enum))
            parts.Add(TypeName(b, false));
        parts.AddRange(type.GetInterfaces().Where(i => i.IsPublic && i.Namespace?.StartsWith("Griff", StringComparison.Ordinal) == true).Select(i => TypeName(i, false)));
        return parts.Count == 0 ? "" : " : " + string.Join(", ", parts);
    }

    // ----- Signatures -----

    private static string Access(MethodBase m) => m.IsPublic ? "public" : m.IsFamilyOrAssembly ? "protected internal" : "protected";

    private static string Signature(ConstructorInfo c, NullabilityInfoContext context) =>
        $"{Access(c)} {TypeName(c.DeclaringType!, false)}({Parameters(c, context)})";

    private static string Signature(MethodInfo m, NullabilityInfoContext context)
    {
        var modifiers = new StringBuilder(Access(m)).Append(' ');
        if (m.IsStatic)
            modifiers.Append("static ");
        else if (m.IsAbstract)
            modifiers.Append("abstract ");
        else if (m.GetBaseDefinition() != m)
            modifiers.Append(m.IsFinal ? "sealed override " : "override ");
        else if (m.IsVirtual && !m.IsFinal && !m.DeclaringType!.IsInterface)
            modifiers.Append("virtual ");
        var generics = m.IsGenericMethodDefinition ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">" : "";
        return $"{modifiers}{WithNullability(m.ReturnType, () => context.Create(m.ReturnParameter))} {m.Name}{generics}({Parameters(m, context)})";
    }

    private static string Signature(PropertyInfo p, NullabilityInfoContext context)
    {
        var access = p.GetMethod ?? p.SetMethod!;
        var modifiers = Access(access) + (access.IsStatic ? " static" : access.IsAbstract ? " abstract" : access.GetBaseDefinition() != access ? " override" : access.IsVirtual && !access.IsFinal ? " virtual" : "");
        var accessors = new StringBuilder("{ ");
        string Accessor(MethodInfo? m, string word) =>
            m is null || !(m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly) ? "" : (Access(m) == Access(access) ? "" : Access(m) + " ") + word + "; ";
        accessors.Append(Accessor(p.GetMethod, "get"));
        accessors.Append(p.SetMethod is { } set && set.ReturnParameter.GetRequiredCustomModifiers().Any(t => t.Name == "IsExternalInit") ? Accessor(set, "init") : Accessor(p.SetMethod, "set"));
        accessors.Append('}');
        var index = p.GetIndexParameters() is { Length: > 0 } ip ? "[" + string.Join(", ", ip.Select(x => TypeName(x.ParameterType, false) + " " + x.Name)) + "]" : "";
        return $"{modifiers} {WithNullability(p.PropertyType, () => context.Create(p))} {(index.Length > 0 ? "this" : p.Name)}{index} {accessors}";
    }

    private static string Signature(FieldInfo f, NullabilityInfoContext context)
    {
        var access = f.IsPublic ? "public" : f.IsFamilyOrAssembly ? "protected internal" : "protected";
        var modifiers = f.IsLiteral ? " const" : (f.IsStatic ? " static" : "") + (f.IsInitOnly ? " readonly" : "");
        return $"{access}{modifiers} {WithNullability(f.FieldType, () => context.Create(f))} {f.Name}";
    }

    private static string Signature(EventInfo e, NullabilityInfoContext context) =>
        $"{(e.AddMethod!.IsPublic ? "public" : "protected")}{(e.AddMethod.IsStatic ? " static" : "")} event {WithNullability(e.EventHandlerType!, () => context.Create(e))} {e.Name}";

    private static string Parameters(MethodBase m, NullabilityInfoContext context) =>
        string.Join(", ", m.GetParameters().Select(p =>
        {
            var type = p.ParameterType;
            var prefix = p.IsOut ? "out " : type.IsByRef ? (p.IsIn ? "in " : "ref ") : p.IsDefined(typeof(ParamArrayAttribute), false) ? "params " : "";
            if (type.IsByRef)
                type = type.GetElementType()!;
            var text = prefix + WithNullability(type, () => context.Create(p)) + " " + p.Name;
            if (p.HasDefaultValue)
                text += " = " + DefaultValue(p.DefaultValue, type);
            return text;
        }));

    private static string DefaultValue(object? value, Type type) => value switch
    {
        null => type.IsValueType ? "default" : "null",
        string s => JsonSerializer.Serialize(s),
        bool b => b ? "true" : "false",
        float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        Enum e => TypeName(e.GetType(), false) + "." + e,
        char c => "'" + c + "'",
        IFormattable fmt => fmt.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "default",
    };

    private static string WithNullability(Type type, Func<NullabilityInfo> info)
    {
        var text = TypeName(type, false);
        if (type.IsValueType || type.IsPointer || text.EndsWith('?'))
            return text;
        try
        {
            if (info().ReadState == NullabilityState.Nullable)
                return text + "?";
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
        }

        return text;
    }

    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(void)] = "void", [typeof(int)] = "int", [typeof(uint)] = "uint", [typeof(long)] = "long", [typeof(ulong)] = "ulong",
        [typeof(short)] = "short", [typeof(ushort)] = "ushort", [typeof(byte)] = "byte", [typeof(sbyte)] = "sbyte",
        [typeof(float)] = "float", [typeof(double)] = "double", [typeof(decimal)] = "decimal", [typeof(bool)] = "bool",
        [typeof(char)] = "char", [typeof(string)] = "string", [typeof(object)] = "object",
    };

    private static string TypeName(Type type, bool declaration)
    {
        if (Keywords.TryGetValue(type, out var keyword))
            return keyword;
        if (type.IsByRef)
            return TypeName(type.GetElementType()!, declaration);
        if (type.IsArray)
            return TypeName(type.GetElementType()!, declaration) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsGenericParameter)
            return type.Name;
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return TypeName(inner, declaration) + "?";
        var name = type.Name.Contains('`') ? type.Name[..type.Name.IndexOf('`')] : type.Name;
        if (type.IsNested && !declaration)
            name = TypeName(type.DeclaringType!, false) + "." + name;
        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();
            // A nested type repeats its declaring type's generic arguments; only its own are written.
            var own = type.IsNested ? arguments.Skip(type.DeclaringType!.GetGenericArguments().Length).ToArray() : arguments;
            if (own.Length > 0)
                name += "<" + string.Join(", ", own.Select(a => TypeName(a, false))) + ">";
        }

        return name;
    }

    private static string TypeTitle(Type type) => TypeName(type, true);

    // ----- XML documentation -----

    private static Dictionary<string, XElement> LoadXml(System.Reflection.Assembly assembly)
    {
        var path = Path.ChangeExtension(assembly.Location, ".xml");
        if (!File.Exists(path))
            throw new FileNotFoundException("Griff.xml isn't beside Griff.dll; the documentation file is generated by building Griff.", path);
        return XDocument.Load(path).Descendants("member").Where(m => m.Attribute("name") is not null)
            .GroupBy(m => m.Attribute("name")!.Value).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    private static string TypeId(Type type) => type.FullName!.Replace('+', '.');

    // The id an XML documentation file gives a type used as a parameter.
    private static string DocId(Type type)
    {
        if (type.IsByRef)
            return DocId(type.GetElementType()!) + "@";
        if (type.IsPointer)
            return DocId(type.GetElementType()!) + "*";
        if (type.IsArray)
            return DocId(type.GetElementType()!) + "[" + (type.GetArrayRank() > 1 ? string.Join(",", Enumerable.Repeat("0:", type.GetArrayRank())) : "") + "]";
        if (type.IsGenericParameter)
            return (type.DeclaringMethod is not null ? "``" : "`") + type.GenericParameterPosition;
        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var name = definition.FullName![..definition.FullName!.IndexOf('`')].Replace('+', '.');
            return name + "{" + string.Join(",", type.GetGenericArguments().Select(DocId)) + "}";
        }

        return TypeId(type);
    }

    private static string ParamIds(MethodBase method) =>
        method.GetParameters().Length == 0 ? "" : "(" + string.Join(",", method.GetParameters().Select(p => DocId(p.ParameterType))) + ")";

    // A member's documentation (or one of its params) as Markdown; empty when it has none.
    private static string Doc(Dictionary<string, XElement> xml, string id, string section, Dictionary<Type, string> slugs, string? param = null)
    {
        if (!xml.TryGetValue(id, out var member))
            return "";
        if (member.Element("inheritdoc") is not null)
            return "";
        var element = section == "param" ? member.Elements("param").FirstOrDefault(e => (string?)e.Attribute("name") == param) : member.Element(section);
        if (element is null)
            return "";
        var builder = new StringBuilder();
        Render(element, builder, slugs);
        return Regex.Replace(builder.ToString().Trim(), @"[ \t]*\n[ \t]*(?=\n)", "").Replace("\r", "");
    }

    private static void Render(XElement element, StringBuilder text, Dictionary<Type, string> slugs)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText t:
                    text.Append(Escape(Regex.Replace(t.Value, @"\s+", " ")));
                    break;
                case XElement e:
                    switch (e.Name.LocalName)
                    {
                        case "c":
                        case "paramref":
                        case "typeparamref":
                            text.Append('`').Append(e.Name.LocalName == "c" ? e.Value.Trim() : (string?)e.Attribute("name")).Append('`');
                            break;
                        case "see":
                        case "seealso":
                            See(e, text, slugs);
                            break;
                        case "para":
                            text.Append("\n\n");
                            Render(e, text, slugs);
                            text.Append("\n\n");
                            break;
                        case "br":
                            text.Append("\n\n");
                            break;
                        case "code":
                            text.Append("\n\n```csharp\n").Append(Dedent(e.Value)).Append("\n```\n\n");
                            break;
                        case "list":
                            text.Append("\n\n");
                            foreach (var item in e.Elements("item"))
                            {
                                text.Append("- ");
                                var inner = new StringBuilder();
                                Render(item.Element("description") ?? item, inner, slugs);
                                text.Append(inner.ToString().Trim().Replace("\n\n", " ")).Append('\n');
                            }

                            text.Append('\n');
                            break;
                        default:
                            Render(e, text, slugs);
                            break;
                    }

                    break;
            }
        }
    }

    private static void See(XElement e, StringBuilder text, Dictionary<Type, string> slugs)
    {
        var cref = (string?)e.Attribute("cref");
        if (cref is null)
        {
            text.Append(Escape((string?)e.Attribute("langword") ?? e.Value));
            return;
        }

        var name = cref[2..];
        var paren = name.IndexOf('(', StringComparison.Ordinal);
        if (paren >= 0)
            name = name[..paren];
        var shown = name[(name.LastIndexOf('.') + 1)..];
        var generic = shown.IndexOf('`', StringComparison.Ordinal);
        if (generic >= 0)
            shown = shown[..generic];
        var type = cref.StartsWith("T:", StringComparison.Ordinal) ? slugs.Keys.FirstOrDefault(t => TypeId(t) == name) : null;
        text.Append(type is null ? $"`{shown}`" : $"[`{shown}`](/reference/api/{slugs[type]}/)");
    }

    private static string Dedent(string code)
    {
        var lines = code.Replace("\r", "").Split('\n').SkipWhile(string.IsNullOrWhiteSpace).Reverse().SkipWhile(string.IsNullOrWhiteSpace).Reverse().ToList();
        var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
    }

    private static string FirstSentence(string markdown)
    {
        var paragraph = markdown.Split("\n\n")[0].Replace('\n', ' ');
        var end = Regex.Match(paragraph, @"[.!?](?=\s+[A-Z(""])");
        return end.Success ? paragraph[..(end.Index + 1)] : paragraph;
    }

    // ----- Markdown -----

    // Page front matter (JSON strings are valid YAML strings).
    private static string Front(string title, string description) =>
        $"---\ntitle: {JsonSerializer.Serialize(title)}\ndescription: {JsonSerializer.Serialize(Plain(description))}\n---\n\n";

    private static string Plain(string markdown) => Regex.Replace(markdown.Replace("\\", ""), @"\[`?([^\]`]+)`?\]\([^)]*\)|`", "$1").Replace("&lt;", "<");

    // Documentation text is prose with the odd <T> or * in it: keep Markdown and HTML from reading them as markup.
    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("<", "&lt;").Replace("*", "\\*").Replace("`", "\\`").Replace("{", "\\{");
}
