using System.IO;
using Microsoft.CodeAnalysis;

namespace Griff.Editor.Features.Scripts.Language;

/// <summary>
/// Finds the XML documentation for an assembly scripts compile against, so the script editor can show what an API
/// does: Griff's own (Griff.xml next to Griff.dll) and, when the .NET SDK is installed, the reference pack's docs for
/// the System assemblies. Files load on first use.
/// </summary>
internal static class ScriptDocumentation
{
    private static readonly Lazy<string?> ReferencePack = new(FindReferencePack);

    public static DocumentationProvider? For(string assemblyPath)
    {
        var beside = Path.ChangeExtension(assemblyPath, ".xml");
        if (File.Exists(beside))
            return XmlDocumentationProvider.CreateFromFile(beside);
        if (ReferencePack.Value is not { } pack)
            return null;

        // The runtime's core types live in System.Private.CoreLib, documented under the public facades.
        var name = Path.GetFileNameWithoutExtension(assemblyPath);
        string[] files = name == "System.Private.CoreLib"
            ? ["System.Runtime.xml", "System.Numerics.Vectors.xml", "System.Collections.xml", "System.Runtime.Intrinsics.xml"]
            : [name + ".xml"];
        var paths = files.Select(f => Path.Combine(pack, f)).Where(File.Exists).ToList();
        return paths.Count == 0 ? null : new XmlFiles(paths);
    }

    // dotnet\shared\Microsoft.NETCore.App\10.0.x\ → dotnet\packs\Microsoft.NETCore.App.Ref\10.0.y\ref\net10.0\
    private static string? FindReferencePack()
    {
        try
        {
            var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location);
            var root = runtime is null ? null : Path.GetFullPath(Path.Combine(runtime, "..", "..", ".."));
            var packs = root is null ? null : Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref");
            if (packs is null || !Directory.Exists(packs))
                return null;
            var major = Environment.Version.Major;
            return Directory.EnumerateDirectories(packs)
                .Select(d => (Path: d, Version: Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v) ? v : null))
                .Where(d => d.Version?.Major == major)
                .OrderByDescending(d => d.Version)
                .Select(d => Path.Combine(d.Path, "ref", $"net{major}.0"))
                .FirstOrDefault(Directory.Exists);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Documentation from one or more XML doc files, each read the first time it's needed.</summary>
    private sealed class XmlFiles(IReadOnlyList<string> paths) : DocumentationProvider
    {
        private readonly IReadOnlyList<string> _paths = paths;
        private readonly Lazy<Dictionary<string, string>>[] _files = [.. paths.Select(p => new Lazy<Dictionary<string, string>>(() => Read(p)))];

        protected override string? GetDocumentationForSymbol(string documentationMemberID, System.Globalization.CultureInfo preferredCulture, CancellationToken cancellationToken = default)
        {
            foreach (var file in _files)
            {
                if (file.Value.TryGetValue(documentationMemberID, out var xml))
                    return xml;
            }

            return null;
        }

        private static Dictionary<string, string> Read(string path)
        {
            var members = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using var reader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore });
                reader.MoveToContent();
                while (!reader.EOF)
                {
                    if (reader is { NodeType: System.Xml.XmlNodeType.Element, Name: "member" } && reader.GetAttribute("name") is { } name)
                        members[name] = reader.ReadOuterXml();
                    else
                        reader.Read();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                // No docs is fine; tooltips just show signatures.
            }

            return members;
        }

        public override bool Equals(object? obj) => obj is XmlFiles other && _paths.SequenceEqual(other._paths);

        public override int GetHashCode() => _paths.Aggregate(17, (h, p) => h * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(p));
    }
}
