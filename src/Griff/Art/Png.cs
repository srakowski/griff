using System.IO.Compression;
using System.Text;

namespace Griff.Art;

/// <summary>
/// Writes PNG files (8-bit RGBA) and reads back the ones Griff writes. Pictures painted in Griff carry their layout
/// (frames, tiles, speed) in a chunk of their own, the way sounds carry their recipe, so they open in the Sprite Painter
/// exactly as they were made; every other program ignores the chunk.
/// </summary>
public static class Png
{
    /// <summary>Griff's own chunk: ancillary, private, safe to copy.</summary>
    public const string LayoutChunk = "grFf";

    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// A PNG of <paramref name="width"/> by <paramref name="height"/> pixels from <paramref name="rgba"/> (four bytes a
    /// pixel, rows top to bottom), with <paramref name="text"/> in a <see cref="LayoutChunk"/> if given.
    /// </summary>
    public static byte[] Encode(byte[] rgba, int width, int height, string? text = null)
    {
        if (width <= 0 || height <= 0 || rgba.Length != width * height * 4)
            throw new ArgumentException("The pixels don't match the size.", nameof(rgba));

        var stride = width * 4;
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
            Array.Copy(rgba, y * stride, raw, y * (stride + 1) + 1, stride);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);

        using var png = new MemoryStream();
        png.Write(Signature);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        (header[8], header[9], header[10], header[11], header[12]) = (8, 6, 0, 0, 0);
        Chunk(png, "IHDR", header);
        if (text is not null)
            Chunk(png, LayoutChunk, Encoding.UTF8.GetBytes(text));
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>The text in a PNG's <see cref="LayoutChunk"/>, or null if it has none (or isn't a PNG).</summary>
    public static string? ReadText(byte[] png)
    {
        foreach (var (type, start, length) in Chunks(png))
        {
            if (type == LayoutChunk)
                return Encoding.UTF8.GetString(png, start, length);
        }

        return null;
    }

    /// <summary>
    /// Reads an 8-bit, non-interlaced RGB or RGBA PNG (what <see cref="Encode"/> writes) into RGBA bytes, or null for
    /// anything else. The editor reads other PNGs with the system's decoder.
    /// </summary>
    public static (byte[] Rgba, int Width, int Height)? Decode(byte[] png)
    {
        int width = 0, height = 0, channels = 0;
        using var data = new MemoryStream();
        foreach (var (type, start, length) in Chunks(png))
        {
            if (type == "IHDR")
            {
                width = (int)ReadBigEndian(png, start);
                height = (int)ReadBigEndian(png, start + 4);
                var (depth, color, interlace) = (png[start + 8], png[start + 9], png[start + 12]);
                channels = color switch { 6 => 4, 2 => 3, _ => 0 };
                if (depth != 8 || interlace != 0 || channels == 0 || width <= 0 || height <= 0)
                    return null;
            }
            else if (type == "IDAT")
                data.Write(png, start, length);
        }

        if (channels == 0)
            return null;

        data.Position = 0;
        var stride = width * channels;
        var raw = new byte[height * (stride + 1)];
        using (var zlib = new ZLibStream(data, CompressionMode.Decompress))
        {
            var read = 0;
            while (read < raw.Length && zlib.Read(raw, read, raw.Length - read) is var n and > 0)
                read += n;
            if (read < raw.Length)
                return null;
        }

        var rows = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            for (var x = 0; x < stride; x++)
            {
                var value = raw[y * (stride + 1) + 1 + x];
                int left = x >= channels ? rows[y * stride + x - channels] : 0;
                int up = y > 0 ? rows[(y - 1) * stride + x] : 0;
                int corner = x >= channels && y > 0 ? rows[(y - 1) * stride + x - channels] : 0;
                rows[y * stride + x] = (byte)(filter switch
                {
                    1 => value + left,
                    2 => value + up,
                    3 => value + (left + up) / 2,
                    4 => value + Paeth(left, up, corner),
                    _ => value,
                });
            }
        }

        if (channels == 4)
            return (rows, width, height);
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
            (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = (rows[i * 3], rows[i * 3 + 1], rows[i * 3 + 2], 255);
        return (rgba, width, height);
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var (pa, pb, pc) = (Math.Abs(p - a), Math.Abs(p - b), Math.Abs(p - c));
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    // Each chunk's type and where its data is, until IEND or the bytes run out.
    private static IEnumerable<(string Type, int Start, int Length)> Chunks(byte[] png)
    {
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            yield break;
        var at = Signature.Length;
        while (at + 12 <= png.Length)
        {
            var length = (int)ReadBigEndian(png, at);
            if (length < 0 || at + 12 + length > png.Length)
                yield break;
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            yield return (type, at + 8, length);
            if (type == "IEND")
                yield break;
            at += 12 + length;
        }
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length);
        var typed = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++)
            typed[i] = (byte)type[i];
        data.CopyTo(typed, 4);
        stream.Write(typed);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc32(typed));
        stream.Write(crc);
    }

    private static uint ReadBigEndian(byte[] buffer, int at) =>
        (uint)(buffer[at] << 24 | buffer[at + 1] << 16 | buffer[at + 2] << 8 | buffer[at + 3]);

    private static void WriteBigEndian(byte[] buffer, int at, uint value)
    {
        buffer[at] = (byte)(value >> 24);
        buffer[at + 1] = (byte)(value >> 16);
        buffer[at + 2] = (byte)(value >> 8);
        buffer[at + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return ~crc;
    }
}
