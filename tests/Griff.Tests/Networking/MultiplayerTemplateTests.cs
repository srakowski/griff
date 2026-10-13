using System.Numerics;
using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Networking;

/// <summary>The Multiplayer template, played by several players on a pretend network.</summary>
public sealed class MultiplayerTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-mp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<(Project Project, ComponentRegistry Registry)> Create()
    {
        var project = await Project.CreateAsync(_root, "Arena", ProjectTemplate.Multiplayer);
        return (project, ComponentRegistry.CreateDefault());
    }

    private static Action<Game> Loaders(Project project, ComponentRegistry registry) => game =>
    {
        game.PrefabLoader = new PrefabLibrary(project, registry).Instantiate;
        game.SceneLoader = name => GameLoader.LoadScene(project, registry, name);
        game.Components = registry;
        game.Title = project.Name;
    };

    [Fact]
    public async Task Three_players_fight_in_the_arena_each_with_their_own_color_spawn_and_name()
    {
        var (project, registry) = await Create();
        var world = new NetWorld(() => GameLoader.LoadScene(project, registry, "Arena"), players: 2, setUp: Loaders(project, registry));
        var players = world.Host.Net.Players;
        Assert.Equal(3, players.Count);
        Assert.Equal(3, players.Select(p => p.Color).Distinct().Count());

        var avatars = players.Select(p => p.Avatar!).ToList();
        Assert.All(avatars, a => Assert.NotNull(a));
        Assert.Equal(3, avatars.Select(a => a.Transform.Position).Distinct().Count());
        foreach (var (player, avatar) in players.Zip(avatars))
        {
            Assert.Equal(player.Color with { A = 1 }, avatar.GetComponent<Sprite>()!.Tint with { A = 1 });
            Assert.Equal(player.Name, avatar.Children.Single(c => c.Name == "Name").GetComponent<Text>()!.Value);
        }

        // On a player's machine, the same: everyone's name over their head, and the camera on their own character.
        var guest = world.Clients[1];
        var seen = guest.Scene.Entities.Where(e => e.Name == "Player").ToList();
        Assert.Equal(3, seen.Count);
        Assert.Equal(["Guest1", "Guest2", "Hosty"], seen.Select(e => e.Children.Single(c => c.Name == "Name").GetComponent<Text>()!.Value).Order());
        world.Run(1);
        var camera = guest.Scene.Entities.First(e => e.GetComponent<Camera>() is not null);
        var mine = guest.Net.LocalPlayer!.Avatar!;
        Assert.Equal(mine, camera.GetComponent<Follow>()!.TargetEntity);
        Assert.Contains("Scores", guest.Scene.Entities.First(e => e.Name == "Scores").GetComponent<Text>()!.Value);
        Assert.Contains("Guest2 (you)", guest.Scene.Entities.First(e => e.Name == "Scores").GetComponent<Text>()!.Value);

        // Slimes come out for whoever is nearest; shots and hits happen on the host and everyone sees them.
        world.Run(6);
        Assert.NotEmpty(world.Host.FindAll("Slime"));
        Assert.Equal(world.Host.FindAll("Slime").Count, guest.FindAll("Slime").Count);
    }

    [Fact]
    public async Task Hosting_from_the_menu_takes_everyone_into_the_arena()
    {
        var (project, registry) = await Create();
        var world = new NetWorld(() => GameLoader.LoadScene(project, registry, "Main"), setUp: Loaders(project, registry), sceneName: "Main");
        Assert.Equal("Main", world.Client.SceneName);

        // Already hosting (as Play with Players does): Host Game just goes to the arena, and the player comes too.
        var host = world.Host;
        var button = host.Find("Host Game")!;
        var keys = NetWorld.Keys(host);
        keys.MouseWorld = new Vector2(button.Transform.Position.X, button.Transform.Position.Y);
        keys.SetMouseButton(MouseButton.Left, true);
        world.Run(0.05);
        keys.SetMouseButton(MouseButton.Left, false);
        world.Run(1.5);
        Assert.Equal("Arena", host.SceneName);
        Assert.Equal("Arena", world.Client.SceneName);
        Assert.NotNull(world.Client.Net.LocalPlayer!.Avatar);
    }

    [Fact]
    public async Task Practice_plays_the_arena_alone()
    {
        var (project, registry) = await Create();
        var input = new InputState();
        var game = new Game(GameLoader.LoadScene(project, registry, "Arena"), input) { SceneName = "Arena" };
        Loaders(project, registry)(game);
        for (var i = 0; i < 60; i++)
        {
            game.Tick(1 / 60.0);
            input.EndFrame();
        }

        Assert.False(game.Net.IsOnline);
        Assert.Single(game.FindAll("Player"));
        Assert.Equal("You", game.Find("Player")!.Children.Single(c => c.Name == "Name").GetComponent<Text>()!.Value);
        Assert.Contains("Practice", game.Find("Scores")!.GetComponent<Text>()!.Value);
    }

    // ----- The other templates, as network games: add a Multiplayer and they're ready -----

    private async Task<(Project Project, ComponentRegistry Registry)> Create(ProjectTemplate template)
    {
        var project = await Project.CreateAsync(_root, template.ToString(), template);
        return (project, ComponentRegistry.CreateDefault());
    }

    private static Func<Scene> WithMultiplayer(Project project, ComponentRegistry registry, Action<Multiplayer>? set = null) => () =>
    {
        var scene = GameLoader.LoadScene(project, registry, Project.DefaultSceneName);
        var entity = new Entity("Multiplayer");
        var settings = entity.AddComponent<Multiplayer>();
        set?.Invoke(settings);
        scene.Add(entity);
        return scene;
    };

    [Fact]
    public async Task The_strategy_template_plays_one_against_one()
    {
        var (project, registry) = await Create(ProjectTemplate.Strategy);
        var world = new NetWorld(WithMultiplayer(project, registry, m => (m.Player, m.Teams) = ("", "Player, Enemy")),
            setUp: Loaders(project, registry), sceneName: Project.DefaultSceneName);
        var client = world.Client;
        Assert.Equal("Enemy", client.Net.LocalPlayer!.Team);
        // Its own scene has no Multiplayer (as when a player's copy of the game differs): it goes by the host's.
        Assert.Null(client.Net.Settings);
        Assert.Equal("Enemy", client.Find<Commander>()!.Team);

        // Red's workers, sent to their nearest gold mine by the player on red's side.
        var workers = client.FindAll<Worker>().Select(w => w.Entity!).Where(e => e.GetComponent<Unit>()!.Team == "Enemy").ToList();
        Assert.NotEmpty(workers);
        var at = new Vector2(workers[0].Transform.Position.X, workers[0].Transform.Position.Y);
        var mine = client.FindAll("Gold Mine").OrderBy(m => Vector2.Distance(at, new Vector2(m.Transform.Position.X, m.Transform.Position.Y))).First();
        client.Select(workers);
        world.Run(0.1);
        var keys = NetWorld.Keys(client);
        keys.MouseWorld = new Vector2(mine.Transform.Position.X, mine.Transform.Position.Y);
        keys.SetMouseButton(MouseButton.Right, true);
        world.Run(0.05);
        keys.SetMouseButton(MouseButton.Right, false);
        world.Run(1);
        var hostWorker = world.Host.Scene.Find(workers[0].Id)!.GetComponent<Worker>()!;
        Assert.Equal(mine.Id, hostWorker.Source?.Id);

        // The host's commander is blue's, and its selection is its own.
        Assert.Equal("Player", world.Host.Find<Commander>()!.Team);
        Assert.Empty(world.Host.Selected);
    }

    [Fact]
    public async Task The_sandbox_template_is_one_world_for_everyone_to_dig_in()
    {
        var (project, registry) = await Create(ProjectTemplate.Sandbox);
        var world = new NetWorld(WithMultiplayer(project, registry), setUp: Loaders(project, registry), sceneName: Project.DefaultSceneName);
        world.Run(1.5);
        var client = world.Client;
        var mine = client.Net.LocalPlayer!.Avatar!;
        var hostMap = world.Host.Find<Blocks>()!.Entity!.GetComponent<Tilemap>()!;
        var clientMap = client.Find<Blocks>()!.Entity!.GetComponent<Tilemap>()!;
        Assert.Equal(hostMap.Tiles, clientMap.Tiles);

        // The block under the player's feet.
        var feet = new Vector2(mine.Transform.Position.X, mine.Transform.Position.Y - 1.2f);
        Assert.True(Matrix4x4.Invert(clientMap.Entity!.Transform.WorldMatrix, out var toLocal));
        var cell = clientMap.CellAt(Vector2.Transform(feet, toLocal));
        Assert.True(hostMap.GetTile(cell.X, cell.Y) >= 0, "standing on a block");
        var keys = NetWorld.Keys(client);
        keys.MouseWorld = feet;
        keys.SetMouseButton(MouseButton.Left, true);
        world.Run(2.5);
        keys.SetMouseButton(MouseButton.Left, false);
        world.Run(0.5);
        Assert.Equal(-1, hostMap.GetTile(cell.X, cell.Y));
        Assert.Equal(-1, clientMap.GetTile(cell.X, cell.Y));
        Assert.True(mine.GetComponent<Inventory>()!.Contents.Count > 0, "what was dug is in their inventory");
    }

    [Fact]
    public async Task The_shooter_template_gives_each_player_their_own_eyes()
    {
        var (project, registry) = await Create(ProjectTemplate.Shooter);
        var world = new NetWorld(WithMultiplayer(project, registry), setUp: Loaders(project, registry), sceneName: Project.DefaultSceneName);
        var client = world.Client;
        var mine = client.Net.LocalPlayer!.Avatar!;
        Assert.Equal(mine, client.FirstPersonViewer);
        Assert.Equal(world.Host.Net.LocalPlayer!.Avatar, world.Host.FirstPersonViewer);

        // Turning happens on the player's own machine, and the host sees which way they face.
        NetWorld.Keys(client).SetKey(Key.Left, true);
        world.Run(0.5);
        NetWorld.Keys(client).SetKey(Key.Left, false);
        world.Run(0.3);
        Assert.Equal(mine.Transform.Rotation.Z, world.Host.Scene.Find(mine.Id)!.Transform.Rotation.Z, 1);
        Assert.NotEqual(0, mine.Transform.Rotation.Z, 1);
    }
}
