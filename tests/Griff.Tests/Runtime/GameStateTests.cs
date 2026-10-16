using Griff.Scenes;
using Griff.Runtime;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>What a running game keeps besides its scene goes into its rewind snapshots, every field of it.</summary>
public class GameStateTests
{
    // The parts of a game whose fields are its state: the game, its clock and numbers, and every system Griff has.
    private static IEnumerable<Type> Parts() =>
        new[] { typeof(Game), typeof(Time), typeof(GameRandom), typeof(Physics) }
            .Concat(typeof(Game).Assembly.GetTypes().Where(t => typeof(GameSystem).IsAssignableFrom(t) && !t.IsAbstract));

    [Fact]
    public void Every_field_of_a_system_is_in_the_snapshot_or_marked_as_not_its_state()
    {
        var unkept = (from part in Parts()
                      from field in KeptFields.OfPart(part).Fields
                      let why = StatePlan.WhyNotKeepable(field.FieldType)
                      where why is not null
                      select $"{field.DeclaringType!.Name}.{field.Name}: {why}").Distinct().ToList();

        Assert.True(unkept.Count == 0, string.Join("\n", unkept));
    }

    [Fact]
    public void Every_field_of_a_component_is_in_its_scene_file_or_the_snapshot_or_marked_as_not_its_state()
    {
        var unkept = (from descriptor in ComponentRegistry.CreateDefault().All
                      from field in KeptFields.OfComponent(descriptor.Type).Fields
                      let why = StatePlan.WhyNotKeepable(field.FieldType)
                      where why is not null
                      select $"{field.DeclaringType!.Name}.{field.Name}: {why}").Distinct().ToList();

        Assert.True(unkept.Count == 0, string.Join("\n", unkept));
    }

    [Fact]
    public void A_games_random_numbers_are_its_own_even_with_another_game_on_the_same_thread()
    {
        static List<float> Draws(Game game, int steps)
        {
            var drawn = new List<float>();
            game.AddSystem(new Drawing(drawn));
            for (var i = 0; i < steps; i++)
                game.Tick(1 / 60.0);
            return drawn;
        }

        // Alone, then side by side with another game started from the same seed, a step of each in turn and numbers drawn
        // outside them between.
        Rand.Seed(21);
        var alone = Draws(new Game(new Scene(), new InputState()), 20);

        Rand.Seed(21);
        var (first, second) = (new Game(new Scene(), new InputState()), new Game(new Scene(), new InputState()));
        var (a, b) = (new List<float>(), new List<float>());
        first.AddSystem(new Drawing(a));
        second.AddSystem(new Drawing(b));
        for (var i = 0; i < 20; i++)
        {
            first.Tick(1 / 60.0);
            second.Tick(1 / 60.0);
            _ = Rand.Value; // numbers drawn outside either game
        }

        Assert.Equal(alone, a);
        Assert.Equal(alone, b);
    }

    [Fact]
    public void A_games_random_numbers_go_back_with_a_rewind()
    {
        Rand.Seed(4);
        var game = new Game(new Scene(), new InputState());
        var drawn = new List<float>();
        game.AddSystem(new Drawing(drawn));
        var timeline = new Timeline(new SceneSerializer(ComponentRegistry.CreateDefault()), framesPerSnapshot: 1000);
        game.Tick(1 / 60.0);
        timeline.Record(game, force: true);
        for (var i = 0; i < 5; i++)
            game.Tick(1 / 60.0);
        var after = drawn[1..];

        timeline.Restore(0, game);
        drawn.Clear();
        for (var i = 0; i < 5; i++)
            game.Tick(1 / 60.0);

        Assert.Equal(after, drawn);
    }

    [Fact]
    public void The_game_s_random_numbers_are_those_a_seeded_Random_gives()
    {
        foreach (var seed in new[] { 0, 1, 42, -7, int.MaxValue, int.MinValue })
        {
            var (ours, theirs) = (new GameRandom(seed), new Random(seed));
            for (var i = 0; i < 2000; i++)
            {
                Assert.Equal(theirs.Next(), ours.Next());
                Assert.Equal(theirs.Next(37), ours.Next(37));
                Assert.Equal(theirs.Next(-5, 1000), ours.Next(-5, 1000));
                Assert.Equal(theirs.Next(int.MinValue, int.MaxValue), ours.Next(int.MinValue, int.MaxValue));
                Assert.Equal(theirs.NextSingle(), ours.NextSingle());
                Assert.Equal(theirs.NextDouble(), ours.NextDouble());
            }

            var copy = ours.Copy();
            Assert.Equal(ours.Next(), copy.Next());
        }
    }

    // Draws a number from Rand each step, into a list (kept as it is: the test's, not the game's).
    private sealed class Drawing(List<float> drawn) : GameSystem
    {
        [NotState("the test's list")]
        private readonly List<float> _drawn = drawn;

        protected override void Update() => _drawn.Add(Rand.Value);
    }
}
