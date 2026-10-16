using System.Numerics;
using Griff.Data;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Rhythm;

/// <summary>
/// Runs song selects: the player's keys, gamepad, clicks and taps move between the songs and difficulties, the song picked
/// out is previewed and its notes and best score are read, and a song played is kept in the save data and its scene
/// loaded. Each machine runs its own (a menu, like a HUD), in real time.
/// </summary>
internal sealed class SongSelectSystem : GameSystem
{
    // Stick past this counts as a direction; held, it moves again after RepeatDelay, then every RepeatEvery.
    private const float StickThreshold = 0.5f;
    private const float RepeatDelay = 0.4f;
    private const float RepeatEvery = 0.12f;

    // The cursor glides to the card picked out: this much of the way a second (as a fraction left over).
    private const float Glide = 18;

    public override string Name => "Song Select";

    internal void Run()
    {
        var game = Game;
        if (game.PausedThisFrame)
            return;
        var selects = game.Showing<SongSelect>();
        for (var i = 0; i < selects.Count; i++)
        {
            var select = selects[i];
            if (select.Entity is { } entity && entity.Scene == game.Scene && !entity.IsPaused)
                Step(select);
        }
    }

    private void Step(SongSelect select)
    {
        var game = Game;
        var count = select.Songs.Count;
        if (count == 0)
            return;
        var difficulties = select.Difficulties.Count;
        if (!select.Seen)
        {
            select.Seen = true;
            Restore(select);
            select.Previewing = null;
        }

        (select.Index, select.DifficultyIndex) = (Wrap(select.Index, count), Math.Clamp(select.DifficultyIndex, 0, Math.Max(0, difficulties - 1)));

        // Picked: a moment to hear it, then its scene.
        if (select.Picked)
        {
            ReadBests(select);
            Place(select, snap: false);
            if (select.Timer > 0)
            {
                select.Timer -= game.Time.UnscaledDelta;
                if (select.Timer <= 0)
                    Finish(select);
            }

            return;
        }

        // Its actions: steps through the songs and difficulties, a song named, or play.
        var (songs, steps, play) = select.Requested;
        select.Requested = default;
        if (songs != 0)
            MoveTo(select, select.Index + songs);
        if (steps != 0)
            Harder(select, steps);
        if (select.PickRequested is { } named)
        {
            select.PickRequested = null;
            if (select.IndexOf(named) is var index and >= 0)
            {
                MoveTo(select, index);
                play = true;
            }
            else
            {
                Game.Warn($"Song Select \"{select.Entity!.Name}\" has no song called \"{named}\" (its songs: {string.Join(", ", select.Titles)}).");
            }
        }

        if (!play && select.Controllable && Listening(select))
            play = ReadInput(select);

        ReadBests(select);
        Preview(select);
        Place(select, snap: false);
        if (play)
            PlaySong(select);
    }

    // As it's first seen: the song and difficulty played last, so coming back finds them picked out again.
    private void Restore(SongSelect select)
    {
        var saves = Game.SaveData;
        if (!string.IsNullOrWhiteSpace(select.SaveAs) && saves.GetString(select.SaveAs.Trim()) is { } song && select.IndexOf(song) is var index and >= 0)
            select.Index = index;
        if (!string.IsNullOrWhiteSpace(select.SaveDifficultyAs) && saves.GetString(select.SaveDifficultyAs.Trim()) is { } difficulty)
        {
            for (var i = 0; i < select.Difficulties.Count; i++)
            {
                if (string.Equals(select.Difficulties[i], difficulty.Trim(), StringComparison.OrdinalIgnoreCase))
                    select.DifficultyIndex = i;
            }
        }

        Place(select, snap: true);
    }

    // Like a menu, it listens while it's on the top panel (or none is open).
    private bool Listening(SongSelect select) =>
        Game.TopPanel is not { Entity: { } owner } || select.Entity!.IsSelfOrDescendantOf(owner);

    // Up and down move between the songs, left and right between the difficulties; a play action plays, Back goes back, and
    // a click on a card picks it out (on the one picked out, plays it). True to play.
    private bool ReadInput(SongSelect select)
    {
        var game = Game;
        var input = game.Input;
        var (x, y) = Held(select, input);
        if (y != 0)
            MoveTo(select, select.Index - y);
        if (x != 0)
            Harder(select, x);

        if (!string.IsNullOrWhiteSpace(select.Back) &&
            (input.WasPressed(Key.Escape) || input.WasPressed(Key.Backspace) || input.WasPressed(GamepadButton.B)))
        {
            Audio(select.MoveSound);
            game.LoadScene(select.Back.Trim(), 0.3f);
            return false;
        }

        foreach (var action in select.PlayList)
        {
            if (WasPressed(input, action))
                return true;
        }

        if (select.Clicks && input.WasPressed(MouseButton.Left) && input.IsMouseInView && !game.MouseOnUi &&
            CardUnder(select, input.MouseWorld) is { } card)
        {
            if (card == select.Index)
                return true;
            MoveTo(select, card);
        }

        return false;
    }

    private static bool WasPressed(IInput input, string action)
    {
        try
        {
            return input.WasPressed(action);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // The way the player pushes this step, once as it's pushed and again while it's held.
    private (int X, int Y) Held(SongSelect select, IInput input)
    {
        var (h, v) = (input.Horizontal, input.Vertical);
        var x = h > StickThreshold ? 1 : h < -StickThreshold ? -1 : 0;
        var y = v > StickThreshold ? 1 : v < -StickThreshold ? -1 : 0;
        // One way at a time: up and down first.
        if (y != 0)
            x = 0;
        var (lastX, lastY, held) = select.LastHeld;
        if (x == 0 && y == 0)
        {
            select.LastHeld = (0, 0, 0);
            return (0, 0);
        }

        if (x != lastX || y != lastY)
        {
            select.LastHeld = (x, y, 0);
            return (x, y);
        }

        var before = held;
        held += Game.Time.UnscaledDelta;
        select.LastHeld = (x, y, held);
        var repeats = before >= RepeatDelay ? (int)((before - RepeatDelay) / RepeatEvery) : -1;
        var now = held >= RepeatDelay ? (int)((held - RepeatDelay) / RepeatEvery) : -1;
        return now > repeats ? (x, y) : (0, 0);
    }

    private void MoveTo(SongSelect select, int index)
    {
        index = Wrap(index, select.Songs.Count);
        if (index == select.Index)
            return;
        select.Index = index;
        Audio(select.MoveSound);
    }

    // A step harder (or easier), stopping at either end.
    private void Harder(SongSelect select, int by)
    {
        var to = Math.Clamp(select.DifficultyIndex + by, 0, Math.Max(0, select.Difficulties.Count - 1));
        if (to == select.DifficultyIndex)
            return;
        select.DifficultyIndex = to;
        Audio(select.MoveSound);
    }

    private void PlaySong(SongSelect select)
    {
        select.Picked = true;
        select.Timer = Math.Max(0.0001f, select.Delay);
        Audio(select.PickSound);
        if (select.Previewing is not null && string.Equals(Game.Audio.Music, select.Previewing, StringComparison.OrdinalIgnoreCase))
            Game.Audio.FadeOutMusic(Math.Max(0.05f, select.Delay));
        select.Previewing = null;
        if (select.Delay <= 0)
            Finish(select);
    }

    // Played: the song and difficulty kept for the lanes, and their scene loaded.
    private void Finish(SongSelect select)
    {
        var saves = Game.SaveData;
        if (!string.IsNullOrWhiteSpace(select.SaveAs))
            saves.Set(select.SaveAs.Trim(), select.Songs[select.Index].Trim());
        if (!string.IsNullOrWhiteSpace(select.SaveDifficultyAs) && select.Difficulty.Length > 0)
            saves.Set(select.SaveDifficultyAs.Trim(), select.Difficulty);
        select.RaisePlayed();
        if (!string.IsNullOrWhiteSpace(select.Scene))
            Game.LoadScene(select.Scene.Trim(), 0.3f);
    }

    // The song picked out plays over and over (from where its preview starts) while it's picked out.
    private void Preview(SongSelect select)
    {
        if (!select.Preview)
            return;
        var song = select.Songs[select.Index];
        if (string.IsNullOrWhiteSpace(song) || ReferenceEquals(select.Previewing, song))
            return;
        select.Previewing = song;
        var volume = float.IsFinite(select.PreviewVolume) ? Math.Clamp(select.PreviewVolume, 0, 1) : 0.7f;
        Game.Audio.PlayMusicFrom(song.Trim(), Math.Max(0, select.PreviewFrom), volume, loop: true);
    }

    // The best score and grade of each song at the difficulty picked out, and the picked out song's notes: read again only
    // as the difficulty, the song, the save data or the tables change, so a step makes no garbage.
    private void ReadBests(SongSelect select)
    {
        var saves = Game.SaveData;
        var key = (select.DifficultyIndex, saves.Version, Game.Tables.Version, select.Index);
        if (select.ReadFor == key)
            return;
        select.ReadFor = key;
        var difficulty = select.Difficulty;
        select.Bests.Clear();
        foreach (var title in select.Titles)
        {
            if (string.IsNullOrWhiteSpace(select.BestFrom) || title.Length == 0)
            {
                select.Bests[title] = (0, "");
                continue;
            }

            var best = NoteLanes.BestKey(select.BestFrom, title, difficulty);
            var played = saves.Has(best);
            select.Bests[title] = (played ? Math.Max(0, saves.GetInt(best)) : 0, played ? saves.GetString(best + ".Grade") ?? "" : "");
        }

        (select.Best, select.BestGrade) = select.Bests.TryGetValue(select.Title, out var kept) ? kept : (0, "");
        select.Notes = NotesOf(select.Title, difficulty);
    }

    private int NotesOf(string chart, string difficulty)
    {
        if (chart.Length == 0)
            return 0;
        Table? table;
        try
        {
            table = Game.Tables.Find(chart);
        }
        catch (InvalidDataException)
        {
            return 0;
        }

        return table is null ? 0 : RhythmChart.Read(table, difficulty, lanes: 16).Notes.Length;
    }

    // The cursor over the card picked out, gliding there.
    private void Place(SongSelect select, bool snap)
    {
        if (string.IsNullOrWhiteSpace(select.Cursor) || Game.Find(select.Cursor.Trim()) is not { } cursor)
            return;
        var cards = select.Entity!.Children;
        if (select.Index >= cards.Count || cursor.Parent is not null)
            return;
        var at = cards[select.Index].Transform.WorldPosition;
        var from = cursor.Transform.Position;
        var to = new Vector3(at.X, at.Y, from.Z);
        var t = snap ? 1 : 1 - MathF.Exp(-Glide * Game.Time.UnscaledDelta);
        var next = Vector3.DistanceSquared(from, to) < 1e-6f ? to : Vector3.Lerp(from, to, t);
        if (next != from)
            cursor.Transform.Position = next;
    }

    // The card (from 0) under a point in the world: its sprite's area.
    private static int? CardUnder(SongSelect select, Vector2 point)
    {
        var cards = select.Entity!.Children;
        for (var i = 0; i < Math.Min(cards.Count, select.Songs.Count); i++)
        {
            var card = cards[i];
            if (!card.IsActive || card.GetComponent<Sprite>() is not { } sprite)
                continue;
            var at = card.Transform.WorldPosition;
            var m = card.Transform.WorldMatrix;
            var half = sprite.Size * 0.5f * new Vector2(new Vector2(m.M11, m.M12).Length(), new Vector2(m.M21, m.M22).Length());
            if (MathF.Abs(point.X - at.X) <= half.X && MathF.Abs(point.Y - at.Y) <= half.Y)
                return i;
        }

        return null;
    }

    private void Audio(string? sound)
    {
        if (!string.IsNullOrWhiteSpace(sound))
            Game.Audio.Play(sound.Trim());
    }

    private static int Wrap(int index, int count) => count <= 0 ? 0 : ((index % count) + count) % count;
}
