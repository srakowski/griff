using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Fighting;

/// <summary>
/// Runs fighting games' character selects (each player's cursor on their own controls, the picks kept and the fight's
/// scene loaded) and versus matches (the fighters picked put in, rounds announced, fought against the clock, won by a
/// K.O. or on time, and the match won), before the fighters move each step. Where the game is run (offline, or the host);
/// everything is counted in the game's own time, so a match plays the same every time and across a rewind.
/// </summary>
internal sealed class VersusSystem : GameSystem
{
    // Stick past this counts as a direction for moving a cursor; held, it moves again after RepeatDelay, then every RepeatEvery.
    private const float StickThreshold = 0.5f;
    private const float RepeatDelay = 0.4f;
    private const float RepeatEvery = 0.12f;

    // How long "Fight!" stays up once the round has started.
    private const float FightShown = 0.8f;

    // In training, how long a fighter knocked out stays down before getting up again.
    private const float TrainingGetUp = 1;

    public override string Name => "Versus";

    // The fighters a select's previews were last made from, so a preview is made again only when its character changes.
    [NotState("worked out again from the selects")]
    private readonly Dictionary<Entity, string> _previewOf = [];

    // What each select's players played with when their controls were last described, so they're described again only on a change.
    [NotState("worked out again from the selects")]
    private readonly Dictionary<CharacterSelect, ((KeySet, int) First, (KeySet, int) Second, int Humans)> _described = [];

    protected override void SceneChanged()
    {
        _previewOf.Clear();
        _described.Clear();
    }

    // ----- Character select -----

    internal void RunSelects()
    {
        var game = Game;
        if (game.PausedThisFrame)
            return;
        var selects = game.Active<CharacterSelect>();
        for (var i = 0; i < selects.Count; i++)
        {
            if (!selects[i].Entity!.IsPaused)
                Select(selects[i]);
        }
    }

    private void Select(CharacterSelect select)
    {
        var game = Game;
        var count = select.CharacterList.Count;
        var humans = Math.Clamp(select.Players, 1, 2);
        var players = game.LocalPlayers;
        var devices = (players.DeviceOf(1, humans), players.DeviceOf(2, humans), humans);
        if (!_described.TryGetValue(select, out var described) || described != devices)
        {
            _described[select] = devices;
            select.FirstControls = players.Describe(1, humans);
            select.SecondControls = humans >= 2 ? players.Describe(2, humans) : select.Computer ? "Computer" : "";
        }

        if (select.SwapRequested)
        {
            select.SwapRequested = false;
            if (humans >= 2)
                players.Swap(1, 2, humans);
        }

        if (count == 0)
            return;
        (select.FirstIndex, select.SecondIndex) = (Math.Clamp(select.FirstIndex, 0, count - 1), Math.Clamp(select.SecondIndex, 0, count - 1));

        // Everyone has picked: a moment, then the fight.
        if (AllPicked(select, humans))
        {
            Place(select);
            if (select.Timer > 0)
            {
                select.Timer -= game.Time.UnscaledDelta;
                if (select.Timer <= 0)
                    Finish(select, humans);
            }

            return;
        }

        // A card clicked (or tapped), or a pick asked for by name: for the first player still choosing.
        if (select.PickRequested is { } named)
        {
            select.PickRequested = null;
            for (var c = 0; c < count; c++)
            {
                if (string.Equals(CharacterSelect.CharacterName(select.CharacterList[c]), CharacterSelect.CharacterName(named), StringComparison.OrdinalIgnoreCase))
                {
                    PickFor(select, select.FirstPicked ? 2 : 1, c, humans);
                    break;
                }
            }
        }

        if (!select.Controllable)
        {
            Place(select);
            return;
        }

        if (game.Input.WasPressed(MouseButton.Left) && game.Input.IsMouseInView && !game.MouseOnUi && CardUnder(select, game.Input.MouseWorld) is { } clicked)
            PickFor(select, select.FirstPicked && humans >= 2 ? 2 : 1, clicked, humans);

        for (var player = 1; player <= humans; player++)
        {
            var input = players.Input(player, humans);
            var picked = player == 1 ? select.FirstPicked : select.SecondPicked;
            if (picked)
            {
                if (Pressed(input, select.BackAction))
                    Unpick(select, player, humans);
                continue;
            }

            var (x, y) = Step(select, input, player - 1);
            if (x != 0 || y != 0)
                MoveCursor(select, player, x, y, count);
            if (PressedAny(input, select.PickList))
                PickFor(select, player, player == 1 ? select.FirstIndex : select.SecondIndex, humans);
        }

        Place(select);
    }

    private static bool AllPicked(CharacterSelect select, int humans) =>
        select.FirstPicked && (humans < 2 && !select.Computer || select.SecondPicked);

    // The way a player pushes the stick this step, once as it's pushed and again while it's held.
    private (int X, int Y) Step(CharacterSelect select, IInput input, int slot)
    {
        var (h, v) = (input.Horizontal, input.Vertical);
        var x = h > StickThreshold ? 1 : h < -StickThreshold ? -1 : 0;
        var y = v > StickThreshold ? 1 : v < -StickThreshold ? -1 : 0;
        var (lastX, lastY, held) = select.LastHeld[slot];
        if (x == 0 && y == 0)
        {
            select.LastHeld[slot] = (0, 0, 0);
            return (0, 0);
        }

        if (x != lastX || y != lastY)
        {
            select.LastHeld[slot] = (x, y, 0);
            return (x, y);
        }

        var before = held;
        held += Game.Time.UnscaledDelta;
        select.LastHeld[slot] = (x, y, held);
        var repeats = before >= RepeatDelay ? (int)((before - RepeatDelay) / RepeatEvery) : -1;
        var now = held >= RepeatDelay ? (int)((held - RepeatDelay) / RepeatEvery) : -1;
        return now > repeats ? (x, y) : (0, 0);
    }

    private void MoveCursor(CharacterSelect select, int player, int x, int y, int count)
    {
        var columns = select.Columns > 0 ? select.Columns : count;
        var index = player == 1 ? select.FirstIndex : select.SecondIndex;
        var (row, column) = (index / columns, index % columns);
        var rows = (count + columns - 1) / columns;
        column = ((column + x) % columns + columns) % columns;
        row = ((row - y) % rows + rows) % rows;
        var next = Math.Min(count - 1, row * columns + column);
        if (next == index)
            return;
        if (player == 1)
            select.FirstIndex = next;
        else
            select.SecondIndex = next;
        if (!string.IsNullOrWhiteSpace(select.MoveSound))
            Game.Audio.Play(select.MoveSound.Trim());
    }

    private void PickFor(CharacterSelect select, int player, int index, int humans)
    {
        if (player == 1)
        {
            if (select.FirstPicked)
                return;
            (select.FirstIndex, select.FirstPicked) = (index, true);
            // The computer takes the one after.
            if (humans < 2 && select.Computer)
                (select.SecondIndex, select.SecondPicked) = ((index + 1) % select.CharacterList.Count, true);
        }
        else
        {
            if (select.SecondPicked || humans < 2)
                return;
            (select.SecondIndex, select.SecondPicked) = (index, true);
        }

        if (!string.IsNullOrWhiteSpace(select.PickSound))
            Game.Audio.Play(select.PickSound.Trim());
        if (AllPicked(select, humans))
        {
            select.Timer = Math.Max(0.0001f, select.Delay);
            if (select.Delay <= 0)
                Finish(select, humans);
        }
    }

    private static void Unpick(CharacterSelect select, int player, int humans)
    {
        if (player == 1)
        {
            select.FirstPicked = false;
            if (humans < 2)
                select.SecondPicked = false;
        }
        else
        {
            select.SecondPicked = false;
        }
    }

    // Everyone has picked: the picks kept, and the fight loaded.
    private void Finish(CharacterSelect select, int humans)
    {
        var game = Game;
        if (!string.IsNullOrWhiteSpace(select.SaveAs))
        {
            var key = select.SaveAs.Trim();
            var saves = game.SaveData;
            saves.Set(key + ".1", CharacterSelect.CharacterName(select.CharacterList[select.FirstIndex]));
            saves.Set(key + ".2", CharacterSelect.CharacterName(select.CharacterList[select.SecondIndex]));
            saves.Set(key + ".Computer", humans < 2 && select.Computer);
            if (game.SceneName is { } scene)
                saves.Set(key + ".Scene", scene);
        }

        select.RaisePicked();
        if (!string.IsNullOrWhiteSpace(select.Scene))
            game.LoadScene(select.Scene.Trim(), 0.3f);
    }

    // The cursors over the cards each player is on, and the previews showing them.
    private void Place(CharacterSelect select)
    {
        var cards = select.Entity!.Children;
        var humans = Math.Clamp(select.Players, 1, 2);
        PlaceCursor(select.FirstCursor, cards, select.FirstIndex, true);
        PlaceCursor(select.SecondCursor, cards, select.SecondIndex, humans >= 2 || select.SecondPicked);
        Preview(select.FirstPreview, select.CharacterList[select.FirstIndex], visible: true, flip: false);
        Preview(select.SecondPreview, select.CharacterList[select.SecondIndex], visible: humans >= 2 || select.SecondPicked, flip: true);
    }

    private void PlaceCursor(string? name, IReadOnlyList<Entity> cards, int index, bool visible)
    {
        if (string.IsNullOrWhiteSpace(name) || Named(name.Trim()) is not { } cursor)
            return;
        if (cursor.IsEnabled != visible)
            cursor.IsEnabled = visible;
        if (index < 0 || index >= cards.Count)
            return;
        var at = cards[index].Transform.WorldPosition;
        var position = new Vector3(at.X, at.Y, cursor.Transform.Position.Z);
        if (cursor.Parent is null && cursor.Transform.Position != position)
            cursor.Transform.Position = position;
    }

    // A preview takes its character's picture, sheet and clips (from its prefab, read once a change).
    private void Preview(string? name, string character, bool visible, bool flip)
    {
        if (string.IsNullOrWhiteSpace(name) || Named(name.Trim()) is not { } preview)
            return;
        if (preview.IsEnabled != visible)
            preview.IsEnabled = visible;
        if (_previewOf.TryGetValue(preview, out var shown) && shown == character)
            return;
        _previewOf[preview] = character;
        if (Game.TryLoadPrefab(character, null, "Character Select: the fighter prefab") is not { } fighter)
            return;

        if (fighter.GetComponent<Sprite>() is { } from && preview.GetComponent<Sprite>() is { } sprite)
        {
            (sprite.Image, sprite.Columns, sprite.Rows, sprite.Frame, sprite.Tint) = (from.Image, from.Columns, from.Rows, from.Frame, from.Tint);
            sprite.FlipX = flip;
        }

        if (fighter.GetComponent<Animation>() is { } clips && preview.GetComponent<Animation>() is { } animation)
        {
            (animation.Clips, animation.Clip, animation.Time, animation.Playing) = (clips.Clips, "Idle", 0, true);
        }
    }

    private static int? CardUnder(CharacterSelect select, Vector2 point)
    {
        var cards = select.Entity!.Children;
        for (var i = 0; i < Math.Min(cards.Count, select.CharacterList.Count); i++)
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

    private static bool Pressed(IInput input, string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return false;
        try
        {
            return input.WasPressed(action.Trim());
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool PressedAny(IInput input, IReadOnlyList<string> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            if (Pressed(input, actions[i]))
                return true;
        }

        return false;
    }

    // ----- Versus matches -----

    internal void RunMatches()
    {
        var game = Game;
        if (game.PausedThisFrame)
            return;
        var matches = game.Active<VersusMatch>();
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (match.Entity!.IsPaused)
                continue;
            match.ShowingHitboxes = Game.GetSystem<HitboxesSystem>()?.Show ?? false;
            Run(match);
        }
    }

    private void Run(VersusMatch match)
    {
        var game = Game;
        if (match.Phase == VersusPhase.Starting)
        {
            PutInPicks(match);
            if (match.RoundsToWin <= 0)
            {
                // Training: straight in, no clock.
                (match.Round, match.Phase, match.Announcement) = (1, VersusPhase.Fighting, null);
                return;
            }

            StartRound(match);
            return;
        }

        var (first, second) = (Named(match.First), Named(match.Second));
        if (match.ToggleRequested)
        {
            match.ToggleRequested = false;
            if (game.GetSystem<HitboxesSystem>() is { } hitboxes)
                match.ShowingHitboxes = hitboxes.Show = !hitboxes.Show;
        }

        if (match.ResetRequested)
        {
            match.ResetRequested = false;
            Place(first, match.FirstStart, match.SecondStart);
            Place(second, match.SecondStart, match.FirstStart);
        }

        if (match.RematchRequested)
        {
            match.RematchRequested = false;
            (match.Round, match.FirstWins, match.SecondWins, match.Winner) = (0, 0, 0, null);
            StartRound(match);
            return;
        }

        var seconds = game.Time.Delta;
        if (seconds <= 0)
            return;
        switch (match.Phase)
        {
            case VersusPhase.Intro:
                match.Timer -= seconds;
                if (match.Timer > 0)
                    break;
                (match.Phase, match.Timer, match.Announcement) = (VersusPhase.Fighting, FightShown, "Fight!");
                Hold(first, false);
                Hold(second, false);
                Play(match.FightSound);
                break;

            case VersusPhase.Fighting:
                if (match.RoundsToWin <= 0)
                {
                    Train(match, first, second, seconds);
                    break;
                }

                if (match.Timer > 0)
                {
                    match.Timer -= seconds;
                    if (match.Timer <= 0)
                        match.Announcement = null;
                }

                if (match.RoundTime > 0)
                    match.Clock = Math.Max(0, match.Clock - seconds);
                var (firstDown, secondDown) = (Down(first), Down(second));
                if (firstDown || secondDown)
                    EndRound(match, first, second, firstDown && secondDown ? 0 : firstDown ? 2 : 1, knockout: true);
                else if (match.RoundTime > 0 && match.Clock <= 0)
                    EndRound(match, first, second, OnTime(first, second), knockout: false);
                break;

            case VersusPhase.RoundOver:
                match.Timer -= seconds;
                if (match.Timer > 0)
                    break;
                if (match.FirstWins >= match.RoundsToWin || match.SecondWins >= match.RoundsToWin)
                    Win(match, first, second);
                else
                    StartRound(match);
                break;
        }
    }

    // As the match starts: the fighters picked in place of the two in the scene, the computer given the second if it's
    // its, and where each starts.
    private void PutInPicks(VersusMatch match)
    {
        var game = Game;
        var saves = game.SaveData;
        var key = string.IsNullOrWhiteSpace(match.Picks) ? null : match.Picks.Trim();
        var computer = match.Computer || key is not null && saves.GetBool(key + ".Computer");
        var (first, firstName) = PutIn(Named(match.First), key is null ? null : saves.GetString(key + ".1"));
        var (second, secondName) = PutIn(Named(match.Second), key is null ? null : saves.GetString(key + ".2"));

        if (second is not null && computer)
        {
            if (second.GetComponent<PlayedBy>() is { } played)
                second.RemoveComponent(played);
            var cpu = second.GetComponent<ComputerFighter>() ?? second.AddComponent<ComputerFighter>();
            (cpu.Behavior, cpu.Difficulty) = (match.ComputerBehavior, match.ComputerDifficulty);
            if (second.GetComponent<FightingCharacter>() is { } fighter)
                fighter.Controllable = false;
        }

        // The same character twice: the second in another color.
        if (firstName.Length > 0 && string.Equals(firstName, secondName, StringComparison.OrdinalIgnoreCase) && second?.GetComponent<Sprite>() is { } tinted)
            tinted.Tint = match.SecondTint;

        (match.FirstName, match.SecondName) = (firstName, secondName);
        match.FirstStart = Where(first);
        match.SecondStart = Where(second);
    }

    // The fighter picked in place of the one placed (keeping its name, place, facing, player and opponent), or the placed
    // one if it's already that character or none was picked; and the character's name, as shown.
    private (Entity? Fighter, string Name) PutIn(Entity? placed, string? character)
    {
        if (placed is null || string.IsNullOrWhiteSpace(character) ||
            string.Equals(CharacterSelect.CharacterName(placed.Prefab), CharacterSelect.CharacterName(character), StringComparison.OrdinalIgnoreCase))
            return (placed, DisplayName(placed));
        if (Game.TryLoadPrefab(character, null, $"Versus Match: the fighter to put in for {placed.Name}") is not { } picked)
            return (placed, DisplayName(placed));

        picked.Name = placed.Name;
        var at = placed.Transform.Position;
        picked.Transform.Position = new Vector3(at.X, at.Y, picked.Transform.Position.Z);
        if (placed.GetComponent<PlayedBy>() is { } played)
            (picked.GetComponent<PlayedBy>() ?? picked.AddComponent<PlayedBy>()).Player = played.Player;
        if (placed.GetComponent<FightingCharacter>() is { } was && picked.GetComponent<FightingCharacter>() is { } now)
            now.Opponent = was.Opponent;
        if (placed.GetComponent<Sprite>() is { } before && picked.GetComponent<Sprite>() is { } after)
            after.FlipX = before.FlipX;
        placed.Name += " (replaced)";
        placed.IsEnabled = false;
        Game.Destroy(placed);
        Game.Spawn(picked, placed.Parent);
        return (picked, CharacterSelect.CharacterName(character));
    }

    private static string DisplayName(Entity? fighter) =>
        fighter is null ? "" : fighter.Prefab is { Length: > 0 } prefab ? CharacterSelect.CharacterName(prefab) : fighter.Name;

    private static Vector2 Where(Entity? fighter) =>
        fighter is null ? Vector2.Zero : new Vector2(fighter.Transform.Position.X, fighter.Transform.Position.Y);

    private void StartRound(VersusMatch match)
    {
        var (first, second) = (Named(match.First), Named(match.Second));
        match.Round++;
        var final = match.RoundsToWin > 1 && match.FirstWins == match.RoundsToWin - 1 && match.SecondWins == match.RoundsToWin - 1;
        (match.Phase, match.Timer, match.Clock) = (VersusPhase.Intro, Math.Max(0, match.IntroTime), Math.Max(0, match.RoundTime));
        match.Announcement = final ? "Final Round" : $"Round {match.Round}";
        Place(first, match.FirstStart, match.SecondStart);
        Place(second, match.SecondStart, match.FirstStart);
        Hold(first, true);
        Hold(second, true);
        Play(match.RoundSound);
        if (!string.IsNullOrWhiteSpace(match.RoundMessage))
            Game.Send(match.RoundMessage.Trim(), match.Round, from: match.Entity);
    }

    // A fighter back where it starts, facing the other, at full health, free.
    private static void Place(Entity? fighter, Vector2 start, Vector2 otherStart)
    {
        if (fighter is null)
            return;
        fighter.Transform.Position = new Vector3(start, fighter.Transform.Position.Z);
        if (fighter.GetComponent<Body>() is { } body)
            body.Velocity = Vector2.Zero;
        GetUp(fighter);
        if (fighter.GetComponent<Sprite>() is { } sprite)
        {
            var left = otherStart.X < start.X;
            if (fighter.Transform.WorldMatrix.M11 < 0)
                left = !left;
            sprite.FlipX = left;
        }
    }

    // Back at full health and free of hitstun and whatever it was doing.
    private static void GetUp(Entity fighter)
    {
        if (fighter.GetComponent<Health>() is { } health)
            health.Current = health.Max;
        fighter.GetComponent<Hitboxes>()?.Recover();
        fighter.GetComponent<FightingCharacter>()?.ResetForRound();
        if (fighter.GetComponent<Animation>() is { } animation && animation.ClipList.Any(c => c.Name == "Idle"))
            (animation.Clip, animation.Time, animation.Playing) = ("Idle", 0, true);
    }

    private static void Hold(Entity? fighter, bool held)
    {
        if (fighter?.GetComponent<FightingCharacter>() is { } f && f.Held != held)
            f.Held = held;
    }

    private static bool Down(Entity? fighter) => fighter?.GetComponent<Health>() is { IsDead: true };

    // The time's up: the one with more health left (as a share of its most) wins; 0 for a draw.
    private static int OnTime(Entity? first, Entity? second)
    {
        static float Left(Entity? fighter) => fighter?.GetComponent<Health>() is { Max: > 0 } h ? h.Current / h.Max : 0;
        var (a, b) = (Left(first), Left(second));
        return MathF.Abs(a - b) < 1e-4f ? 0 : a > b ? 1 : 2;
    }

    // A round won by `winner` (1 or 2; 0 for neither), by a K.O. or on time.
    private void EndRound(VersusMatch match, Entity? first, Entity? second, int winner, bool knockout)
    {
        if (winner == 1)
            match.FirstWins++;
        else if (winner == 2)
            match.SecondWins++;
        (match.Phase, match.Timer) = (VersusPhase.RoundOver, Math.Max(0, match.RoundOverTime));
        match.Announcement = knockout ? winner == 0 ? "Double K.O." : "K.O." : winner == 0 ? "Draw" : "Time";
        Hold(first, true);
        Hold(second, true);
        if (knockout)
        {
            Play(match.KoSound);
            if (match.KoFreeze > 0)
                Game.HitStop(match.KoFreeze);
        }

        var by = winner == 1 ? first : winner == 2 ? second : null;
        if (!string.IsNullOrWhiteSpace(match.RoundOverMessage))
            Game.Send(match.RoundOverMessage.Trim(), winner == 1 ? match.FirstName : winner == 2 ? match.SecondName : "Draw", from: match.Entity);
        match.RaiseRoundWon(by);
    }

    private void Win(VersusMatch match, Entity? first, Entity? second)
    {
        var firstWon = match.FirstWins >= match.RoundsToWin;
        var secondWon = match.SecondWins >= match.RoundsToWin;
        var winner = firstWon == secondWon ? null : firstWon ? first : second;
        match.Phase = VersusPhase.Over;
        match.Winner = winner is null ? "Draw" : firstWon ? match.FirstName : match.SecondName;
        match.Announcement = winner is null ? "Draw" : $"{match.Winner} Wins";
        Play(match.WinSound);
        if (!string.IsNullOrWhiteSpace(match.WinMessage))
            Game.Send(match.WinMessage.Trim(), match.Winner, from: match.Entity);
        match.RaiseWon(winner);
    }

    // Training: no clock and no rounds; a fighter knocked out gets up again a moment later.
    private static void Train(VersusMatch match, Entity? first, Entity? second, float seconds)
    {
        if (!Down(first) && !Down(second))
        {
            match.Timer = 0;
            return;
        }

        if (match.Timer <= 0)
            match.Timer = TrainingGetUp;
        match.Timer -= seconds;
        if (match.Timer > 0)
            return;
        match.Timer = 0;
        if (Down(first))
            GetUp(first!);
        if (Down(second))
            GetUp(second!);
    }

    private void Play(string? sound)
    {
        if (!string.IsNullOrWhiteSpace(sound))
            Game.PlayShared(sound.Trim());
    }

    // The enabled entity called `name`.
    private Entity? Named(string? name) => EntityNames.Find(Game.Scene, name, new NameLookup(Where: static e => e.IsEnabled));
}
