using System.Numerics;
using Griff.Scenes;

namespace Griff.Projects;

// The Falling Blocks template: a menu with the best score, and a game where pieces of four blocks fall, full rows clear
// and the pieces fall faster every ten rows, until they stack up to the top. All of it no-code components: a Falling
// Board, counters that show its score, rows and level, and a panel that opens on its "Game Over".
public static partial class ProjectTemplates
{
    private static readonly Color FallingCyan = Color.FromBytes(0x4C, 0xD4, 0xE8);
    private static readonly Color FallingBlue = Color.FromBytes(0x4A, 0x6C, 0xE8);
    private static readonly Color FallingGray = Color.FromBytes(0x34, 0x3A, 0x4E);
    private static readonly Color FallingLight = Color.FromBytes(0xC4, 0xCC, 0xDC);
    private static readonly Color FallingDim = Color.FromBytes(0x7A, 0x84, 0x98);
    private static readonly Color FallingSky = Color.FromBytes(0x12, 0x16, 0x22);

    // The best score is kept under this, shown on the menu and beside the board.
    private const string FallingBestKey = "best";

    private static Scene FallingMenu()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 6;
        view.Background = FallingSky;
        view.Transition = SceneTransition.Wipe;
        scene.Add(camera);

        scene.Add(Label("Title", new Vector2(0, 3.6f), "FALLING BLOCKS", 1.4f, FallingCyan));
        scene.Add(Label("Subtitle", new Vector2(0, 2.5f), "Fill a row to clear it. How long can you last?", 0.5f, FallingLight));

        var best = Label("Best", new Vector2(0, 1.3f), "Best: 0", 0.55f, Color.FromBytes(0xF2, 0xD3, 0x4B));
        var counter = best.AddComponent<Counter>();
        counter.Message = null;
        counter.SaveBestAs = FallingBestKey;
        counter.Format = "Best: {1}";
        scene.Add(best);

        scene.Add(MenuButton("Play", new Vector2(0, -0.2f), Scenes.ButtonAction.LoadScene, "Game", Runtime.Key.Enter, FallingBlue));
        scene.Add(MenuButton("Quit", new Vector2(0, -1.6f), Scenes.ButtonAction.Quit, null, Runtime.Key.Escape, FallingGray));

        var hint = Label("Hint", Vector2.Zero, "Enter to play  ·  Arrows to choose", 0.4f, FallingDim);
        var anchor = hint.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new Vector2(0, 0.6f);
        scene.Add(hint);
        return scene;
    }

    private static Scene FallingGame()
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 11.2f;
        view.Background = FallingSky;
        view.Transition = SceneTransition.Wipe;
        scene.Add(camera);

        // Ten across and twenty down, with the pieces coming next in a box to its right.
        var entity = new Entity("Board");
        var map = entity.AddComponent<Tilemap>();
        map.Image = TemplateArt.PieceTiles;
        map.Columns = TemplateArt.PieceColumns;
        map.Solid = false;
        var board = entity.AddComponent<FallingBoard>();
        entity.Transform.Position = new Vector3(-board.Columns / 2f - 2.5f, board.Rows / 2f, 0);
        board.Background = Color.FromBytes(0x08, 0x0A, 0x12, 0xD0);
        board.MoveSound = TemplateSounds.Select;
        board.RotateSound = TemplateSounds.Switch;
        board.LockSound = TemplateSounds.Dig;
        board.ClearSound = TemplateSounds.Coin;
        board.LevelUpSound = TemplateSounds.PowerUp;
        board.GameOverSound = TemplateSounds.Hurt;
        scene.Add(entity);

        var next = Label("Next", new Vector2(entity.Transform.Position.X + board.Columns + 3, board.Rows / 2f + 0.55f), "NEXT", 0.6f, FallingDim);
        scene.Add(next);

        AddFallingHud(scene);
        scene.Add(FallingGameOverPanel());
        AddPauseMenu(scene, menuScene: "Main");
        return scene;
    }

    // Down the left: the score, the rows cleared, the level and the best score. The keys at the bottom.
    private static void AddFallingHud(Scene scene)
    {
        var top = -0.8f;
        Counter Line(string name, string text, float size, Color color)
        {
            var label = Label(name, Vector2.Zero, text, size, color);
            label.GetComponent<Text>()!.Align = TextAlign.Left;
            var corner = label.AddComponent<ScreenAnchor>();
            corner.Offset = new Vector2(0.8f, top);
            top -= size + 0.6f;
            scene.Add(label);
            return label.AddComponent<Counter>();
        }

        var score = Line("Score", "Score: 0", 0.8f, FallingLight);
        score.Message = null;
        score.CountScore = true;
        score.Format = "Score: {0}";
        score.Pop = 1.15f;

        var lines = Line("Lines", "Lines: 0", 0.8f, FallingLight);
        lines.Message = "Lines";
        lines.Format = "Lines: {0}";

        // The board sends "Level Up" with how many levels it went up, and the game starts on level 1.
        var level = Line("Level", "Level: 1", 0.8f, FallingCyan);
        level.Message = "Level Up";
        level.Value = 1;
        level.Format = "Level: {0}";

        var best = Line("Best", "Best: 0", 0.6f, Color.FromBytes(0xF2, 0xD3, 0x4B));
        best.Message = null;
        best.CountScore = true;
        best.SaveBestAs = FallingBestKey;
        best.Format = "Best: {1}";
        best.Pop = 1;

        // "{Jump}" shows Space on the keyboard and A on a gamepad.
        var hint = Label("Hint", Vector2.Zero, "Left and Right to slide  ·  Up to turn  ·  Down to fall faster  ·  {Jump} to drop  ·  {Pause} for the menu", 0.55f, FallingDim);
        var bottom = hint.AddComponent<ScreenAnchor>();
        bottom.Anchor = Anchor.Bottom;
        bottom.Offset = new Vector2(0, 0.7f);
        scene.Add(hint);
    }

    // Opens on "Game Over": the score and the best, then again or the menu.
    private static Entity FallingGameOverPanel()
    {
        var panel = new Entity("Game Over");
        panel.Transform.Position = new Vector3(0, 0, 2);
        panel.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var over = panel.AddComponent<Panel>();
        over.OpenMessage = "Game Over";
        over.CloseOnBack = false;
        var backdrop = panel.AddComponent<Sprite>();
        backdrop.Size = new Vector2(10, 6.4f);
        backdrop.Tint = new Color(0.06f, 0.07f, 0.11f, 0.94f);
        backdrop.Order = 400;

        var heading = Label("Heading", new Vector2(0, 2.1f), "Game Over", 1.3f, Color.FromBytes(0xE0, 0x4A, 0x4A));
        heading.GetComponent<Text>()!.Order = 401;
        WithChild(panel, heading);

        var detail = Label("Detail", new Vector2(0, 0.9f), "", 0.6f, FallingLight);
        detail.GetComponent<Text>()!.Order = 401;
        var score = detail.AddComponent<Counter>();
        score.Message = null;
        score.CountScore = true;
        score.Format = "Score: {0}";
        score.Pop = 1;
        WithChild(panel, detail);

        var bestLine = Label("Best Score", new Vector2(0, 0.1f), "", 0.5f, Color.FromBytes(0xF2, 0xD3, 0x4B));
        bestLine.GetComponent<Text>()!.Order = 401;
        var best = bestLine.AddComponent<Counter>();
        best.Message = null;
        best.CountScore = true;
        best.SaveBestAs = FallingBestKey;
        best.Format = "Best: {1}";
        best.Pop = 1;
        WithChild(panel, bestLine);

        WithChild(panel, MenuButton("Again", new Vector2(-2.2f, -1.6f), Scenes.ButtonAction.RestartScene, null, Runtime.Key.Enter, FallingBlue, order: 410));
        WithChild(panel, MenuButton("Menu", new Vector2(2.2f, -1.6f), Scenes.ButtonAction.LoadScene, "Main", Runtime.Key.Escape, FallingGray, order: 410));
        return panel;
    }
}
