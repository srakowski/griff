using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Griff.Runtime;

/// <summary>
/// A game's content (project.griff, Assets and compiled scripts) zipped onto the end of the player executable, so an
/// exported game is a single file. The exe ends with the zip, then its length (8 bytes) and a magic tag (8 bytes).
/// </summary>
/// <remarks>
/// The player unpacks the content to a cache folder the first time it runs, and plays from there: sounds and fonts
/// are played from real files.
/// </remarks>
public static class GamePackage
{
    private static readonly byte[] Magic = "GRIFFPAK"u8.ToArray();
    private const int FooterSize = 16;

    /// <summary>Zips <paramref name="gameFolder"/> onto the end of <paramref name="executable"/>.</summary>
    public static void Append(string executable, string gameFolder)
    {
        using var zip = new MemoryStream();
        ZipFile.CreateFromDirectory(gameFolder, zip, CompressionLevel.Optimal, includeBaseDirectory: false);

        using var file = new FileStream(executable, FileMode.Append, FileAccess.Write);
        zip.Position = 0;
        zip.CopyTo(file);
        file.Write(BitConverter.GetBytes(zip.Length));
        file.Write(Magic);
    }

    /// <summary>True if <paramref name="executable"/> carries a game.</summary>
    public static bool IsPackaged(string executable) => Locate(executable) is not null;

    /// <summary>
    /// Unpacks the game carried by <paramref name="executable"/> into a folder under <paramref name="cacheRoot"/> (once
    /// per version of the game) and returns that folder, or null if the executable carries no game.
    /// </summary>
    public static string? Extract(string executable, string cacheRoot, string name)
    {
        if (Locate(executable) is not var (offset, length))
            return null;

        using var file = File.OpenRead(executable);
        var hash = Hash(file, offset, length);
        var folder = Path.Combine(cacheRoot, SafeName(name) + "-" + hash);
        var done = Path.Combine(folder, ".unpacked");
        if (File.Exists(done))
            return folder;

        // Unpack beside the final folder and move it into place, so a half-finished unpack is never used.
        var staging = folder + ".tmp-" + Environment.ProcessId;
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        file.Position = offset;
        using (var zip = new ZipArchive(new Slice(file, offset, length), ZipArchiveMode.Read))
            zip.ExtractToDirectory(staging);
        File.WriteAllText(Path.Combine(staging, ".unpacked"), hash);

        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.Move(staging, folder);
        }
        catch (IOException) when (File.Exists(done))
        {
            // Another copy of the game unpacked it first.
            Directory.Delete(staging, recursive: true);
        }

        // Earlier versions of this game aren't needed any more.
        var prefix = SafeName(name) + "-";
        bool IsVersion(string dir) => Path.GetFileName(dir) is var n && n.Length == prefix.Length + 16 && n.StartsWith(prefix, StringComparison.Ordinal) && n[prefix.Length..].All(Uri.IsHexDigit);
        foreach (var old in Directory.EnumerateDirectories(cacheRoot, prefix + "*").Where(d => d != folder && IsVersion(d)))
        {
            try
            {
                Directory.Delete(old, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use by a copy that's still running; it goes next time.
            }
        }

        return folder;
    }

    private static (long Offset, long Length)? Locate(string executable)
    {
        using var file = File.OpenRead(executable);
        if (file.Length < FooterSize)
            return null;

        Span<byte> footer = stackalloc byte[FooterSize];
        file.Position = file.Length - FooterSize;
        file.ReadExactly(footer);
        if (!footer[8..].SequenceEqual(Magic))
            return null;

        var length = BitConverter.ToInt64(footer[..8]);
        var offset = file.Length - FooterSize - length;
        return length > 0 && offset >= 0 ? (offset, length) : null;
    }

    private static string Hash(Stream file, long offset, long length)
    {
        file.Position = offset;
        var bytes = SHA256.HashData(new Slice(file, offset, length));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private static string SafeName(string name)
    {
        var safe = new StringBuilder();
        foreach (var c in name)
            safe.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        return safe.ToString().Trim();
    }

    /// <summary>A read-only window onto part of another stream.</summary>
    private sealed class Slice(Stream inner, long start, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, length);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var available = (int)Math.Min(count, length - _position);
            if (available <= 0)
                return 0;
            inner.Position = start + _position;
            var read = inner.Read(buffer, offset, available);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
