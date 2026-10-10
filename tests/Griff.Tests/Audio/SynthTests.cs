using Griff.Audio;

namespace Griff.Tests.Audio;

/// <summary>The sound effect and music generators: what they make, and that it's the same every time.</summary>
public class SynthTests
{
    [Theory]
    [InlineData(SoundPreset.Coin)]
    [InlineData(SoundPreset.Jump)]
    [InlineData(SoundPreset.Shoot)]
    [InlineData(SoundPreset.Explosion)]
    [InlineData(SoundPreset.PowerUp)]
    [InlineData(SoundPreset.Hit)]
    [InlineData(SoundPreset.Blip)]
    [InlineData(SoundPreset.Random)]
    public void EveryPreset_MakesAShortSound_AsLoudAsItsVolume(SoundPreset preset)
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var recipe = SoundRecipe.Preset(preset, new Random(seed));
            var clip = SoundSynth.Render(recipe);

            Assert.Equal(1, clip.Channels);
            Assert.Equal(Mixer.SampleRate, clip.SampleRate);
            Assert.InRange(clip.Seconds, 0.001, SoundSynth.MaxSeconds);
            Assert.All(clip.Samples, s => Assert.True(float.IsFinite(s)));
            Assert.Equal(recipe.Volume, clip.Samples.Max(Math.Abs), 3);
        }
    }

    [Fact]
    public void TheSameRecipe_MakesTheSameSound()
    {
        var recipe = SoundRecipe.Preset(SoundPreset.Explosion, new Random(4));

        Assert.Equal(SoundSynth.Render(recipe).Samples, SoundSynth.Render(recipe with { }).Samples);
        Assert.Equal(recipe, SoundRecipe.Preset(SoundPreset.Explosion, new Random(4)));
    }

    [Fact]
    public void Jump_RisesInPitch_Coin_IsBrief()
    {
        var jump = SoundRecipe.Preset(SoundPreset.Jump, new Random(1));
        var coin = SoundRecipe.Preset(SoundPreset.Coin, new Random(1));

        Assert.True(jump.Slide > 0);
        Assert.True(SoundSynth.Render(coin).Seconds < 1.5);
    }

    [Fact]
    public void Mutate_ChangesALittle_AndStaysInRange()
    {
        var recipe = SoundRecipe.Preset(SoundPreset.Coin, new Random(2));
        var mutated = recipe.Mutate(new Random(3));

        Assert.NotEqual(recipe, mutated);
        Assert.Equal(recipe.Wave, mutated.Wave);
        Assert.InRange(Math.Abs(mutated.Frequency - recipe.Frequency), 0, 0.05f);
        Assert.Equal(mutated, mutated.Clamped());
    }

    [Fact]
    public void Clamped_KeepsSettingsInRange_AndAudible()
    {
        var wild = new SoundRecipe { Frequency = 3, Slide = -9, Volume = float.NaN, Attack = 0, Sustain = 0, Decay = 0, Wave = (Waveform)42 };

        var clamped = wild.Clamped();

        Assert.Equal((1f, -1f, 0f, Waveform.Square), (clamped.Frequency, clamped.Slide, clamped.Volume, clamped.Wave));
        Assert.True(clamped.Decay > 0);
    }

    [Fact]
    public void Music_LastsItsBars_AtItsTempo_InStereo()
    {
        var recipe = MusicRecipe.ForMood(MusicMood.Bright, seed: 3) with { Tempo = 120, Bars = 8 };

        var clip = MusicSynth.Render(recipe);

        // 8 bars of 4 beats at 120 a minute is 16 seconds.
        Assert.Equal(2, clip.Channels);
        Assert.Equal(16, clip.Seconds, 2);
        Assert.Equal(16, MusicSynth.Seconds(recipe), 6);
        Assert.Equal(recipe.Volume * 0.9f, clip.Samples.Max(Math.Abs), 3);
    }

    [Theory]
    [InlineData(MusicMood.Bright)]
    [InlineData(MusicMood.Chill)]
    [InlineData(MusicMood.Tense)]
    [InlineData(MusicMood.Spooky)]
    [InlineData(MusicMood.Heroic)]
    public void EveryMood_WritesTheSameTuneForTheSameSeed_AndAnotherForAnother(MusicMood mood)
    {
        var recipe = MusicRecipe.ForMood(mood, seed: 7) with { Bars = 4 };

        var a = MusicSynth.Render(recipe).Samples;
        var b = MusicSynth.Render(recipe).Samples;
        var other = MusicSynth.Render(recipe with { Seed = 8 }).Samples;

        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
        Assert.All(a, s => Assert.True(float.IsFinite(s)));
    }

    [Fact]
    public void Music_TurningAPartOff_LeavesTheOthersAsTheyWere()
    {
        var recipe = MusicRecipe.ForMood(MusicMood.Heroic, seed: 2) with { Bars = 4, Volume = 1 };
        var bassOnly = MusicSynth.Render(recipe with { Melody = false, Chords = false, Drums = false });
        var bassAgain = MusicSynth.Render(recipe with { Melody = false, Chords = false, Drums = false, Seed = 2 });
        var silent = MusicSynth.Render(recipe with { Melody = false, Chords = false, Drums = false, Bass = false });

        Assert.Equal(bassOnly.Samples, bassAgain.Samples);
        Assert.All(silent.Samples, s => Assert.Equal(0, s));
    }

    [Fact]
    public void Peaks_DescribeTheShapeOfTheSound()
    {
        var clip = new SoundClip([0, 0.5f, -1, 0.25f], 1, 4);

        Assert.Equal([0.5f, 1f], clip.Peaks(2));
    }
}
