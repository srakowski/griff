using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

// Calibrations: the player taps along to clicks (or a flashing light) and gets how late they hear (or see) the game.
public sealed partial class Game
{
    // A tap belongs to the beat it's nearest, counting from a quarter of a beat before it to three quarters after: players
    // are seldom early, and late speakers can be very late.
    private const double TapWindowBefore = 0.25;

    // Taps further than this from the middle one (or than three times how far they typically are from it) are left out.
    private const double SteadyTaps = 0.04;

    // The calibrations seen showing, kept to stop the ones that go (a panel closing) and start the ones that come.
    private readonly List<Calibration> _calibrations = [];

    // Runs in real time, under a pause menu too: it times what's heard, not the game. Each machine runs its own.
    private void RunCalibrations()
    {
        for (var i = _calibrations.Count - 1; i >= 0; i--)
        {
            var gone = _calibrations[i];
            if (gone.Entity is { } entity && entity.Scene == Scene && entity.IsActive)
                continue;
            _calibrations.RemoveAt(i);
            // Gone with its scene, the next scene's music is left to start; hidden (its panel closing), the music comes back.
            if (gone.IsRunning)
                EndCalibration(gone, CalibrationState.Waiting, resume: gone.Entity?.Scene == Scene);
        }

        foreach (var calibration in Showing<Calibration>())
        {
            if (!_calibrations.Contains(calibration))
            {
                _calibrations.Add(calibration);
                calibration.State = CalibrationState.Waiting;
                if (calibration.StartOnShow)
                    calibration.Wanted = CalibrationRequest.Start;
            }

            var wanted = calibration.Wanted;
            calibration.Wanted = CalibrationRequest.None;
            if (wanted == CalibrationRequest.Start)
                StartCalibration(calibration);
            else if (wanted == CalibrationRequest.Stop && calibration.IsRunning)
                EndCalibration(calibration, CalibrationState.Waiting);

            if (calibration.IsRunning)
                StepCalibration(calibration);
            UpdatePrompt(calibration);
        }

        // Every one, shown or not, says the offset in use, so a button opening one can show it.
        foreach (var calibration in Scene.All<Calibration>())
            calibration.Current = calibration.Measures == CalibrationKind.Visual ? VisualOffset : AudioOffset;
    }

    private static double PerBeat(Calibration calibration) =>
        60.0 / (float.IsFinite(calibration.Tempo) ? Math.Clamp(calibration.Tempo, 20, 400) : 120);

    private void StartCalibration(Calibration calibration)
    {
        if (!calibration.IsRunning)
        {
            calibration.Resume = Audio.Music is { } playing && playing != IAudio.MetronomeMusic ? playing : null;
            // Its buttons and sliders go while the player taps, so a tap can't press or drag one.
            calibration.Hidden.Clear();
            foreach (var inside in calibration.Entity!.SelfAndDescendants())
            {
                if (inside != calibration.Entity && inside.IsEnabled && (inside.GetComponent<Button>() is not null || inside.GetComponent<Slider>() is not null))
                {
                    inside.IsEnabled = false;
                    calibration.Hidden.Add(inside);
                }
            }
        }

        var silent = calibration.Measures == CalibrationKind.Visual;
        Audio.PlayMetronome(calibration.Tempo, 4, silent ? 0 : Math.Clamp(calibration.Volume, 0, 1));
        calibration.State = CalibrationState.Listening;
        (calibration.Taps, calibration.Result, calibration.Spread, calibration.Applied) = (0, 0, 0, false);
        (calibration.Position, calibration.LastTap) = (-1, double.NegativeInfinity);
        calibration.Lags.Clear();
        calibration.Tapped = new bool[Math.Clamp(calibration.Beats, 1, 256)];
    }

    private void StepCalibration(Calibration calibration)
    {
        // Something else took the music over (the scene's starting as it opens with it): that's the music to come back
        // to, and the clicks start again.
        if (Audio.Music != IAudio.MetronomeMusic)
        {
            if (Audio.Music is { } other)
                calibration.Resume = other;
            StartCalibration(calibration);
            return;
        }

        var perBeat = PerBeat(calibration);
        var position = Audio.MusicTime / perBeat;
        calibration.Position = position;
        var countIn = Math.Max(0, calibration.CountIn);
        var beats = calibration.Tapped.Length;
        if (calibration.State == CalibrationState.Listening && position >= countIn - TapWindowBefore)
            calibration.State = CalibrationState.Tapping;

        if (Tapped(calibration))
        {
            calibration.LastTap = position;
            var beat = (long)Math.Floor(position + TapWindowBefore);
            var index = beat - countIn;
            if (index >= 0 && index < beats)
            {
                calibration.Lags.Add((position - beat) * perBeat);
                calibration.Tapped[index] = true;
                calibration.Taps++;
            }
        }

        // The last beat's window has gone by.
        if (position >= countIn + beats - TapWindowBefore)
            FinishCalibration(calibration);
    }

    // A tap this step: its action, or a click or touch. Like a menu's controls, it listens while it's on the top panel (or
    // none is open), so a panel opened over it has the keys.
    private bool Tapped(Calibration calibration)
    {
        if (TopPanel is { } top && !Inside(calibration.Entity!, top))
            return false;
        if (calibration.TapAction is { Length: > 0 } action && !string.IsNullOrWhiteSpace(action) && Input.WasPressed(action.Trim()))
            return true;
        return calibration.Clicks && Input.WasPressed(MouseButton.Left);
    }

    // The taps' average lag, leaving out those far from the middle one: a missed beat, a double tap, a late start.
    private void FinishCalibration(Calibration calibration)
    {
        var lags = calibration.Lags;
        var needed = Math.Max(3, (calibration.Tapped.Length + 1) / 2);
        var state = CalibrationState.Failed;
        if (lags.Count >= needed)
        {
            var middle = Middle(lags);
            var deviations = lags.Select(l => Math.Abs(l - middle)).ToList();
            var reach = Math.Max(SteadyTaps, 3 * 1.4826 * Middle(deviations));
            var steady = lags.Where(l => Math.Abs(l - middle) <= reach).ToList();
            if (steady.Count >= needed)
            {
                var mean = steady.Average();
                var spread = Math.Sqrt(steady.Sum(l => (l - mean) * (l - mean)) / steady.Count);
                calibration.Result = MathF.Round((float)(mean * 1000));
                calibration.Spread = MathF.Round((float)(spread * 1000));
                state = CalibrationState.Measured;
            }

            calibration.Taps = steady.Count;
        }

        EndCalibration(calibration, state);
        calibration.RaiseFinished();
    }

    private static double Middle(List<double> values)
    {
        var sorted = values.Order().ToList();
        var half = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[half] : (sorted[half - 1] + sorted[half]) / 2;
    }

    // Done or stopped: the music that was playing comes back, and so do the buttons.
    private void EndCalibration(Calibration calibration, CalibrationState state, bool resume = true)
    {
        calibration.State = state;
        if (Audio.Music == IAudio.MetronomeMusic)
        {
            Audio.StopMusic();
            if (resume && calibration.Resume is { } music)
            {
                var playing = Scene.Active<BackgroundMusic>().FirstOrDefault(m => string.Equals(m.Music?.Trim(), music.Trim(), StringComparison.OrdinalIgnoreCase));
                Audio.PlayMusic(music, playing is null ? 0.6f : Math.Clamp(playing.Volume, 0, 1), playing?.Loop ?? true);
            }
        }

        calibration.Resume = null;
        foreach (var hidden in calibration.Hidden)
            hidden.IsEnabled = true;
        calibration.Hidden.Clear();
    }

    // Made again only when what it says changes, so reading it allocates nothing.
    private static void UpdatePrompt(Calibration calibration)
    {
        var from = (calibration.State, calibration.Measures, calibration.Taps, calibration.Tapped.Length, calibration.Result,
            calibration.Spread, calibration.Applied, calibration.TapAction, calibration.Clicks);
        if (calibration.PromptMadeFrom is { } made && made.Equals(from))
            return;
        calibration.PromptMadeFrom = from;
        calibration.Prompt = Prompt(calibration);
    }

    private static string Prompt(Calibration calibration)
    {
        var seen = calibration.Measures == CalibrationKind.Visual;
        var cue = seen ? "flash" : "click";
        var what = seen ? "visual" : "audio";
        var action = string.IsNullOrWhiteSpace(calibration.TapAction) ? null : $"{{{calibration.TapAction.Trim()}}}";
        var how = (action, calibration.Clicks) switch
        {
            (not null, true) => $"{action}, a click or a touch",
            (not null, false) => action,
            (null, true) => "a click or a touch",
            _ => "nothing (set its Tap Action)",
        };
        var result = calibration.Result;
        var amount = $"{Math.Abs(result):0} ms {(result < 0 ? "before" : "after")}";
        return calibration.State switch
        {
            CalibrationState.Listening => seen
                ? $"Watch the light, then tap along\neach time it flashes: {how}."
                : $"Listen to the clicks, then tap along\nto each one: {how}.",
            CalibrationState.Tapping => $"Tap along to each {cue}:\n{calibration.Taps} of {calibration.Tapped.Length}",
            CalibrationState.Measured when calibration.Applied => $"Your {what} offset is now {result:0} ms.",
            CalibrationState.Measured => $"Your taps came {amount} the {cue}s\n(give or take {calibration.Spread:0} ms). Apply it?",
            CalibrationState.Failed => $"Only {calibration.Taps} taps were steady enough to tell.\nTry again, tapping on each {cue}.",
            _ => seen
                ? "Tap along to a flashing light\nto set how late you see the screen."
                : "Tap along to clicks\nto set how late you hear the sound.",
        };
    }
}
