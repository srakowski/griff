using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Projects;

/// <summary>Games share their starting random numbers unless the project asks for a new seed from the clock each game.</summary>
public sealed class ProjectRandomSeedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-random-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static int FirstRoll(Game game)
    {
        var previous = Rand.EnterGame(game.Random);
        try { return Rand.Range(0, 1_000_000); }
        finally { Rand.EnterGame(previous); }
    }

    [Fact]
    public async Task By_default_every_game_starts_from_the_same_numbers_and_the_setting_is_not_written()
    {
        var project = await Project.CreateAsync(_root, "Game");
        Rand.Seed(7);

        var first = new Game(new Scene(), new InputState());
        first.UseProjectSettings(project);
        var second = new Game(new Scene(), new InputState());
        second.UseProjectSettings(project);

        Assert.False(project.NewRandomEachGame);
        Assert.Equal(FirstRoll(first), FirstRoll(second));
        Assert.DoesNotContain("newRandomEachGame", await File.ReadAllTextAsync(project.ManifestPath));
    }

    [Fact]
    public async Task Turned_on_each_game_starts_from_a_seed_of_its_own_that_a_rewind_keeps()
    {
        var project = await Project.CreateAsync(_root, "Game");
        project.SetNewRandomEachGame(true);
        Assert.True((await Project.LoadAsync(project.RootPath)).NewRandomEachGame);
        Rand.Seed(7);

        var rolls = Enumerable.Range(0, 5).Select(_ =>
        {
            var game = new Game(new Scene(), new InputState());
            game.UseProjectSettings(project);
            return FirstRoll(game);
        }).ToList();

        Assert.True(rolls.Distinct().Count() > 1);

        var copy = new Game(new Scene(), new InputState());
        copy.UseProjectSettings(project);
        var snapshot = copy.Random.Copy();
        var expected = new[] { snapshot.Next(1000), snapshot.Next(1000) };
        Assert.Equal(expected, new[] { copy.Random.Next(1000), copy.Random.Next(1000) });

        project.SetNewRandomEachGame(false);
        Assert.DoesNotContain("newRandomEachGame", await File.ReadAllTextAsync(project.ManifestPath));
    }
}
