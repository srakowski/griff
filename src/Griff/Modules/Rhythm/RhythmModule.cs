using Griff.Runtime;

namespace Griff.Modules.Rhythm;

/// <summary>
/// Rhythm games: lanes of notes from a chart (a table of beats and lanes) that scroll to a hit line in time with the music
/// (<see cref="NoteLanes"/>), each press judged Perfect, Great, Good or Miss against the beat as the player hears it; and
/// Create Note Lanes in the Entity menu, with a song and its chart at three difficulties.
/// </summary>
public sealed class RhythmModule : GameModule
{
    public override string Name => "Rhythm";

    protected internal override void AddTo(GameSetup game)
    {
        // After the beat clocks (so the notes go by this step's beat), before scripts (so they see this step's judgments).
        var lanes = game.AddSystem(new RhythmSystem());
        game.StepAfter("Calibrations", "Note Lanes", lanes, lanes.Run);
        game.OnNetCommand(RhythmSystem.RhythmCommand, lanes.RunCommand);
    }
}
