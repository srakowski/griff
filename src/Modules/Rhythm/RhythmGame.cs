using Griff.Runtime;

namespace Griff.Modules.Rhythm;

/// <summary>What a script can ask of a rhythm game, as members of <see cref="Game"/>: <c>Game.NoteSpeed</c>.</summary>
public static class RhythmGame
{
    extension(Game game)
    {
        /// <summary>
        /// How fast this player likes a rhythm game's notes to come, times what the lanes say: 2 is twice as fast (the notes
        /// further apart, and seen for half as long), 0.5 half as fast. Players who read ahead like them slow; those who like
        /// them spread out, fast. Only how they're drawn changes, never when they're to be hit. Like the offsets, it's the
        /// player's own, kept in <see cref="Game.SaveData"/> and set by a <see cref="Scenes.Slider"/> bound to it; from 0.25
        /// to 4, 1 by default.
        /// </summary>
        public float NoteSpeed
        {
            get => SettingsOf(game).NoteSpeed;
            set => SettingsOf(game).NoteSpeed = value;
        }

        /// <summary>Where <c>Game.NoteSpeed</c> is kept in <see cref="Game.SaveData"/>.</summary>
        public static string NoteSpeedSaveKey => RhythmSettingsSystem.NoteSpeedSaveKey;

        /// <summary>The slowest <c>Game.NoteSpeed</c>.</summary>
        public static float MinNoteSpeed => RhythmSettingsSystem.MinNoteSpeed;

        /// <summary>The fastest <c>Game.NoteSpeed</c>.</summary>
        public static float MaxNoteSpeed => RhythmSettingsSystem.MaxNoteSpeed;
    }

    // Every game runs the module's systems.
    internal static RhythmSettingsSystem SettingsOf(Game game) =>
        game.GetSystem<RhythmSettingsSystem>() ?? throw new InvalidOperationException("The game isn't running the Rhythm module.");
}
