using System.Numerics;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Projects;

// The Match-3 template: a menu of levels, each opening once the one before is won, and levels where gems swapped into
// lines of three clear. All of it no-code components: a Match Board with its moves and goals, counters that show them,
// and panels that open on the board's "Level Complete" and "Out of Moves".
public static partial class ProjectTemplates
{
    private static readonly Color MatchPink = Color.FromBytes(0xF0, 0x5A, 0x9C);
    private static readonly Color MatchViolet = Color.FromBytes(0x7A, 0x5C, 0xE8);
    private static readonly Color MatchGray = Color.FromBytes(0x3E, 0x38, 0x56);
    private static readonly Color MatchLight = Color.FromBytes(0xC8, 0xC2, 0xDC);
    private static readonly Color MatchDim = Color.FromBytes(0x86, 0x7E, 0x9E);
    private static readonly Color MatchSky = Color.FromBytes(0x1E, 0x1A, 0x30);

    /// <summary>The names of the gem kinds, by tile number, as the levels' goals call them.</summary>
    public static IReadOnlyList<string> GemNames { get; } = ["Rubies", "Topazes", "Citrines", "Emeralds", "Sapphires", "Amethysts"];

    /// <summary>
    /// The levels, in order: how many moves each has, its goals (a score to reach, gems to collect as kind:count), which
    /// tiles are gems, and its layout, eight rows of eight: # a stone block, . a gem.
    /// </summary>
    public static IReadOnlyList<(string Name, int Moves, int TargetScore, string? Collect, string Gems, string[] Layout)> MatchLevels { get; } =
    [
        ("First Sparkle", 20, 1200, null, "0-4",
        [
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
        ]),
        ("Ruby Hunt", 22, 0, "0:20", "0-5",
        [
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
        ]),
        ("Stone Garden", 20, 1500, "3:20", "0-4",
        [
            "#......#",
            "........",
            "........",
            "...##...",
            "...##...",
            "........",
            "........",
            "#......#",
        ]),
        ("Grand Hoard", 30, 1800, "1:12, 4:12", "0-5",
        [
            "........",
            ".#....#.",
            "........",
            "..#..#..",
            "........",
            "........",
            ".#....#.",
            "........",
        ]),
    ];

    /// <summary>The scene each level is saved as, e.g. "Level1".</summary>
    public static string MatchLevelScene(int index) => $"Level{index + 1}";

    // Winning a level saves this, which opens the next one's button on the menu.
    private static string CompleteKey(int index) => $"complete-{MatchLevelScene(index).ToLowerInvariant()}";

    private static Dictionary<string, Scene> MatchLevelScenes(ProjectTemplate template) =>
        Enumerable.Range(0, MatchLevels.Count).ToDictionary(i => $"Scenes/{MatchLevelScene(i)}.scene", i => WithSounds(template, MatchLevel(i)));

    // The menu: a title, a button for each level (each but the first opening once the one before is won), and Quit.
    private static Scene MatchMenu()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 6;
        view.Background = MatchSky;
        view.Transition = SceneTransition.Wipe;
        scene.Add(camera);

        scene.Add(Label("Title", new Vector2(0, 4.4f), "MATCH-3", 1.6f, MatchPink));
        scene.Add(Label("Subtitle", new Vector2(0, 3.3f), "Swap gems to line up three of a kind", 0.5f, MatchLight));

        for (var i = 0; i < MatchLevels.Count; i++)
        {
            var button = MenuButton($"Level {i + 1}", new Vector2(0, 1.9f - i * 1.3f), Scenes.ButtonAction.LoadScene, MatchLevelScene(i),
                i == 0 ? Key.Enter : Key.None, i == 0 ? MatchViolet : MatchGray);
            var size = new Vector2(6, 1.1f);
            button.GetComponent<Sprite>()!.Size = size;
            button.GetComponent<Scenes.Button>()!.Size = size;
            button.GetComponent<Text>()!.Value = $"{i + 1}. {MatchLevels[i].Name}";
            if (i > 0)
                button.GetComponent<Scenes.Button>()!.NeedsSaved = CompleteKey(i - 1);
            scene.Add(button);
        }

        scene.Add(MenuButton("Quit", new Vector2(0, 1.9f - MatchLevels.Count * 1.3f - 0.3f), Scenes.ButtonAction.Quit, null, Key.Escape, MatchGray));

        var hint = Label("Hint", Vector2.Zero, "Enter to play  ·  Arrows to choose  ·  Win a level to open the next", 0.4f, MatchDim);
        var anchor = hint.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 0.6f);
        scene.Add(hint);
        return scene;
    }

    // One level: the board with its moves and goals, what's on screen, and the panels for winning and running out of moves.
    private static Scene MatchLevel(int index)
    {
        var (name, moves, target, collect, gems, layout) = MatchLevels[index];
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 5.6f;
        view.Background = MatchSky;
        view.Transition = SceneTransition.Wipe;
        scene.Add(camera);

        // The board sits right of the middle, leaving room on the left for the goals. Stones are painted where the layout
        // has them; the gems are dealt now so the scene shows them, and dealt afresh each time the level starts.
        var (columns, rows) = (layout[0].Length, layout.Length);
        var entity = new Entity("Board");
        entity.Transform.Position = new Vector3(2.4f - columns / 2f, rows / 2f, 0);
        var map = entity.AddComponent<Tilemap>();
        map.Image = TemplateArt.GemTiles;
        map.Columns = TemplateArt.GemColumns;
        map.Solid = false;
        map.Tiles = layout.Select(row => new string(row.Select(c => c == '#' ? Tilemap.CharOf(TemplateArt.GemStoneTile) : Tilemap.Empty).ToArray())).ToList();
        var board = entity.AddComponent<MatchBoard>();
        (board.Columns, board.Rows, board.Gems) = (columns, rows, gems);
        (board.Moves, board.TargetScore, board.Collect) = (moves, target, collect);
        board.Background = Color.FromBytes(0x0C, 0x0A, 0x18, 0xC8);
        board.SwapSound = TemplateSounds.Select;
        board.MatchSound = TemplateSounds.Coin;
        board.NoMatchSound = TemplateSounds.Dig;
        board.ShuffleSound = TemplateSounds.Dash;
        board.WinSound = TemplateSounds.PowerUp;
        board.OutOfMovesSound = TemplateSounds.Hurt;
        board.DealInto(map, new Random(index + 1));
        scene.Add(entity);

        AddMatchHud(scene, index, name, board);
        scene.Add(MatchCompletePanel(index));
        scene.Add(OutOfMovesPanel());

        // Winning saves that it's won, which opens the next level's button on the menu.
        var progress = new Entity("Progress");
        var won = progress.AddComponent<Counter>();
        won.Message = "Level Complete";
        won.Format = null;
        won.SaveBestAs = CompleteKey(index);
        scene.Add(progress);

        AddPauseMenu(scene, menuScene: "Main");
        return scene;
    }

    // Down the left: the level's number and name, the score (out of the score to reach), the moves left, and a gem with a
    // count for each kind to collect. The keys at the bottom.
    private static void AddMatchHud(Scene scene, int index, string name, MatchBoard board)
    {
        var top = -0.7f;
        Entity Line(string entityName, string text, float size, Color color)
        {
            var label = Label(entityName, Vector2.Zero, text, size, color);
            label.GetComponent<Text>()!.Align = TextAlign.Left;
            var corner = label.AddComponent<ScreenAnchor>();
            corner.Offset = new Vector2(0.6f, top);
            top -= size + 0.35f;
            scene.Add(label);
            return label;
        }

        Line("Level Name", $"{index + 1}. {name}", 0.6f, MatchPink);
        top -= 0.2f;

        var score = Line("Score", board.TargetScore > 0 ? $"Score: 0 / {board.TargetScore}" : "Score: 0", 0.5f, MatchLight).AddComponent<Counter>();
        score.Message = null;
        score.CountScore = true;
        score.Format = board.TargetScore > 0 ? "Score: {0} / {2}" : "Score: {0}";

        var moves = Line("Moves", $"Moves: {board.Moves}", 0.5f, MatchLight).AddComponent<Counter>();
        moves.Message = null;
        moves.CountMoves = true;
        moves.Format = "Moves: {3}";

        var collect = MatchBoard.ParseCounts(board.Collect);
        if (collect.Count > 0)
        {
            top -= 0.2f;
            Line("Collect", "Collect", 0.45f, MatchDim);
        }

        foreach (var (kind, count) in collect.OrderBy(c => c.Key))
        {
            var gemName = kind < GemNames.Count ? GemNames[kind] : $"Gem {kind}";
            var row = Line(gemName, $"0 / {count}", 0.5f, MatchLight);
            row.GetComponent<ScreenAnchor>()!.Offset += new Vector2(0.9f, 0);
            var counter = row.AddComponent<Counter>();
            counter.Message = null;
            counter.CountGem = kind;
            counter.Format = "{0} / {2}";

            var icon = new Entity($"{gemName} Icon");
            var sprite = icon.AddComponent<Sprite>();
            sprite.Image = TemplateArt.GemTiles;
            sprite.Columns = TemplateArt.GemColumns;
            sprite.Frame = kind;
            sprite.Size = new Vector2(0.65f, 0.65f);
            sprite.Order = 100;
            var corner = icon.AddComponent<ScreenAnchor>();
            corner.Offset = row.GetComponent<ScreenAnchor>()!.Offset - new Vector2(0.55f, 0.05f);
            scene.Add(icon);
        }

        // "{Jump}" shows Space on the keyboard and A on a gamepad.
        var hint = Label("Hint", Vector2.Zero, "Drag a gem onto its neighbor  ·  Arrows and {Jump} with keys  ·  {Pause} for the menu", 0.4f, MatchDim);
        var bottom = hint.AddComponent<ScreenAnchor>();
        bottom.Anchor = Anchor.Bottom;
        bottom.Offset = new Vector2(0, 0.5f);
        scene.Add(hint);
    }

    // Opens on "Level Complete": the score, and on to the next level (or back to the menu after the last), again, or the menu.
    private static Entity MatchCompletePanel(int index)
    {
        var last = index == MatchLevels.Count - 1;
        var panel = new Entity("Complete");
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var complete = panel.AddComponent<Panel>();
        complete.OpenMessage = "Level Complete";
        complete.CloseOnBack = false;
        var backdrop = panel.AddComponent<Sprite>();
        backdrop.Size = new Vector2(10, last ? 5.2f : 6.4f);
        backdrop.Tint = new Color(0.08f, 0.07f, 0.12f, 0.94f);
        backdrop.Order = 400;

        var heading = Label("Heading", new Vector2(0, last ? 1.6f : 2.2f), last ? "All Complete!" : "Level Complete!", 1.2f, Color.FromBytes(0x9C, 0xE0, 0x8A));
        heading.GetComponent<Text>()!.Order = 401;
        WithChild(panel, heading);

        var detail = Label("Detail", new Vector2(0, last ? 0.4f : 1), "", 0.5f, MatchLight);
        detail.GetComponent<Text>()!.Order = 401;
        var score = detail.AddComponent<Counter>();
        score.Message = null;
        score.CountScore = true;
        score.Format = last ? "Score: {0}. That's every level: make some more!" : "Score: {0}";
        score.Pop = 1;
        WithChild(panel, detail);

        var next = last
            ? MenuButton("Menu", new Vector2(-2.2f, -1.2f), Scenes.ButtonAction.LoadScene, "Main", Key.Enter, MatchViolet, order: 410)
            : MenuButton("Next", new Vector2(-2.2f, -0.6f), Scenes.ButtonAction.LoadScene, MatchLevelScene(index + 1), Key.Enter, MatchViolet, order: 410);
        WithChild(panel, next);
        WithChild(panel, MenuButton("Again", new Vector2(2.2f, last ? -1.2f : -0.6f), Scenes.ButtonAction.RestartScene, null, Key.R, MatchGray, order: 410));
        if (!last)
            WithChild(panel, MenuButton("Menu", new Vector2(0, -1.9f), Scenes.ButtonAction.LoadScene, "Main", Key.Escape, MatchGray, order: 410));
        return panel;
    }

    // Opens on "Out of Moves": how close the score came, then again or the menu.
    private static Entity OutOfMovesPanel()
    {
        var panel = new Entity("Out of Moves");
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var lost = panel.AddComponent<Panel>();
        lost.OpenMessage = "Out of Moves";
        lost.CloseOnBack = false;
        var backdrop = panel.AddComponent<Sprite>();
        backdrop.Size = new Vector2(10, 5.2f);
        backdrop.Tint = new Color(0.08f, 0.07f, 0.12f, 0.94f);
        backdrop.Order = 400;

        var heading = Label("Heading", new Vector2(0, 1.6f), "Out of Moves", 1.2f, MatchPink);
        heading.GetComponent<Text>()!.Order = 401;
        WithChild(panel, heading);

        var detail = Label("Detail", new Vector2(0, 0.4f), "", 0.5f, MatchLight);
        detail.GetComponent<Text>()!.Order = 401;
        var score = detail.AddComponent<Counter>();
        score.Message = null;
        score.CountScore = true;
        score.Format = "Score: {0}. Try again?";
        score.Pop = 1;
        WithChild(panel, detail);

        WithChild(panel, MenuButton("Again", new Vector2(-2.2f, -1.2f), Scenes.ButtonAction.RestartScene, null, Key.Enter, MatchViolet, order: 410));
        WithChild(panel, MenuButton("Menu", new Vector2(2.2f, -1.2f), Scenes.ButtonAction.LoadScene, "Main", Key.Escape, MatchGray, order: 410));
        return panel;
    }
}
