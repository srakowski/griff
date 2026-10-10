using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Griff.Audio;

/// <summary>
/// Reads and writes WAV files. Sounds made in Griff carry the recipe they were made from in a chunk of their own
/// (other programs skip it), so the Sound Maker can open them again to tweak.
/// </summary>
public static class Wav
{
    private const string RecipeChunk = "grff";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>16-bit PCM WAV bytes for <paramref name="clip"/>, with the recipe it was made from if any.</summary>
    public static byte[] Encode(SoundClip clip, SoundRecipe? sound = null, MusicRecipe? music = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var dataBytes = clip.Samples.Length * 2;
        var extra = sound is not null || music is not null
            ? Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Recipe { Sound = sound, Music = music }, Json))
            : null;

        writer.Write("RIFF"u8);
        writer.Write(0); // patched below
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)clip.Channels);
        writer.Write(clip.SampleRate);
        writer.Write(clip.SampleRate * clip.Channels * 2);
        writer.Write((short)(clip.Channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var s in clip.Samples)
            writer.Write((short)Math.Round(Math.Clamp(s, -1, 1) * short.MaxValue));

        if (extra is not null)
        {
            writer.Write(Encoding.ASCII.GetBytes(RecipeChunk));
            writer.Write(extra.Length);
            writer.Write(extra);
            if (extra.Length % 2 == 1)
                writer.Write((byte)0);
        }

        writer.Flush();
        var bytes = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        return bytes;
    }

    /// <summary>
    /// Decodes a WAV file: 8, 16, 24 or 32-bit PCM or 32-bit float, mono or stereo (more channels keep the first two).
    /// Null if it isn't one.
    /// </summary>
    public static SoundClip? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8))
            return null;

        int format = 0, channels = 0, rate = 0, bits = 0;
        var position = 12;
        while (position + 8 <= bytes.Length)
        {
            var id = bytes.Slice(position, 4);
            var size = BinaryPrimitives.ReadInt32LittleEndian(bytes[(position + 4)..]);
            var body = position + 8;
            if (size < 0)
                return null;
            var available = Math.Min(size, bytes.Length - body);

            if (id.SequenceEqual("fmt "u8) && available >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(bytes[body..]);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(body + 2)..]);
                rate = BinaryPrimitives.ReadInt32LittleEndian(bytes[(body + 4)..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(body + 14)..]);
                // WAVE_FORMAT_EXTENSIBLE: the real format is the first two bytes of the sub-format GUID.
                if (format == 0xFFFE && available >= 26)
                    format = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(body + 24)..]);
            }
            else if (id.SequenceEqual("data"u8))
            {
                if (channels <= 0 || rate <= 0)
                    return null;
                return Samples(bytes.Slice(body, available), format, channels, rate, bits);
            }

            position = body + size + (size & 1);
        }

        return null;
    }

    public static SoundClip? Decode(string path) => File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null;

    /// <summary>The sound effect recipe a WAV made in Griff was made from, or null.</summary>
    public static SoundRecipe? ReadSoundRecipe(string path) => ReadRecipe(path)?.Sound;

    /// <summary>The music recipe a WAV made in Griff was made from, or null.</summary>
    public static MusicRecipe? ReadMusicRecipe(string path) => ReadRecipe(path)?.Music;

    private static Recipe? ReadRecipe(string path)
    {
        try
        {
            if (!File.Exists(path) || !path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                return null;
            var bytes = File.ReadAllBytes(path);
            var position = 12;
            while (position + 8 <= bytes.Length)
            {
                var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(position + 4));
                if (size < 0 || position + 8 + size > bytes.Length)
                    return null;
                if (Encoding.ASCII.GetString(bytes, position, 4) == RecipeChunk)
                    return JsonSerializer.Deserialize<Recipe>(bytes.AsSpan(position + 8, size), Json);
                position += 8 + size + (size & 1);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        return null;
    }

    private static SoundClip? Samples(ReadOnlySpan<byte> data, int format, int channels, int rate, int bits)
    {
        var bytesPer = bits / 8;
        if (bytesPer is < 1 or > 4 || (format != 1 && format != 3) || (format == 3 && bits != 32))
            return null;

        var frames = data.Length / (bytesPer * channels);
        var kept = Math.Min(channels, 2);
        var samples = new float[frames * kept];
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < kept; c++)
            {
                var at = data[((f * channels + c) * bytesPer)..];
                samples[f * kept + c] = format == 3 ? BinaryPrimitives.ReadSingleLittleEndian(at) : bytesPer switch
                {
                    1 => (at[0] - 128) / 128f,
                    2 => BinaryPrimitives.ReadInt16LittleEndian(at) / 32768f,
                    3 => ((at[0] | at[1] << 8 | (sbyte)at[2] << 16)) / 8388608f,
                    _ => BinaryPrimitives.ReadInt32LittleEndian(at) / 2147483648f,
                };
            }
        }

        return new SoundClip(samples, kept, rate);
    }

    private sealed class Recipe
    {
        public SoundRecipe? Sound { get; set; }

        public MusicRecipe? Music { get; set; }
    }
}
