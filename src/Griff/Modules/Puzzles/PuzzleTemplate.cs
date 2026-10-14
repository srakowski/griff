using System.Globalization;
using System.Numerics;
using Griff.Audio;
using Griff.Input;
using Griff.Projects;
using Griff.Scenes;
using static Griff.Projects.ProjectTemplates;

namespace Griff.Modules.Puzzles;

/// <summary>
/// The Puzzle template: Sokoban. A menu of levels, each opening once the one before is solved, and levels where a worker
/// pushes crates onto the marks on the floor. All of it no-code components: Grid Movement, Pushable, Goal, Move History,
/// and a panel that opens on the goals' "Solved".
/// </summary>
public sealed class PuzzleTemplate : ProjectTemplate
{
    public override string Description => "Push crates onto the marks on the floor in four Sokoban levels, each opening once the one before is solved. Arrow keys or WASD to walk and push, Z to take a move back, R to start the level over.";

    public override int Order => 80;

    protected override MusicRecipe Music => MusicRecipe.ForMood(MusicMood.Chill, seed: 9) with { Volume = 0.5f };

    protected override Scene BuildStartScene() => PuzzleMenu();

    protected override Dictionary<string, Scene> BuildMoreScenes() => PuzzleLevelScenes();

    public override IReadOnlyDictionary<string, byte[]> Images() => PuzzlesArt.Files();

    private static readonly Color PuzzleOrange = Color.FromBytes(0xF0, 0x8C, 0x2E);
    private static readonly Color PuzzleBlue = Color.FromBytes(0x4C, 0x9A, 0xFF);
    private static readonly Color PuzzleGray = Color.FromBytes(0x3A, 0x3F, 0x4B);
    private static readonly Color PuzzleLight = Color.FromBytes(0xB4, 0xB9, 0xC4);
    private static readonly Color PuzzleDim = Color.FromBytes(0x7D, 0x83, 0x90);

    /// <summary>
    /// The levels, in order, as Sokoban players write them: # wall, @ the worker, $ a crate, . a mark, * a crate on a mark,
    /// + the worker on a mark, and a space for floor. Spaces outside the walls are left empty.
    /// </summary>
    public static IReadOnlyList<(string Name, string[] Rows)> PuzzleLevels { get; } =
    [
        ("First Push",
        [
            "#######",
            "#     #",
            "# @$ .#",
            "#     #",
            "# $  .#",
            "#     #",
            "#######",
        ]),
        ("Tight Corner",
        [
            "  ####",
            "###  #",
            "# $  #",
            "# .$.#",
            "## @ #",
            " #####",
        ]),
        ("Crossroads",
        [
            " #######",
            " #  .  #",
            "## $#$ ##",
            "#  .@.  #",
            "## $#$ ##",
            " #  .  #",
            " #######",
        ]),
        ("Storeroom",
        [
            "########",
            "#..    #",
            "#..$ $ #",
            "###$## #",
            "  # $  #",
            "  #  @ #",
            "  ######",
        ]),
    ];

    /// <summary>The scene each level is saved as, e.g. "Level1".</summary>
    public static string PuzzleLevelScene(int index) => $"Level{index + 1}";

    // Solving a level saves this, which opens the next one's button on the menu.
    private static string SolvedKey(int index) => $"solved-{PuzzleLevelScene(index).ToLowerInvariant()}";

    private static Dictionary<string, Scene> PuzzleLevelScenes() =>
        Enumerable.Range(0, PuzzleLevels.Count).ToDictionary(i => $"Scenes/{PuzzleLevelScene(i)}.scene", i => PuzzleLevel(i));

    // The menu: a title, a button for each level (each but the first opening once the one before is solved), and Quit.
    private static Scene PuzzleMenu()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 6;
        view.Background = Color.FromBytes(0x22, 0x1E, 0x1C);
        view.Transition = SceneTransition.Wipe;
        scene.Add(camera);

        scene.Add(Label("Title", new Vector2(0, 4.4f), "PUZZLE", 1.6f, PuzzleOrange));
        scene.Add(Label("Subtitle", new Vector2(0, 3.3f), "Push every crate onto a mark", 0.5f, PuzzleLight));

        for (var i = 0; i < PuzzleLevels.Count; i++)
        {
            var button = MenuButton($"Level {i + 1}", new Vector2(0, 1.9f - i * 1.3f), Scenes.ButtonAction.LoadScene, PuzzleLevelScene(i),
                i == 0 ? Key.Enter : Key.None, i == 0 ? PuzzleBlue : PuzzleGray);
            var size = new Vector2(6, 1.1f);
            button.GetComponent<Sprite>()!.Size = size;
            button.GetComponent<Scenes.Button>()!.Size = size;
            button.GetComponent<Text>()!.Value = $"{i + 1}. {PuzzleLevels[i].Name}";
            if (i > 0)
                button.GetComponent<Scenes.Button>()!.NeedsSaved = SolvedKey(i - 1);
            scene.Add(button);
        }

        scene.Add(MenuButton("Quit", new Vector2(0, 1.9f - PuzzleLevels.Count * 1.3f - 0.3f), Scenes.ButtonAction.Quit, null, Key.Escape, PuzzleGray));

        var hint = Label("Hint", Vector2.Zero, "Enter to play  ·  Arrows to choose  ·  Solve a level to open the next", 0.4f, PuzzleDim);
        var anchor = hint.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 0.6f);
        scene.Add(hint);
        return scene;
    }

    // One level: its walls and floor as a tilemap, the worker, the crates and their marks, what's on screen, and the panel
    // that opens once every crate is on a mark.
    private static Scene PuzzleLevel(int index)
    {
        var (name, rows) = PuzzleLevels[index];
        var width = rows.Max(r => r.Length);
        var height = rows.Length;
        var scene = new Scene();

        // The level fills the view, whatever its size, with room for the HUD above and below.
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = Math.Max(height / 2f + 1.6f, (width / 2f + 1) * 9 / 16);
        view.Background = Color.FromBytes(0x22, 0x1E, 0x1C);
        view.Transition = SceneTransition.Wipe;
        scene.Add(camera);

        // Walls are solid; the floor is a background tile, so it's drawn but never blocks. Move History lets the player
        // take moves back (Z) and start over (R); the counter at the top counts its moves.
        var origin = new Vector2(-width / 2f, height / 2f);
        var level = new Entity("Level");
        level.Transform.Position = new Vector3(origin, 0);
        var map = level.AddComponent<Tilemap>();
        map.Image = PuzzlesArt.PuzzleTiles;
        map.Columns = PuzzlesArt.PuzzleColumns;
        map.BackgroundTiles = PuzzlesArt.WarehouseFloorTile.ToString(CultureInfo.InvariantCulture);
        map.Order = -10;
        map.Tiles = Inside(rows, width).Select((row, y) => new string(row.Select((cell, x) => cell switch
        {
            '#' => (char)('0' + PuzzlesArt.WarehouseWallTile),
            'o' => Tilemap.Empty,
            _ => (char)('0' + PuzzlesArt.WarehouseFloorTile),
        }).ToArray())).ToList();
        var history = level.AddComponent<MoveHistory>();
        history.UndoSound = TemplateSounds.Select;
        history.RestartSound = TemplateSounds.Dash;
        scene.Add(level);

        Vector2 At(int x, int y) => origin + new Vector2(x + 0.5f, -y - 0.5f);
        var crates = new Entity("Crates");
        var goals = new Entity("Goals");
        scene.Add(goals);
        scene.Add(crates);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                var cell = rows[y][x];
                if (cell is '.' or '*' or '+')
                    scene.Add(PuzzleGoal(At(x, y)), goals);
                if (cell is '$' or '*')
                    scene.Add(PuzzleCrate(At(x, y)), crates);
                if (cell is '@' or '+')
                    scene.Add(PuzzleWorker(At(x, y)));
            }
        }

        AddPuzzleHud(scene, index, name);
        scene.Add(SolvedPanel(index));

        // Solving it saves that it's solved, which opens the next level's button on the menu.
        var progress = new Entity("Progress");
        var solved = progress.AddComponent<Counter>();
        solved.Message = "Solved";
        solved.Format = null;
        solved.SaveBestAs = SolvedKey(index);
        scene.Add(progress);

        AddPauseMenu(scene, menuScene: "Main");
        return scene;
    }

    // The rows with the cells outside the walls marked 'o' (anything the outside reaches from the edge without crossing a
    // wall), so they're left empty; the rest is floor.
    private static IEnumerable<string> Inside(string[] rows, int width)
    {
        var grid = rows.Select(r => r.PadRight(width).ToCharArray()).ToArray();
        var open = new Stack<(int X, int Y)>();
        for (var y = 0; y < grid.Length; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x == 0 || y == 0 || x == width - 1 || y == grid.Length - 1)
                    open.Push((x, y));
            }
        }

        while (open.TryPop(out var c))
        {
            if (c.X < 0 || c.Y < 0 || c.X >= width || c.Y >= grid.Length || grid[c.Y][c.X] != ' ')
                continue;
            grid[c.Y][c.X] = 'o';
            open.Push((c.X + 1, c.Y));
            open.Push((c.X - 1, c.Y));
            open.Push((c.X, c.Y + 1));
            open.Push((c.X, c.Y - 1));
        }

        return grid.Select(r => new string(r));
    }

    // The worker: steps a cell at a time (a moment's hold before it walks on, so single steps come easily) and pushes crates.
    private static Entity PuzzleWorker(Vector2 at)
    {
        var entity = new Entity("Player");
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Image = PuzzlesArt.Worker;
        sprite.Size = new Vector2(0.9f, 0.9f);
        sprite.Order = 2;
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = new Vector2(0.8f, 0.8f);
        collider.Layer = "Player";
        entity.AddComponent<Body>().GravityScale = 0;
        var movement = entity.AddComponent<GridMovement>();
        movement.StepTime = 0.12f;
        movement.RepeatDelay = 0.25f;
        return entity;
    }

    // A crate the worker pushes a cell at a time; it turns green on a mark.
    private static Entity PuzzleCrate(Vector2 at)
    {
        var entity = new Entity("Crate");
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Image = TemplateArt.CrateArt;
        sprite.Size = new Vector2(0.9f, 0.9f);
        sprite.Order = 1;
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = new Vector2(0.9f, 0.9f);
        collider.Layer = "Crate";
        entity.AddComponent<Body>().GravityScale = 0;
        entity.AddComponent<GridMovement>().StepSound = TemplateSounds.Dig;
        entity.AddComponent<Pushable>();
        return entity;
    }

    // A mark on the floor. Once every one has a crate on it, they send "Solved".
    private static Entity PuzzleGoal(Vector2 at)
    {
        var entity = new Entity("Goal");
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Image = PuzzlesArt.GoalMark;
        sprite.Size = new Vector2(0.7f, 0.7f);
        sprite.Order = -5;
        var goal = entity.AddComponent<Goal>();
        goal.CoveredTint = Color.FromBytes(0xA8, 0xF0, 0x90);
        goal.Sound = TemplateSounds.Switch;
        goal.AllCoveredMessage = "Solved";
        return entity;
    }

    // The level's number and name at the top left, crates on marks and moves at the top right, and the keys at the bottom.
    private static void AddPuzzleHud(Scene scene, int index, string name)
    {
        var title = Label("Level Name", Vector2.Zero, $"{index + 1}. {name}", 0.6f, PuzzleOrange);
        title.GetComponent<Text>()!.Align = TextAlign.Left;
        var topLeft = title.AddComponent<ScreenAnchor>();
        topLeft.Offset = new Vector2(0.6f, -0.7f);
        scene.Add(title);

        var crates = Label("Crate Count", Vector2.Zero, "Crates: 0", 0.5f, PuzzleLight);
        crates.GetComponent<Text>()!.Align = TextAlign.Right;
        var cratesCorner = crates.AddComponent<ScreenAnchor>();
        cratesCorner.Anchor = Anchor.TopRight;
        cratesCorner.Offset = new Vector2(-0.6f, -0.7f);
        var cratesCounter = crates.AddComponent<Counter>();
        cratesCounter.Message = null;
        cratesCounter.CountGoals = true;
        cratesCounter.Format = "Crates: {0} / {2}";
        scene.Add(crates);

        var moves = Label("Moves", Vector2.Zero, "Moves: 0", 0.5f, PuzzleLight);
        moves.GetComponent<Text>()!.Align = TextAlign.Right;
        var movesCorner = moves.AddComponent<ScreenAnchor>();
        movesCorner.Anchor = Anchor.TopRight;
        movesCorner.Offset = new Vector2(-0.6f, -1.4f);
        var movesCounter = moves.AddComponent<Counter>();
        movesCounter.Message = null;
        movesCounter.CountMoves = true;
        movesCounter.Format = "Moves: {0}";
        movesCounter.Pop = 1;
        scene.Add(moves);

        // "{Undo}" shows Z on the keyboard and LB on a gamepad.
        var hint = Label("Hint", Vector2.Zero, "Arrows to push  ·  {Undo} to undo  ·  {Restart} to restart  ·  {Pause} for the menu", 0.4f, PuzzleDim);
        var bottom = hint.AddComponent<ScreenAnchor>();
        bottom.Anchor = Anchor.Bottom;
        bottom.Offset = new Vector2(0, 0.6f);
        scene.Add(hint);
    }

    // Opens on "Solved": how many moves it took, and on to the next level (or back to the menu after the last), again, or
    // the menu.
    private static Entity SolvedPanel(int index)
    {
        var last = index == PuzzleLevels.Count - 1;
        var panel = new Entity("Solved");
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var solved = panel.AddComponent<Panel>();
        solved.OpenMessage = "Solved";
        solved.CloseOnBack = false;
        solved.Sound = TemplateSounds.PowerUp;
        var backdrop = panel.AddComponent<Sprite>();
        backdrop.Size = new Vector2(10, last ? 5.2f : 6.4f);
        backdrop.Tint = new Color(0.07f, 0.08f, 0.1f, 0.94f);
        backdrop.Order = 400;

        var heading = Label("Heading", new Vector2(0, last ? 1.6f : 2.2f), last ? "All Solved!" : "Solved!", 1.3f, Color.FromBytes(0x9C, 0xE0, 0x8A));
        heading.GetComponent<Text>()!.Order = 401;
        WithChild(panel, heading);

        // Counts the level's moves, as the HUD's counter does.
        var detail = Label("Detail", new Vector2(0, last ? 0.4f : 1), "", 0.5f, PuzzleLight);
        detail.GetComponent<Text>()!.Order = 401;
        var moves = detail.AddComponent<Counter>();
        moves.Message = null;
        moves.CountMoves = true;
        moves.Format = last ? "In {0} moves. That's every level: make some more!" : "In {0} moves.";
        moves.Pop = 1;
        WithChild(panel, detail);

        var next = last
            ? MenuButton("Menu", new Vector2(-2.2f, -1.2f), Scenes.ButtonAction.LoadScene, "Main", Key.Enter, PuzzleBlue, order: 410)
            : MenuButton("Next", new Vector2(-2.2f, -0.6f), Scenes.ButtonAction.LoadScene, PuzzleLevelScene(index + 1), Key.Enter, PuzzleBlue, order: 410);
        WithChild(panel, next);
        WithChild(panel, MenuButton("Again", new Vector2(2.2f, last ? -1.2f : -0.6f), Scenes.ButtonAction.RestartScene, null, Key.R, PuzzleGray, order: 410));
        if (!last)
            WithChild(panel, MenuButton("Menu", new Vector2(0, -1.9f), Scenes.ButtonAction.LoadScene, "Main", Key.Escape, PuzzleGray, order: 410));
        return panel;
    }
}
