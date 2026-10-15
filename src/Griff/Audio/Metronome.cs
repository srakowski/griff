namespace Griff.Audio;

/// <summary>
/// A metronome's clicks, made in code: one bar of them to loop, a short click starting exactly on each beat and a higher
/// one on the first of the bar. What <see cref="MixerAudio.PlayMetronome"/> plays, for a <see cref="Scenes.Calibration"/>
/// to tap along to or a count-in.
/// </summary>
public static class Metronome
{
    private static readonly Dictionary<(float Tempo, int Beats), SoundClip> Bars = [];

    /// <summary>
    /// One bar of clicks at <paramref name="tempo"/> beats a minute (20 to 400), <paramref name="beatsPerBar"/> of them (1 to
    /// 16), each starting on its beat's very first sample. The same bar is made once and kept.
    /// </summary>
    public static SoundClip Bar(float tempo, int beatsPerBar)
    {
        tempo = float.IsFinite(tempo) ? Math.Clamp(tempo, 20, 400) : 120;
        beatsPerBar = Math.Clamp(beatsPerBar, 1, 16);
        lock (Bars)
        {
            if (Bars.TryGetValue((tempo, beatsPerBar), out var made))
                return made;
            var rate = Mixer.SampleRate;
            var perBeat = 60.0 / tempo;
            var samples = new float[(int)Math.Round(beatsPerBar * perBeat * rate)];
            for (var beat = 0; beat < beatsPerBar; beat++)
                Click(samples, (int)Math.Round(beat * perBeat * rate), beat == 0 ? 1760 : 1175);
            return Bars[(tempo, beatsPerBar)] = new SoundClip(samples, 1, rate);
        }
    }

    // A sine that starts at full strength and dies away within a few hundredths of a second: sharp enough to tap to.
    private static void Click(float[] samples, int start, double frequency)
    {
        var rate = (double)Mixer.SampleRate;
        var length = (int)(0.04 * rate);
        for (var i = 0; i < length && start + i < samples.Length; i++)
        {
            var t = i / rate;
            samples[start + i] += (float)(0.7 * Math.Exp(-t / 0.008) * Math.Sin(2 * Math.PI * frequency * t + Math.PI / 2));
        }
    }
}
