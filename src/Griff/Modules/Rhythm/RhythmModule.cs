using Griff.Runtime;

namespace Griff.Modules.Rhythm;

/// <summary>
/// Rhythm games: lanes of notes from a chart (a table of beats and lanes) that scroll to a hit line in time with the music
/// (<see cref="NoteLanes"/>), each press judged Perfect, Great, Good or Miss against the beat as the player hears it; and
/// Create Note Lanes in the Entity menu, with a song and its chart at three difficulties; a <see cref="SongSelect"/> to pick a
/// song and difficulty with its best score; and the Rhythm template, a whole rhythm game.
/// </summary>
public sealed class RhythmModule : GameModule
{
    public override string Name => "Rhythm";

    protected internal override void AddTo(GameSetup game)
    {
        // After the beat clocks (so the notes go by this step's beat), before scripts (so they see this step's judgments).
        // The player's note speed is kept with their settings.
        var settings = game.AddSystem(new RhythmSettingsSystem());
        var lanes = game.AddSystem(new RhythmSystem(settings));
        var laneStep = game.StepAfter(Steps.Calibrations, "Note Lanes", lanes, lanes.Run);
        game.OnNetCommand(RhythmSystem.RhythmCommand, lanes.RunCommand);

        // Song selects after the lanes (a menu: it plays the songs they play).
        var songs = game.AddSystem(new SongSelectSystem());
        game.StepAfter(laneStep, "Song Select", songs, songs.Run);
    }
}
