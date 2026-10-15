#if DEBUG
namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// Speaks the demo's narration with the Windows speech synthesizer (SAPI, through COM, so no extra packages) and shows
/// it as a caption while it's spoken. Silent runs show the caption for a moment instead.
/// </summary>
internal sealed class DemoNarrator
{
    private const int SpeakAsync = 1, PurgeBeforeSpeak = 2;

    private readonly DemoOverlay _overlay;
    private readonly dynamic? _voice;
    private readonly double _silentSpeed;
    private readonly int _rate;

    public DemoNarrator(DemoOverlay overlay, bool speak, string? voiceName, int rate, double silentSpeed)
    {
        _overlay = overlay;
        _silentSpeed = silentSpeed;
        _rate = rate;
        if (!speak || Type.GetTypeFromProgID("SAPI.SpVoice") is not { } type)
            return;

        _voice = Activator.CreateInstance(type);
        if (_voice is null)
            return;
        _voice.Rate = rate;
        _voice.Volume = 100;
        if (voiceName is not null)
        {
            var voices = _voice.GetVoices($"Name={voiceName}");
            if (voices.Count > 0)
                _voice.Voice = voices.Item(0);
        }
    }

    public bool IsSpeaking { get; private set; }

    /// <summary>Speaks <paramref name="text"/> and finishes when it's been said.</summary>
    public async Task Say(string text)
    {
        var control = DemoControl.Active;
        _overlay.Caption(text);
        IsSpeaking = true;
        try
        {
            // Hurrying past a chapter, no voice or the narration muted: show the caption for about as long as it takes to read.
            if (_voice is null || control?.Skipping == true || control?.NarrationMuted == true)
            {
                var ms = Math.Max(150, text.Length * 55 / _silentSpeed);
                await (control?.Delay(ms) ?? Task.Delay(TimeSpan.FromMilliseconds(ms)));
                return;
            }

            // Faster playback speaks faster: SAPI's rate goes up to 10, about three times normal.
            _voice.Rate = control?.Speed switch { >= 4 => 10, >= 2 => Math.Min(10, _rate + 5), _ => _rate };
            _voice.Speak(Pronounce(text), SpeakAsync | PurgeBeforeSpeak);
            while (!(bool)_voice.WaitUntilDone(0))
            {
                if (control is not null)
                {
                    if (control.Paused)
                    {
                        _voice.Pause();
                        try
                        {
                            await control.Gate();
                        }
                        finally
                        {
                            _voice.Resume();
                        }
                    }

                    await control.Gate();
                    if (control.Skipping || control.NarrationMuted)
                    {
                        _voice.Speak("", SpeakAsync | PurgeBeforeSpeak);
                        break;
                    }
                }

                await Task.Delay(40);
            }
        }
        catch (OperationCanceledException)
        {
            _voice?.Speak("", SpeakAsync | PurgeBeforeSpeak);
            throw;
        }
        finally
        {
            IsSpeaking = false;
        }
    }

    public void Stop()
    {
        if (_voice is not null)
            _voice.Speak("", SpeakAsync | PurgeBeforeSpeak);
        _overlay.Caption(null);
    }

    // Captions read naturally; a few words need help to be spoken right.
    private static string Pronounce(string text) => text
        .Replace("Griff", "Griff")
        .Replace("C#", "C sharp")
        .Replace("F5", "F 5")
        .Replace("Ctrl", "Control")
        .Replace(".exe", " dot E X E")
        .Replace("HUD", "hud")
        .Replace("—", ", ");
}
#endif
