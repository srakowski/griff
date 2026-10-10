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

    public DemoNarrator(DemoOverlay overlay, bool speak, string? voiceName, int rate, double silentSpeed)
    {
        _overlay = overlay;
        _silentSpeed = silentSpeed;
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
        _overlay.Caption(text);
        IsSpeaking = true;
        try
        {
            if (_voice is null)
            {
                // Roughly reading speed, scaled for test runs.
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(150, text.Length * 55 / _silentSpeed)));
                return;
            }

            _voice.Speak(Pronounce(text), SpeakAsync | PurgeBeforeSpeak);
            while (!(bool)_voice.WaitUntilDone(0))
                await Task.Delay(40);
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
