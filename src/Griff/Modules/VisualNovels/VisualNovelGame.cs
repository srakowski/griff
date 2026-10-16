using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.VisualNovels;

/// <summary>
/// What a script can ask of a visual novel, as members of <see cref="Game"/>: how the player likes dialogue read
/// (<c>Game.SkipUnread</c>, <c>Game.AutoSpeed</c>) and the lines they've read (<c>Game.HasRead</c>).
/// </summary>
public static class VisualNovelGame
{
    extension(Game game)
    {
        /// <summary>
        /// The player's setting for whether skipping goes through lines they haven't read yet too, rather than stopping at
        /// the first. Kept in <see cref="Game.SaveData"/>; a Toggle set to Skip Unread changes it.
        /// </summary>
        public bool SkipUnread
        {
            get => ReadingOf(game).SkipUnread;
            set => ReadingOf(game).SkipUnread = value;
        }

        /// <summary>
        /// The player's setting for how quickly <see cref="Game.AutoReading"/> reads on, from 0 (slowly) to 1 (quickly); 0.5
        /// to begin with. Kept in <see cref="Game.SaveData"/>; a Slider set to Auto Speed changes it.
        /// </summary>
        public float AutoSpeed
        {
            get => ReadingOf(game).AutoSpeed;
            set => ReadingOf(game).AutoSpeed = value;
        }

        /// <summary>
        /// True if row <paramref name="row"/> of <paramref name="dialogue"/>'s lines has been shown before, in this run of
        /// the game or an earlier one: skipping goes through lines read before.
        /// </summary>
        public bool HasRead(Dialogue dialogue, int row) => ReadingOf(game).HasRead(dialogue, row);

        /// <summary>Where the lines read before are kept in <see cref="Game.SaveData"/>, so skipping knows them next time too.</summary>
        public static string ReadLinesKey => ReadingSettingsSystem.ReadLinesKey;

        /// <summary>Where <c>Game.SkipUnread</c> is kept in <see cref="Game.SaveData"/>.</summary>
        public static string SkipUnreadSaveKey => ReadingSettingsSystem.SkipUnreadSaveKey;

        /// <summary>Where <c>Game.AutoSpeed</c> is kept in <see cref="Game.SaveData"/>.</summary>
        public static string AutoSpeedSaveKey => ReadingSettingsSystem.AutoSpeedSaveKey;
    }

    // Every game runs the module's systems.
    internal static ReadingSettingsSystem ReadingOf(Game game) =>
        game.GetSystem<ReadingSettingsSystem>() ?? throw new InvalidOperationException("The game isn't running the Visual Novels module.");
}
