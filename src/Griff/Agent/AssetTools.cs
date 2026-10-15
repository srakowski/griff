using System.Text;

namespace Griff.Agent;

/// <summary>The open project's files, for the asset tools.</summary>
public interface IAgentProject
{
    /// <summary>The project's Assets folder, or null with no project open.</summary>
    string? AssetsPath { get; }

    /// <summary>
    /// Called after an agent writes or deletes a file (a path under Assets, with '/'), e.g. to compile a script.
    /// <paramref name="previousText"/> is a text file's text before it was written over (null if it's new, deleted or
    /// binary), e.g. so a prefab's copies keep what they changed from it. Returns anything the agent should hear about
    /// it, like how a script compiled.
    /// </summary>
    Task<AssetFeedback?> AssetChangedAsync(string assetPath, string? previousText = null);

    /// <summary>
    /// Called after an agent renames or moves a prefab (paths under Assets, with '/'), so its copies stay linked to it.
    /// Returns anything the agent should hear about it, like how many copies were relinked.
    /// </summary>
    Task<AssetFeedback?> AssetMovedAsync(string fromPath, string toPath) => Task.FromResult<AssetFeedback?>(null);
}

/// <summary>What came of writing a file: e.g. a script's compile errors (<see cref="IsError"/>) or "compiled".</summary>
public sealed record AssetFeedback(string Message, bool IsError = false);

/// <summary>
/// File tools for the project's Assets folder: list, read, write and delete scripts, scenes, prefabs, pictures and
/// sounds. Writing a script compiles it and says how that went; pictures can be read back as images.
/// </summary>
public sealed class AssetTools(IAgentProject project) : IAgentToolSource
{
    private static readonly string[] TextExtensions = [".cs", ".scene", ".prefab", ".table", ".sim", ".json", ".txt", ".md", ".griff", ".csv", ".xml", ".ini", ".shader", ".glsl"];
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];

    public IEnumerable<AgentTool> GetTools()
    {
        yield return new AgentTool
        {
            Name = "assets_list",
            Title = "List Assets",
            ReadOnly = true,
            Description = "Lists the files in the project's Assets folder (scripts, scenes, prefabs, tables, sprites, tiles, sounds, music), " +
                "with their sizes. Paths are relative to Assets, e.g. \"Sprites/hero.png\", which is how components name them.",
            InputSchema = Schema.Object([
                ("folder", Schema.String("Only this folder, e.g. \"Sprites\".")),
                ("pattern", Schema.String("Only file names matching this, e.g. \"*.cs\".")),
            ]),
            Invoke = args => Task.FromResult(List(args)),
        };

        yield return new AgentTool
        {
            Name = "asset_read",
            Title = "Read Asset",
            ReadOnly = true,
            Description = "Reads a file from Assets: text files as text (scripts, scenes, prefabs, tables), pictures as an image you can see.",
            InputSchema = Schema.Object([("path", Schema.String("The path under Assets, e.g. \"Scripts/Player.cs\"."))], "path"),
            Invoke = args => Task.FromResult(Read(args)),
        };

        yield return new AgentTool
        {
            Name = "asset_write",
            Title = "Write Asset",
            Destructive = true,
            Description = "Writes a file into Assets, making folders as needed and replacing any file already there: \"text\" for " +
                "scripts and other text, or \"base64\" for binary files like PNG pictures or WAV sounds. A C# script is compiled " +
                "straight away and swapped into the scene (and the running game); the answer says whether it built and lists any errors. " +
                "A table (.table: JSON with \"columns\", each { \"name\", \"type\" } with a type of " + string.Join(", ", Data.TableColumn.TypeNames) +
                " (a choice lists its \"choices\", a row names its \"table\"; a column worked out from the row and other tables has a " +
                "\"formula\", like \"=Base * 1.15 ^ (Level - 1)\", and no cells; one generated a row a level keeps its \"curve\", like " +
                "{ \"kind\": \"exponential\", \"first\": 100, \"last\": 50000, \"levels\": 50 }), and \"rows\", an object of rows by name, each an " +
                "object of its cells by column) is read straight away and reaches the running game; the answer says if it doesn't read, and " +
                "what's wrong with its formulas. A balance simulation (.sim: JSON with \"arena\", a scene; \"first\" and \"second\", each " +
                "{ \"entity\", \"table\", \"rows\" }; \"runs\", \"seed\", \"timeLimit\", \"rewards\", \"track\", \"sweep\" { \"table\", \"column\", \"from\", " +
                "\"to\", \"step\" } or \"script\") is read straight away; the answer says what's missing for it to run (Data_RunSimulation runs it).",
            InputSchema = Schema.Object([
                ("path", Schema.String("The path under Assets, e.g. \"Scripts/Spinner.cs\" or \"Sprites/hero.png\".")),
                ("text", Schema.String("The file's text.")),
                ("base64", Schema.String("The file's bytes, base64-encoded.")),
            ], "path"),
            Invoke = WriteAsync,
        };

        yield return new AgentTool
        {
            Name = "asset_delete",
            Title = "Delete Asset",
            Destructive = true,
            Description = "Deletes a file (or an empty folder) from Assets.",
            InputSchema = Schema.Object([("path", Schema.String("The path under Assets."))], "path"),
            Invoke = DeleteAsync,
        };

        yield return new AgentTool
        {
            Name = "asset_move",
            Title = "Move Asset",
            Destructive = true,
            Description = "Renames or moves a prefab within Assets (making folders as needed). Every placed copy stays linked to it: " +
                "in every scene, inside other prefabs, and in the open scene and prefab tabs. Its name is what Game.Spawn and " +
                "fields like a Spawner's Prefab use, so renaming it means changing those too.",
            InputSchema = Schema.Object([
                ("from", Schema.String("The prefab's path under Assets, e.g. \"Prefabs/Coin.prefab\".")),
                ("to", Schema.String("Its new path, e.g. \"Prefabs/Pickups/Gold Coin.prefab\".")),
            ], "from", "to"),
            Invoke = MoveAsync,
        };
    }

    private string Root => project.AssetsPath ?? throw new AgentException("No project is open.");

    /// <summary>The full path for a path under Assets; refuses anything that would leave the folder.</summary>
    public string FullPath(string assetPath)
    {
        var root = Path.GetFullPath(Root);
        var trimmed = assetPath.Replace('\\', '/').Trim().TrimStart('/');
        if (trimmed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["Assets/".Length..];
        var full = Path.GetFullPath(Path.Combine(root, trimmed));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && full != root)
            throw new AgentException("Paths have to stay inside the Assets folder.");
        return full;
    }

    private string AssetPathOf(string full) => Path.GetRelativePath(Root, full).Replace('\\', '/');

    private ToolResult List(ToolArgs args)
    {
        var folder = args.OptionalString("folder") is { } f ? FullPath(f) : Root;
        if (!Directory.Exists(folder))
            throw new AgentException($"There's no folder {args.OptionalString("folder")}.");
        var files = Directory.EnumerateFiles(folder, args.OptionalString("pattern") ?? "*", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}.", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
            return ToolResult.Ok("No files.");

        var text = new StringBuilder($"{files.Count} files:\n");
        foreach (var file in files.Take(1000))
            text.Append(AssetPathOf(file)).Append("  (").Append(Size(new FileInfo(file).Length)).Append(')').AppendLine();
        return ToolResult.Ok(text.ToString());
    }

    private ToolResult Read(ToolArgs args)
    {
        var full = FullPath(args.String("path"));
        if (!File.Exists(full))
            throw new AgentException($"There's no file {args.String("path")}.");
        var extension = Path.GetExtension(full).ToLowerInvariant();
        if (extension == ".png")
            return ToolResult.Image(File.ReadAllBytes(full), $"{AssetPathOf(full)} ({Size(new FileInfo(full).Length)})");
        if (ImageExtensions.Contains(extension) || !TextExtensions.Contains(extension) && LooksBinary(full))
            return ToolResult.Ok($"{AssetPathOf(full)} is a binary file of {Size(new FileInfo(full).Length)}.");
        return ToolResult.Ok(File.ReadAllText(full));
    }

    private async Task<ToolResult> WriteAsync(ToolArgs args)
    {
        var path = args.String("path");
        var full = FullPath(path);
        byte[] bytes;
        if (args.OptionalString("text") is { } text)
            bytes = Encoding.UTF8.GetBytes(text);
        else if (args.OptionalString("base64") is { } base64)
        {
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                throw new AgentException("\"base64\" isn't valid base64.");
            }
        }
        else
            throw new AgentException("Give \"text\" or \"base64\".");

        var existed = File.Exists(full);
        var previous = existed && TextExtensions.Contains(Path.GetExtension(full).ToLowerInvariant()) ? await File.ReadAllTextAsync(full) : null;
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, bytes);
        var assetPath = AssetPathOf(full);
        var feedback = await project.AssetChangedAsync(assetPath, previous);
        var result = $"{(existed ? "Replaced" : "Wrote")} {assetPath} ({Size(bytes.Length)}).{(feedback is null ? "" : "\n" + feedback.Message)}";
        return feedback is { IsError: true } ? ToolResult.Error(result) : ToolResult.Ok(result);
    }

    private async Task<ToolResult> DeleteAsync(ToolArgs args)
    {
        var full = FullPath(args.String("path"));
        if (File.Exists(full))
            File.Delete(full);
        else if (Directory.Exists(full) && !Directory.EnumerateFileSystemEntries(full).Any())
            Directory.Delete(full);
        else
            throw new AgentException($"There's no file (or empty folder) {args.String("path")}.");
        var feedback = await project.AssetChangedAsync(AssetPathOf(full));
        return ToolResult.Ok($"Deleted {AssetPathOf(full)}.{(feedback is null ? "" : "\n" + feedback.Message)}");
    }

    private async Task<ToolResult> MoveAsync(ToolArgs args)
    {
        var from = FullPath(args.String("from"));
        var to = FullPath(args.String("to"));
        if (!File.Exists(from))
            throw new AgentException($"There's no file {args.String("from")}.");
        if (!from.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || !to.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            throw new AgentException("Only prefabs can be moved, to a path ending in .prefab.");
        if (File.Exists(to) && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            throw new AgentException($"There's already a file at {AssetPathOf(to)}.");

        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to);
        var feedback = await project.AssetMovedAsync(AssetPathOf(from), AssetPathOf(to));
        var result = $"Moved {AssetPathOf(from)} to {AssetPathOf(to)}.{(feedback is null ? "" : "\n" + feedback.Message)}";
        return feedback is { IsError: true } ? ToolResult.Error(result) : ToolResult.Ok(result);
    }

    private static bool LooksBinary(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(4096, stream.Length)];
        var read = stream.Read(buffer);
        return buffer.Take(read).Any(b => b == 0);
    }

    private static string Size(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1048576.0:0.#} MB";
}
