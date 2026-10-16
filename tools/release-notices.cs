#:sdk Microsoft.NET.Sdk
#:project ../src/Griff.Exporting/Griff.Exporting.csproj
#:property PublishAot=false
#:property TreatWarningsAsErrors=false
#:property EnforceCodeStyleInBuild=false
#:property AnalysisLevel=none

// The third-party notices for an editor release (the zip tools\release.ps1 makes): the licenses of every third-party
// assembly in the editor's folder and in the players inside it, worked out from the files themselves the way a game
// export's are (ThirdPartyNotices), so a package the editor starts to carry can't go unlisted. Exits 1 if a file has no
// notice on record.
//   dotnet run tools/release-notices.cs -- <editor folder> <notices file to write>
using System.IO;
using Griff.Exporting;

if (args.Length != 2 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: release-notices <editor folder> <notices file to write>");
    return 2;
}

var editor = Path.GetFullPath(args[0]);
var player = Path.Combine(editor, "Player");
var files = new List<string>();
foreach (var file in Directory.EnumerateFiles(editor, "*.*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
{
    var relative = Path.GetRelativePath(editor, file);
    var extension = Path.GetExtension(file);
    if (file.StartsWith(player + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    {
        // A desktop player is a single-file bundle: its notices come from the files inside it. The web player's are its
        // own files (.NET for WebAssembly, one .wasm an assembly), as a web export's are.
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            files.AddRange(ThirdPartyNotices.FilesIn(file));
        else if (extension is ".wasm" or ".dll")
            files.Add(relative);
    }
    else if (extension is ".dll" or ".exe")
    {
        files.Add(relative);
    }
}

var notices = ThirdPartyNotices.For(files, introduction:
    "Griff's editor and the game players that come with it carry software and fonts made by others, listed here with their\n" +
    "licenses. Griff itself is under the MIT License (see LICENSE). A game made with Griff has its own notices file, written\n" +
    "when it is exported; what a game's maker made is theirs.");
if (notices.Unlisted.Count > 0)
{
    Console.Error.WriteLine("No notice on record for: " + string.Join(", ", notices.Unlisted));
    return 1;
}

File.WriteAllText(args[1], notices.Text);
Console.WriteLine($"Wrote {args[1]} ({files.Count} files covered).");
return 0;
