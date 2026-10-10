using System.Buffers.Binary;
using Griff.Audio;

namespace Griff.Tests.Audio;

/// <summary>The mixer that plays every sound: pitch, pan, volume, loops and stopping; and WAV files in and out.</summary>
public class MixerTests
{
    private static SoundClip Constant(int frames, float value = 0.5f, int rate = Mixer.SampleRate) =>
        new(Enumerable.Repeat(value, frames).ToArray(), 1, rate);

    private static float[] Mix(Mixer mixer, int frames)
    {
        var output = new float[frames * 2];
        mixer.Mix(output);
        return output;
    }

    [Fact]
    public void ASound_PlaysThenFinishes()
    {
        var mixer = new Mixer();
        var voice = mixer.Play(Constant(100).Open());

        var output = Mix(mixer, 200);

        Assert.Equal(0.5f, output[0], 4);
        Assert.Equal(0.5f, output[1], 4);
        Assert.Equal(0, output[150 * 2]);
        Assert.False(voice.IsPlaying);
        Assert.Equal(0, mixer.Playing);
    }

    [Fact]
    public void PitchTwo_PlaysTwiceAsFast()
    {
        var mixer = new Mixer();
        mixer.Play(Constant(1000).Open(), pitch: 2);

        var output = Mix(mixer, 1000);

        Assert.Equal(0.5f, output[490 * 2], 4);
        Assert.Equal(0, output[510 * 2]);
    }

    [Fact]
    public void ASoundAtAnotherRate_IsResampled()
    {
        var mixer = new Mixer();
        mixer.Play(Constant(Mixer.SampleRate / 2, rate: Mixer.SampleRate / 2).Open());

        var output = Mix(mixer, Mixer.SampleRate);

        // Half a second's worth of samples at half the rate still lasts... half a second at twice the samples.
        Assert.Equal(0.5f, output[(Mixer.SampleRate - 10) * 2], 4);
    }

    [Fact]
    public void Pan_TakesItToOneSide()
    {
        var mixer = new Mixer();
        mixer.Play(Constant(100).Open(), pan: -1);

        var output = Mix(mixer, 50);

        Assert.Equal((0.5f, 0f), (output[20], output[21]));
    }

    [Fact]
    public void Volume_Glides_RatherThanJumping()
    {
        var mixer = new Mixer();
        var voice = mixer.Play(Constant(10_000).Open());
        Mix(mixer, 100);

        voice.Volume = 0;
        var output = Mix(mixer, 100);

        Assert.InRange(output[0], 0.45f, 0.5f);
        Assert.InRange(output[50 * 2], 0.2f, 0.3f);
        Assert.InRange(output[99 * 2], 0, 0.01f);
    }

    [Fact]
    public void Loops_GoOnUntilStopped_FadingOut()
    {
        var mixer = new Mixer();
        var voice = mixer.Play(Constant(64).Open(), loop: true);

        var output = Mix(mixer, 1000);
        Assert.Equal(0.5f, output[900 * 2], 4);
        Assert.True(voice.IsPlaying);

        voice.Stop();
        Assert.False(voice.IsPlaying);
        var fading = Mix(mixer, 100);
        Assert.InRange(fading[99 * 2], 0, 0.01f);
        Assert.Equal(0, mixer.Playing);
    }

    [Fact]
    public void LoudSounds_BendRatherThanClip()
    {
        var mixer = new Mixer();
        for (var i = 0; i < 4; i++)
            mixer.Play(Constant(100, 0.9f).Open());

        var output = Mix(mixer, 50);

        Assert.InRange(output[0], 0.95f, 1f);
    }

    [Fact]
    public void Wav_RoundTrips_WithItsRecipe()
    {
        var recipe = SoundRecipe.Preset(SoundPreset.Coin, new Random(5));
        var clip = SoundSynth.Render(recipe);
        var path = Path.Combine(Path.GetTempPath(), $"griff-{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(path, Wav.Encode(clip, recipe));

            var decoded = Wav.Decode(path)!;
            Assert.Equal((clip.Channels, clip.SampleRate, clip.Frames), (decoded.Channels, decoded.SampleRate, decoded.Frames));
            Assert.Equal(clip.Samples[500], decoded.Samples[500], 3);
            Assert.Equal(recipe, Wav.ReadSoundRecipe(path));
            Assert.Null(Wav.ReadMusicRecipe(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Wav_ReadsMusicRecipes_AndNoneFromPlainFiles()
    {
        var recipe = MusicRecipe.ForMood(MusicMood.Spooky, 9) with { Bars = 4 };
        var path = Path.Combine(Path.GetTempPath(), $"griff-{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(path, Wav.Encode(new SoundClip([0, 0], 2, 44100), music: recipe));
            Assert.Equal(recipe, Wav.ReadMusicRecipe(path));

            File.WriteAllBytes(path, Wav.Encode(new SoundClip([0, 0], 2, 44100)));
            Assert.Null(Wav.ReadSoundRecipe(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(1, 8)]
    [InlineData(1, 24)]
    [InlineData(3, 32)]
    public void Wav_DecodesOtherFormats(int format, int bits)
    {
        var bytesPer = bits / 8;
        var data = new byte[2 * bytesPer * 2];
        // Two stereo frames: the first sample half way up, the rest silent.
        if (format == 3)
            BinaryPrimitives.WriteSingleLittleEndian(data, 0.5f);
        else if (bits == 8)
        {
            Array.Fill(data, (byte)128);
            data[0] = 192;
        }
        else
            data[2] = 0x40;

        var file = new List<byte>();
        file.AddRange("RIFF"u8.ToArray());
        file.AddRange(BitConverter.GetBytes(36 + data.Length));
        file.AddRange("WAVEfmt "u8.ToArray());
        file.AddRange(BitConverter.GetBytes(16));
        file.AddRange(BitConverter.GetBytes((short)format));
        file.AddRange(BitConverter.GetBytes((short)2));
        file.AddRange(BitConverter.GetBytes(22050));
        file.AddRange(BitConverter.GetBytes(22050 * 2 * bytesPer));
        file.AddRange(BitConverter.GetBytes((short)(2 * bytesPer)));
        file.AddRange(BitConverter.GetBytes((short)bits));
        file.AddRange("data"u8.ToArray());
        file.AddRange(BitConverter.GetBytes(data.Length));
        file.AddRange(data);

        var clip = Wav.Decode(file.ToArray())!;

        Assert.Equal((2, 22050, 2), (clip.Channels, clip.SampleRate, clip.Frames));
        Assert.Equal(0.5f, clip.Samples[0], 2);
        Assert.Equal(0, clip.Samples[1], 2);
    }

    [Fact]
    public void Wav_RejectsWhatIsntOne() => Assert.Null(Wav.Decode("not a wav file at all"u8));
}
