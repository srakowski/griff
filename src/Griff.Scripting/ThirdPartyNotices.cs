using System.Text;
using Griff.Fonts;
using Griff.Runtime;

namespace Griff.Scripting;

/// <summary>
/// The notices for what other people made that a game carries: every export, desktop and web, writes them as
/// <see cref="FileName"/>. They're worked out from the files actually shipped (the files inside a single-file player, or
/// a web player's), so a package the player starts to carry can't go unlisted: a file no notice covers is listed in
/// <see cref="Notices.Unlisted"/> (and at the end of the file), which the export tests fail on.
/// </summary>
/// <remarks>
/// Griff's own files and the game's (its Assets, scripts and settings) need no notice: what a game maker puts in their
/// Assets, fonts included, is theirs to license.
/// </remarks>
public static class ThirdPartyNotices
{
    /// <summary>The notices file every export writes.</summary>
    public const string FileName = "THIRD-PARTY-NOTICES.txt";

    // Single-file bundles start their manifest from an offset just before this signature in the host.
    private static readonly byte[] BundleSignature =
    [
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae,
    ];

    // What the executable of a single-file bundle is called among its files: it's .NET's host, whatever the game's name.
    private const string SingleFileHost = "singlefilehost.exe";

    private static readonly Component[] Components =
    [
        new(".NET (the runtime, its libraries and WPF), by the .NET Foundation and Microsoft, under the MIT License",
            "System.*, Microsoft.*, mscorlib, netstandard, WindowsBase, Presentation*, UIAutomation*, ReachFramework, Accessibility, " +
            "DirectWriteForwarder, *_cor3 (WPF's native parts), dotnet.* and icudt* (in the browser), and the .exe's host",
            name => name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Presentation", StringComparison.OrdinalIgnoreCase) || name.StartsWith("UIAutomation", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("dotnet.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("icudt", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("_cor3", StringComparison.OrdinalIgnoreCase) ||
                    name is "System" or "dotnet" or "singlefilehost" or "mscorlib" or "netstandard" or "WindowsBase" or "ReachFramework" or "Accessibility" or "DirectWriteForwarder",
            () => [Read("dotnet-LICENSE.txt"), Read("dotnet-THIRD-PARTY-NOTICES.txt")]),
        new("NAudio, by Mark Heath, under the MIT License", "NAudio.*",
            name => name.StartsWith("NAudio.", StringComparison.OrdinalIgnoreCase), () => [Read("NAudio-LICENSE.txt")]),
        new("NVorbis, by Andrew Ward, under the MIT License", "NVorbis",
            name => name.Equals("NVorbis", StringComparison.OrdinalIgnoreCase), () => [Read("NVorbis-LICENSE.txt")]),
        new("Silk.NET, by the .NET Foundation and contributors, under the MIT License", "Silk.NET.*",
            name => name.StartsWith("Silk.NET.", StringComparison.OrdinalIgnoreCase), () => [Read("Silk.NET-LICENSE.txt")]),
        new("StbImageSharp and StbTrueTypeSharp, C# ports of Sean Barrett's stb_image and stb_truetype, in the public domain " +
            "(stb is offered under the MIT License or the Unlicense, as you prefer)", "StbImageSharp, StbTrueTypeSharp",
            name => name.Equals("StbImageSharp", StringComparison.OrdinalIgnoreCase) || name.Equals("StbTrueTypeSharp", StringComparison.OrdinalIgnoreCase),
            () => [Read("stb-LICENSE.txt")]),
        .. BuiltInFonts.All.Select(font => new Component(font.About, "Griff.Fonts (the font is inside it)",
            name => name.Equals("Griff.Fonts", StringComparison.OrdinalIgnoreCase), () => [font.License])),
    ];

    /// <summary>What a set of shipped files needs: the notices' text, and the files no notice covers.</summary>
    public sealed record Notices(string Text, IReadOnlyList<string> Unlisted);

    /// <summary>
    /// The notices for <paramref name="files"/>: paths relative to the export (or inside a bundle), with '/' or '\'
    /// between folders. Griff's own and the game's need none.
    /// </summary>
    public static Notices For(IEnumerable<string> files)
    {
        var used = new HashSet<Component>();
        var unlisted = new List<string>();
        foreach (var file in files)
        {
            var name = NameOf(file);
            var covering = Components.Where(c => c.Covers(name)).ToList();
            if (covering.Count > 0)
                used.UnionWith(covering);
            else if (!IsGriffsOrTheGames(file, name))
                unlisted.Add(file);
        }

        var text = new StringBuilder();
        text.AppendLine("Third-party notices");
        text.AppendLine("===================");
        text.AppendLine();
        text.AppendLine("This game was made with Griff. It carries software and fonts made by others, listed here with their");
        text.AppendLine("licenses. What the game's maker made (its pictures, sounds, music, fonts and scripts) is theirs.");
        foreach (var component in Components.Where(used.Contains))
        {
            text.AppendLine();
            text.AppendLine(new string('-', 78));
            text.AppendLine(component.Title);
            text.AppendLine("Files: " + component.Files);
            text.AppendLine(new string('-', 78));
            text.AppendLine();
            foreach (var license in component.Texts())
                text.AppendLine(license.Trim('﻿').TrimEnd());
        }

        if (unlisted.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(new string('-', 78));
            text.AppendLine("Also carried, with no notice on record (Griff's ThirdPartyNotices needs one for each):");
            foreach (var file in unlisted)
                text.AppendLine("  " + file);
        }

        return new Notices(text.ToString().ReplaceLineEndings("\r\n"), unlisted);
    }

    /// <summary>
    /// The files inside a single-file .NET executable (the player), from its bundle's manifest; just the executable for one
    /// that isn't a bundle.
    /// </summary>
    public static IReadOnlyList<string> FilesIn(string executable)
    {
        var files = new List<string> { Path.GetFileName(executable) };
        try
        {
            var data = File.ReadAllBytes(executable);
            var signature = data.AsSpan().IndexOf(BundleSignature);
            if (signature < 8)
                return files;

            // The executable itself is .NET's single-file host.
            files[0] = SingleFileHost;
            var header = BitConverter.ToInt64(data, signature - 8);
            if (header <= 0 || header >= data.Length)
                return files;
            using var reader = new BinaryReader(new MemoryStream(data, writable: false));
            reader.BaseStream.Position = header;
            var major = reader.ReadUInt32();
            reader.ReadUInt32();
            var count = reader.ReadInt32();
            reader.ReadString();
            if (major >= 2)
            {
                // Where the deps.json and runtimeconfig.json are, and the bundle's flags.
                for (var i = 0; i < 5; i++)
                    reader.ReadInt64();
            }

            for (var i = 0; i < count; i++)
            {
                reader.ReadInt64();
                reader.ReadInt64();
                if (major >= 6)
                    reader.ReadInt64();
                reader.ReadByte();
                files.Add(reader.ReadString());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or ArgumentException)
        {
        }

        return files;
    }

    // A file's name without its folders, its compressed copy's extension (.br, .gz) or its own (.dll, .wasm, .exe...).
    private static string NameOf(string file)
    {
        var name = Path.GetFileName(file.Replace('\\', '/').TrimEnd('/'));
        foreach (var compressed in new[] { ".br", ".gz" })
        {
            if (name.EndsWith(compressed, StringComparison.OrdinalIgnoreCase))
                name = name[..^compressed.Length];
        }

        var dot = name.LastIndexOf('.');
        name = dot > 0 ? name[..dot] : name;

        // A web player's files are fingerprinted: "StbTrueTypeSharp.qb2g6iiwq8.wasm".
        dot = name.LastIndexOf('.');
        if (dot > 0 && name.Length - dot - 1 == 10 && name[(dot + 1)..].All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)))
            name = name[..dot];
        return name;
    }

    // Griff's assemblies and pages, the settings a .NET build writes for it, and the game's own folder.
    private static bool IsGriffsOrTheGames(string file, string name)
    {
        var path = file.Replace('\\', '/');
        return name.Equals("Griff", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Griff.", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(ExportLayout.GameFolder + "/", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".deps", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".runtimeconfig", StringComparison.OrdinalIgnoreCase) ||
               name is "index" or "main" or "griff" or "THIRD-PARTY-NOTICES" || Path.GetFileName(path) == ExportLayout.Marker ||
               name.Equals(Path.GetFileNameWithoutExtension(ExportLayout.ScriptsAssembly), StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string fileName)
    {
        var name = "Griff.Scripting.Notices." + fileName;
        using var stream = typeof(ThirdPartyNotices).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"The notice {name} isn't in Griff.Scripting.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record Component(string Title, string Files, Func<string, bool> Covers, Func<IEnumerable<string>> Texts);
}
