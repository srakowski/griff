using System.Globalization;
using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// Plays out physics puzzlers' levels with no code: counts each <see cref="LevelGoal"/>'s targets still standing, and once
/// they're all down (or its slingshot is out of shots) waits for things to settle, then counts the bonus for the shots
/// left, gives the stars, keeps the best in the save data and says the level's won, or says it's lost. Where the game is
/// run (offline, or the host); what it decides reaches players' machines as messages and its outputs.
/// </summary>
internal sealed class LevelGoalSystem : GameSystem
{
    // Seconds between one shot's bonus and the next as they're counted.
    internal const float BonusPace = 0.45f;

    public override string Name => "Level Goals";

    // After the slingshots and breaking: what this step knocked down counts.
    internal void Run()
    {
        var goals = Game.Showing<LevelGoal>();
        if (goals.Count == 0)
            return;
        var seconds = Game.Time.Delta;
        foreach (var goal in goals)
        {
            var entity = goal.Entity!;
            goal.TargetsLeft = CountTargets(goal);
            ReadBest(goal);
            if (!Game.Runs(entity) || entity.IsPaused)
                continue;
            if (goal.TargetCount == 0 && goal.Phase == LevelPhase.Playing)
                goal.TargetCount = goal.TargetsLeft;
            if (seconds > 0)
                Work(goal, seconds);
        }
    }

    private void Work(LevelGoal goal, float seconds)
    {
        if (goal.LoseRequested)
        {
            goal.LoseRequested = false;
            if (goal.Phase is LevelPhase.Playing or LevelPhase.Settling)
                Lose(goal);
        }

        if (goal.WinRequested)
        {
            goal.WinRequested = false;
            if (goal.Phase is LevelPhase.Playing or LevelPhase.Settling)
                StartBonus(goal);
        }

        var slingshot = SlingshotOf(goal);
        switch (goal.Phase)
        {
            case LevelPhase.Playing:
                // The last target down, or the last shot come to rest: wait for things to settle, the slingshot put down if
                // the targets are all gone.
                // (A level that never had a target isn't won by having none.)
                var cleared = goal.TargetsLeft == 0 && goal.TargetCount > 0;
                if (cleared || slingshot is { OutOfShots: true })
                {
                    (goal.Phase, goal.Timer) = (LevelPhase.Settling, Math.Max(0, goal.Settle));
                    if (cleared && slingshot is not null)
                        slingshot.Controllable = false;
                }

                break;

            case LevelPhase.Settling:
                if (goal.TargetsLeft == 0 && slingshot is { Controllable: true })
                    slingshot.Controllable = false;
                goal.Timer -= seconds;
                if (goal.Timer > 0)
                    break;
                if (goal.TargetsLeft == 0)
                    StartBonus(goal);
                else if (slingshot is null or { OutOfShots: true })
                    Lose(goal);
                else
                    (goal.Phase, goal.Timer) = (LevelPhase.Playing, 0);
                break;

            case LevelPhase.Bonus:
                goal.Timer -= seconds;
                if (goal.Timer > 0)
                    break;
                if (!TakeShot(goal, slingshot))
                    Finish(goal);
                else
                    goal.Timer = BonusPace;
                break;
        }
    }

    // Every entity named as its targets that's still standing: about, turned on, and not broken.
    private int CountTargets(LevelGoal goal)
    {
        var name = goal.Targets;
        if (string.IsNullOrWhiteSpace(name))
            return 0;
        return EntityNames.Count(Game.Scene, name, static e => e.IsActive && e.GetComponent<Breakable>() is not { Broken: true });
    }

    // Its best stars and score, read from the save data once.
    private void ReadBest(LevelGoal goal)
    {
        if (goal.ReadBest)
            return;
        goal.ReadBest = true;
        if (goal.StarsKey is { } stars)
            goal.BestStars = Game.SaveData.GetInt(stars);
        if (goal.BestKey is { } best)
            goal.BestScore = Game.SaveData.GetInt(best);
    }

    // Its slingshot: the one it names (found again only when that changes or it leaves), or the scene's first.
    private Slingshot? SlingshotOf(LevelGoal goal)
    {
        if (string.IsNullOrWhiteSpace(goal.Slingshot))
        {
            var slingshots = Game.Showing<Slingshot>();
            return slingshots.Count > 0 ? slingshots[0] : null;
        }

        if (goal.FoundSlingshot is { } found && ReferenceEquals(goal.FoundFor, goal.Slingshot) && found.Entity?.Scene == Game.Scene &&
            found.Entity.Name == goal.Slingshot.Trim())
            return found;
        goal.FoundFor = goal.Slingshot;
        return goal.FoundSlingshot = Named(goal.Slingshot.Trim())?.GetComponent<Slingshot>();
    }

    private Counter? ScoreOf(LevelGoal goal) => string.IsNullOrWhiteSpace(goal.Score) ? null : Named(goal.Score.Trim())?.GetComponent<Counter>();

    private Entity? Named(string name) => EntityNames.Find(Game.Scene, name);

    private void StartBonus(LevelGoal goal)
    {
        (goal.Phase, goal.Timer) = (LevelPhase.Bonus, BonusPace);
        if (SlingshotOf(goal) is { } slingshot)
            slingshot.Controllable = false;
    }

    // One shot left over taken for its bonus: the next waiting in the slingshot, or one of its prefab's still to come. False
    // once there are none.
    private bool TakeShot(LevelGoal goal, Slingshot? slingshot)
    {
        if (slingshot?.Entity is not { } entity || goal.ShotBonus == 0)
            return false;
        Vector2 at;
        if (slingshot.FirstShot is { } shot)
        {
            var position = shot.Transform.WorldPosition;
            at = new Vector2(position.X, position.Y);
            if (slingshot.Loaded)
                slingshot.Loaded = false;
            Game.Destroy(shot);
            shot.IsEnabled = false;
        }
        else if (!string.IsNullOrWhiteSpace(slingshot.Shot) && slingshot.Made < slingshot.Shots)
        {
            slingshot.Made++;
            at = slingshot.PouchInWorld;
        }
        else
        {
            return false;
        }

        ScoreOf(goal)?.Add(goal.ShotBonus);
        PointsPopup.Spawn(Game, at, goal.ShotBonus, PointsPopup.BonusColor, 0.9f);
        if (!string.IsNullOrWhiteSpace(goal.BonusSound))
            Game.PlayShared(goal.BonusSound.Trim());
        return true;
    }

    // Won: the stars its score earns, the best kept, and the panel opened.
    private void Finish(LevelGoal goal)
    {
        var score = ScoreOf(goal)?.Value ?? 0;
        goal.Stars = goal.StarsFor(score);
        goal.Phase = LevelPhase.Won;
        goal.Timer = 0;
        goal.BestStars = Math.Max(goal.BestStars, goal.Stars);
        goal.BestScore = Math.Max(goal.BestScore, score);
        if (goal.StarsKey is { } stars && Game.SaveData.GetInt(stars) < goal.Stars)
            Game.SaveData.Set(stars, goal.Stars);
        if (goal.BestKey is { } best && (!Game.SaveData.Has(best) || Game.SaveData.GetInt(best) < score))
            Game.SaveData.Set(best, score);

        if (!string.IsNullOrWhiteSpace(goal.WinSound))
            Game.PlayShared(goal.WinSound.Trim());
        if (!string.IsNullOrWhiteSpace(goal.WonMessage))
            Game.Send(goal.WonMessage.Trim(), goal.Stars, from: goal.Entity);
        goal.RaiseCompleted();
    }

    private void Lose(LevelGoal goal)
    {
        (goal.Phase, goal.Timer) = (LevelPhase.Lost, 0);
        if (!string.IsNullOrWhiteSpace(goal.LoseSound))
            Game.PlayShared(goal.LoseSound.Trim());
        if (!string.IsNullOrWhiteSpace(goal.LostMessage))
            Game.Send(goal.LostMessage.Trim(), goal.TargetsLeft, from: goal.Entity);
        goal.RaiseFailed();
    }
}

/// <summary>Points that rise from where they were scored and fade away: a broken block's, a shot's bonus.</summary>
internal static class PointsPopup
{
    /// <summary>The color a bonus's points are shown in.</summary>
    public static readonly Color BonusColor = Color.FromBytes(0xFF, 0xE0, 0x6A);

    /// <summary>How long the points are shown, in seconds.</summary>
    public const float Seconds = 1.2f;

    /// <summary>Shows <paramref name="points"/> at <paramref name="at"/>, rising and fading.</summary>
    public static void Spawn(Game game, Vector2 at, int points, Color color, float size)
    {
        var popup = new Entity("Points");
        popup.Transform.Position = new Vector3(at, 5);
        var text = popup.AddComponent<Text>();
        (text.Value, text.Size, text.Color, text.Order) = (points.ToString("N0", CultureInfo.InvariantCulture), size, color, 250);
        var lifetime = popup.AddComponent<Lifetime>();
        (lifetime.Seconds, lifetime.FadeOut) = (Seconds, 0.5f);
        game.Spawn(popup);
        game.MoveTo(popup, at + new Vector2(0, 1.5f), Seconds, Ease.OutQuad);
    }
}
