using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>Where a <see cref="LevelGoal"/>'s level has got to.</summary>
public enum LevelPhase
{
    /// <summary>Being played: targets still standing and shots to knock them down with.</summary>
    Playing,

    /// <summary>The last target is down, or the last shot has come to rest: waiting for things to settle before saying which.</summary>
    Settling,

    /// <summary>Won: each shot left is counted for its bonus, one after another.</summary>
    Bonus,

    /// <summary>Won, with its stars.</summary>
    Won,

    /// <summary>Lost: out of shots with targets still standing.</summary>
    Lost,
}

/// <summary>
/// What a physics puzzler's level is for, with no code: knock out every target before the slingshot runs out of shots. Once
/// the last target is down it waits for things to settle, counts a bonus for each shot left, rates the level one to three
/// stars by the score and keeps the best stars and score in the save data, and sends its <see cref="WonMessage"/>; out of
/// shots with targets still standing, it sends its <see cref="LostMessage"/>. A panel opens on each.
/// </summary>
/// <remarks>
/// The targets are everything named <see cref="Targets"/> (a Breakable is gone once broken; anything else once removed or
/// turned off), so they can be pigs in a tower or a platformer's enemies. The score is a <see cref="Counter"/>'s
/// (<see cref="Score"/>), which the targets' and blocks' Break Messages add their points to. With <see cref="SaveAs"/>
/// "Level1" it saves the best stars as "Level1.Stars" and the best score as "Level1.Best": a level select menu's
/// <see cref="Stars"/> show them, and a button that Needs Saved "Level1.Stars" opens the next level once it's won.
/// </remarks>
[Category("Physics Games")]
[WorksWith(typeof(Slingshot), Note = "its shots: out of them with targets standing loses, and each one left is a bonus")]
[WorksWith(typeof(Counter), Note = "the score (on another entity), which the stars are given by")]
[WorksWith(typeof(Stars), Note = "shows its Stars (on the panel its Won Message opens)", Back = "shows a Level Goal's stars")]
[ComponentName("Griff.LevelGoal")]
public sealed class LevelGoal : Component, IPlaytestReport
{
    /// <summary>What has to be knocked out, by name: every entity called this, e.g. "Target". The level's won once none is left.</summary>
    [EntityName(Every = true)]
    public string? Targets { get; set; } = "Target";

    /// <summary>The slingshot whose shots the level is played with, by name. Empty is the scene's first.</summary>
    [EntityName]
    public string? Slingshot { get; set; }

    /// <summary>The <see cref="Counter"/> keeping the score, by name, e.g. "Score": the stars are given by it, and the bonus added to it.</summary>
    [EntityName]
    public string? Score { get; set; } = "Score";

    /// <summary>Points for each shot left once the targets are down, added one after another.</summary>
    public int ShotBonus { get; set; } = 10000;

    /// <summary>
    /// Seconds it waits, once the last target is down or the last shot has come to rest, for things to settle before it
    /// says the level's won or lost: a target knocked out meanwhile still counts.
    /// </summary>
    public float Settle { get; set; } = 2;

    // ----- Stars -----

    /// <summary>The score that earns two stars (winning earns one).</summary>
    [Group("Stars")]
    public int TwoStars { get; set; } = 20000;

    /// <summary>The score that earns three stars.</summary>
    public int ThreeStars { get; set; } = 35000;

    /// <summary>
    /// A name to keep the level's best in the save data under, e.g. "Level1": the best stars as "Level1.Stars" and the best
    /// score as "Level1.Best", for a level select menu. Empty keeps nothing.
    /// </summary>
    [Names(NameKind.Saved, Defines = true, Suffixes = [".Stars", ".Best"])]
    public string? SaveAs { get; set; }

    // ----- Sound and messages -----

    /// <summary>A message sent as the level is won (after the bonus), with the stars as its data: a panel opens on it.</summary>
    [Group("Sound and Messages")]
    [Names(NameKind.Message)]
    public string? WonMessage { get; set; } = "Level Complete";

    /// <summary>A message sent as the level is lost, with the targets left as its data.</summary>
    [Names(NameKind.Message)]
    public string? LostMessage { get; set; } = "Level Failed";

    /// <summary>A sound played for each shot's bonus as it's counted.</summary>
    public string? BonusSound { get; set; }

    /// <summary>A sound played as the level is won.</summary>
    public string? WinSound { get; set; }

    /// <summary>A sound played as the level is lost.</summary>
    public string? LoseSound { get; set; }

    // ----- State, kept with the scene -----

    /// <summary>Where the level has got to. Set by the game.</summary>
    [Group("State")]
    public LevelPhase Phase { get; set; }

    /// <summary>Seconds until it moves on from settling, or counts the next shot's bonus. Set by the game.</summary>
    public float Timer { get; set; }

    /// <summary>How many targets the level started with. Set by the game as it starts.</summary>
    public int TargetCount { get; set; }

    /// <summary>The stars it earned, 1 to 3, once it's won; 0 before. Set by the game.</summary>
    [Output(OutOf = nameof(MaxStars))]
    public int Stars { get; set; }

    /// <summary>How many targets are still standing.</summary>
    [DoNotSerialize]
    [Output(OutOf = nameof(TargetCount))]
    public int TargetsLeft { get; internal set; }

    /// <summary>True once the level's won (and its bonus counted).</summary>
    [DoNotSerialize]
    [Output]
    public bool Won => Phase == LevelPhase.Won;

    /// <summary>True once the level's lost.</summary>
    [DoNotSerialize]
    [Output]
    public bool Lost => Phase == LevelPhase.Lost;

    /// <summary>The best stars the level has had, from the save data (with <see cref="SaveAs"/>; this run's without).</summary>
    [DoNotSerialize]
    [Output(OutOf = nameof(MaxStars))]
    public int BestStars { get; internal set; }

    /// <summary>The best score the level has been won with, from the save data (with <see cref="SaveAs"/>; this run's without).</summary>
    [DoNotSerialize]
    [Output]
    public int BestScore { get; internal set; }

    /// <summary>Three: what <see cref="Stars"/> is out of.</summary>
    [DoNotSerialize]
    public int MaxStars => 3;

    // Hidden in the Inspector: a timer only the game reads.
    public override bool Uses(string property) => property != nameof(Timer);

    /// <summary>Raised as the level is won, after its bonus, with the stars it earned.</summary>
    public event Action<LevelGoal, int>? Completed;

    /// <summary>Raised as the level is lost.</summary>
    public event Action<LevelGoal>? Failed;

    /// <summary>Wins the level now: its shots left are counted for their bonus, and its stars given.</summary>
    [Action]
    public void Win() => WinRequested = true;

    /// <summary>Loses the level now.</summary>
    [Action]
    public void Lose() => LoseRequested = true;

    /// <summary>The stars a score earns: one for winning, two from <see cref="TwoStars"/>, three from <see cref="ThreeStars"/>.</summary>
    public int StarsFor(int score) => score >= ThreeStars ? 3 : score >= TwoStars ? 2 : 1;

    /// <summary>The save data name its best stars are kept under ("Level1.Stars"), or null without <see cref="SaveAs"/>.</summary>
    public string? StarsKey => string.IsNullOrWhiteSpace(SaveAs) ? null : SaveAs.Trim() + ".Stars";

    /// <summary>The save data name its best score is kept under ("Level1.Best"), or null without <see cref="SaveAs"/>.</summary>
    public string? BestKey => string.IsNullOrWhiteSpace(SaveAs) ? null : SaveAs.Trim() + ".Best";

    // ----- Kept by the game while it runs -----

    internal bool WinRequested { get; set; }

    internal bool LoseRequested { get; set; }

    // Whether it has read its best from the save data yet.
    internal bool ReadBest;

    // The slingshot it names, found once, and the name it was found for.
    internal Slingshot? FoundSlingshot;
    internal string? FoundFor;

    internal void RaiseCompleted() => Completed?.Invoke(this, Stars);

    internal void RaiseFailed() => Failed?.Invoke(this);

    string IPlaytestReport.Report() => Phase switch
    {
        LevelPhase.Won => $", won with {Stars} star{(Stars == 1 ? "" : "s")}",
        LevelPhase.Lost => $", lost with {TargetsLeft} target{(TargetsLeft == 1 ? "" : "s")} left",
        LevelPhase.Bonus => ", counting the bonus",
        _ => $", {TargetsLeft} of {TargetCount} target{(TargetCount == 1 ? "" : "s")} left{(Phase == LevelPhase.Settling ? ", settling" : "")}",
    };
}
