using System.Buffers.Binary;
using Griff.Audio;

namespace Griff.Tests.Audio;

/// <summary>
/// WAV read as it plays (Wav.Open, how the web player plays WAV music): the same samples Decode gives, a little at a time,
/// in every format Decode reads, its length known, from the start again on Rewind.
/// </summary>
public sealed class WavStreamTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "griff-wav-" + Guid.NewGuid().ToString("N")[..8]);

    public WavStreamTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(1, 1, 8)]
    [InlineData(1, 2, 16)]
    [InlineData(1, 1, 24)]
    [InlineData(1, 2, 32)]
    [InlineData(3, 2, 32)]
    [InlineData(0xFFFE, 2, 16)]
    [InlineData(1, 4, 16)]
    public void It_reads_what_Decode_reads_a_little_at_a_time(int code, int channels, int bits)
    {
        var path = Write(code, channels, bits, frames: 10_007);
        var decoded = Wav.Decode(path)!;
        using var stream = Wav.Open(path)!;
        Assert.Equal(decoded.SampleRate, stream.SampleRate);
        Assert.Equal(decoded.Channels, stream.Channels);
        Assert.Equal(10_007, stream.Frames);
        Assert.Equal(decoded.Samples, ReadAll(stream, chunk: 2 * 333));

        // From the start again, read in other sizes.
        stream.Rewind();
        Assert.Equal(decoded.Samples, ReadAll(stream, chunk: 2 * 4096 + 2));
    }

    [Fact]
    public void Griff_music_with_its_recipe_after_the_sound_streams_and_keeps_its_recipe()
    {
        var clip = new SoundClip(Enumerable.Range(0, 20_000).Select(i => MathF.Sin(i / 40f) * 0.5f).ToArray(), 2, 44100);
        var recipe = new MusicRecipe { Seed = 7 };
        var path = Path.Combine(_folder, "music.wav");
        File.WriteAllBytes(path, Wav.Encode(clip, music: recipe));

        using var stream = Wav.Open(path)!;
        Assert.Equal(Wav.Decode(path)!.Samples, ReadAll(stream, chunk: 1024));
        Assert.Equal(recipe, Wav.ReadMusicRecipe(path));
    }

    [Fact]
    public void Anything_else_opens_as_nothing()
    {
        var text = Path.Combine(_folder, "notes.wav");
        File.WriteAllText(text, "not a sound");
        Assert.Null(Wav.Open(text));
        Assert.Null(Wav.Open(Path.Combine(_folder, "missing.wav")));
        Assert.Null(Wav.Open(Write(2, 1, 16, frames: 10)));
    }

    private static float[] ReadAll(IAudioStream stream, int chunk)
    {
        var all = new List<float>();
        var buffer = new float[chunk];
        int read;
        while ((read = stream.Read(buffer)) > 0)
            all.AddRange(buffer.AsSpan(0, read));
        return [.. all];
    }

    // A WAV of `frames` frames of a ramp in the format given (a LIST chunk before its samples, as other programs write),
    // with an odd-sized chunk of something else after them.
    private string Write(int code, int channels, int bits, int frames)
    {
        var bytesPer = bits / 8;
        var data = new byte[frames * channels * bytesPer];
        for (var i = 0; i < frames * channels; i++)
        {
            var value = (i % 200 - 100) / 101.0;
            var at = data.AsSpan(i * bytesPer);
            if (code == 3)
                BinaryPrimitives.WriteSingleLittleEndian(at, (float)value);
            else if (bytesPer == 1)
                at[0] = (byte)(128 + value * 127);
            else if (bytesPer == 2)
                BinaryPrimitives.WriteInt16LittleEndian(at, (short)(value * short.MaxValue));
            else if (bytesPer == 3)
            {
                var v = (int)(value * 8388607);
                (at[0], at[1], at[2]) = ((byte)v, (byte)(v >> 8), (byte)(v >> 16));
            }
            else
                BinaryPrimitives.WriteInt32LittleEndian(at, (int)(value * int.MaxValue));
        }

        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);
        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVE"u8);
        writer.Write("LIST"u8);
        writer.Write(3);
        writer.Write("abc\0"u8);
        writer.Write("fmt "u8);
        writer.Write(code == 0xFFFE ? 40 : 16);
        writer.Write((short)code);
        writer.Write((short)channels);
        writer.Write(44100);
        writer.Write(44100 * channels * bytesPer);
        writer.Write((short)(channels * bytesPer));
        writer.Write((short)bits);
        if (code == 0xFFFE)
        {
            writer.Write((short)22);
            writer.Write((short)bits);
            writer.Write(0);
            writer.Write((short)1);
            writer.Write(new byte[14]);
        }

        writer.Write("data"u8);
        writer.Write(data.Length);
        writer.Write(data);
        writer.Write("junk"u8);
        writer.Write(5);
        writer.Write("12345\0"u8);
        writer.Flush();
        var bytes = file.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        var path = Path.Combine(_folder, $"{code}-{channels}-{bits}-{frames}.wav");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
