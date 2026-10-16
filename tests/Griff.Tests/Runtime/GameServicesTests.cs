using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

// A component asks the game for what it needs by type (an IGameService), and the system that owns that part provides it.
public class GameServicesTests
{
    [Fact]
    public void Every_service_a_component_can_ask_for_is_there_in_a_running_game()
    {
        var game = new Game(new Scene(), new InputState());
        var get = typeof(IGameServices).GetMethod(nameof(IGameServices.Get))!;
        var services = ModuleAssemblies.All.Prepend(typeof(IGameService).Assembly).SelectMany(a => a.GetTypes())
            .Where(t => t.IsInterface && t != typeof(IGameService) && typeof(IGameService).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(services);
        foreach (var service in services)
        {
            var provider = get.MakeGenericMethod(service).Invoke(game.Services, null);
            Assert.True(provider is GameSystem system && game.Systems.Contains(system), $"No system of the game provides {service.Name}.");
        }
    }

    [Fact]
    public void The_scene_played_reaches_the_games_services_and_the_one_left_no_longer_does()
    {
        var first = new Scene();
        var next = new Scene();
        var game = new Game(first, new InputState()) { SceneLoader = _ => next };
        Assert.Same(game.Services, first.Services);

        game.LoadScene("Next");
        game.Advance(1 / 60f);

        Assert.Same(next, game.Scene);
        Assert.Same(game.Services, next.Services);
        Assert.Null(first.Services);
    }

    // A script's action takes a service the same way Griff's components do, and a script's own system can provide one.
    [Fact]
    public void A_script_reaches_the_same_services_a_built_in_component_does()
    {
        var scene = new Scene();
        var pause = new Entity("Pause");
        var panel = pause.AddComponent<Panel>();
        pause.IsEnabled = false;
        scene.Add(pause);
        var host = new Entity("Host");
        var script = host.AddComponent<PausesAndScores>();
        script.Panel = panel;
        scene.Add(host);
        var game = new Game(scene, new InputState());
        var board = game.AddSystem(new ScoreBoard());
        game.Advance(1 / 60f);

        Assert.Equal(1, game.Do(host, "Pause"));
        Assert.Contains(panel, game.OpenPanels);
        Assert.Same(game.Services.Get<IPanels>(), script.Panels);

        Assert.Equal(1, game.Do(host, "Score", 3));
        Assert.Same(board, game.Services.Get<IScoreBoard>());
        Assert.Equal(3, board.Points);
    }

    // No-code actions reach the panels, the dialogue and the flags through their systems' services: a trigger opens a
    // closed panel on a message, and buttons' shortcuts set a flag and start a dialogue.
    [Fact]
    public void No_code_actions_open_a_panel_set_a_flag_and_start_a_dialogue()
    {
        var scene = new Scene();
        var paused = new Entity("Paused");
        paused.AddComponent<Panel>();
        paused.IsEnabled = false;
        scene.Add(paused);
        var opener = new Entity("Opener");
        var trigger = opener.AddComponent<Trigger>();
        (trigger.Message, trigger.Do, trigger.Targets) = ("Pause", "Open", "Paused");
        scene.Add(opener);
        var king = new Entity("King");
        var dialogue = king.AddComponent<Dialogue>();
        (dialogue.Lines, dialogue.PauseGame) = (["Hello there!"], false);
        scene.Add(king);
        var flagger = new Entity("Flag Key");
        flagger.AddComponent<Trigger>();
        var flag = flagger.AddComponent<Button>();
        (flag.Action, flag.Do, flag.With, flag.Shortcut) = (ButtonAction.Do, "Set Flag", "Seen", Key.D3);
        scene.Add(flagger);
        var talker = new Entity("Talk Key");
        var talk = talker.AddComponent<Button>();
        (talk.Action, talk.Do, talk.Targets, talk.Shortcut) = (ButtonAction.Do, "Start", "King", Key.D2);
        scene.Add(talker);

        var input = new InputState();
        var game = new Game(scene, input);
        game.Advance(1 / 60f);
        input.SetKey(Key.D3, true);
        game.Advance(1 / 60f);
        input.SetKey(Key.D3, false);
        Assert.True(game.HasFlag("Seen"));

        input.SetKey(Key.D2, true);
        game.Advance(1 / 60f);
        input.SetKey(Key.D2, false);
        Assert.Same(dialogue, game.Talking);

        game.Send("Pause");
        Assert.True(paused.IsEnabled);
        Assert.Contains(paused.GetComponent<Panel>()!, game.OpenPanels);
    }

    [Fact]
    public void An_action_that_needs_the_game_is_done_only_while_one_runs()
    {
        var panel = new Entity("Pause").AddComponent<Panel>();
        var open = ComponentActions.Find(panel, "Open")!;

        Assert.True(open.TakesGame);
        var error = Assert.Throws<InvalidOperationException>(() => open.Invoke(panel, null, (string?)null));
        Assert.Contains("only be done while the game runs", error.Message, StringComparison.Ordinal);
    }

    public interface IScoreBoard : IGameService
    {
        void Add(int points);
    }

    private sealed class ScoreBoard : GameSystem, IScoreBoard
    {
        public int Points { get; private set; }

        public void Add(int points) => Points += points;
    }

    private sealed class PausesAndScores : Behavior
    {
        public Panel? Panel { get; set; }

        public IPanels? Panels { get; private set; }

        [Action]
        public void Pause(IPanels panels)
        {
            Panels = panels;
            panels.Open(Panel!);
        }

        public int Scored { get; private set; }

        [Action]
        public void Score(IScoreBoard board, int points)
        {
            Scored += points;
            board.Add(points);
        }
    }
}
