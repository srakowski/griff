using Griff.Modules.Rhythm;
using Griff.Modules.VisualNovels;
using Griff.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards who does the game's work: each of Griff's systems (the gameplay ones, menus, panels, scenes, scripts, saves and
/// the rest) owns its state and its steps of the frame, and <c>Game</c> is only the scheduler and what every system shares
/// (the frame, time, input and the players, the scene, the systems, services), in the files named here; the rest of
/// <c>Game</c> is scripts' API, each member forwarding to the system that does the work. So a system is the place to read
/// what a feature does, and the way Griff builds a feature is the way a module or script's system does.
/// </summary>
public sealed class GameSystemsTests
{
    // The files of Game that schedule the frame (Game.Frame.cs) and hold what every system shares: the scene, time and
    // services (Game.cs), and the players and their input (Game.Players.cs). Every other part of Game only forwards.
    private static readonly string[] ScheduleAndShare = ["Game.cs", "Game.Frame.cs", "Game.Players.cs"];

    // Each of Griff's steps of the frame (a Steps member) and the system that owns it.
    public static TheoryData<string, string> CoreSteps => new()
    {
        { "Panels", "Panels" }, { "Pauses", "Pauses" }, { "Time", "Time" }, { "Input", "Input" }, { "DebugDrawing", "Time" },
        { "BeginNetwork", "Network" }, { "Tables", "Tables" }, { "CarriedValues", "Scenes" }, { "ScriptStart", "Scripts" },
        { "Menus", "Menus" }, { "Lobby", "Lobby" }, { "Grids", "Tilemaps" }, { "SwitchTargets", "Switches" }, { "Turns", "Turns" },
        { "BeatClocks", "Beat" }, { "Calibrations", "Calibration" }, { "Enemies", "Levels" }, { "Movement", "Movement" },
        { "ItemUses", "Items" }, { "Scripts", "Scripts" }, { "HUD", "HUD" }, { "OtherPlayersTools", "Network" },
        { "Dragging", "Dragging" }, { "Attacks", "Combat" }, { "IdleGridMovers", "Turns" }, { "Timers", "Timers" },
        { "Coroutines", "Timers" }, { "Tweens", "Timers" }, { "Lifetimes", "Lifetimes" }, { "Spawners", "Lifetimes" },
        { "Shooters", "Levels" }, { "Movers", "Levels" }, { "Animations", "Effects" }, { "AnimatedTiles", "Tilemaps" },
        { "Particles", "Effects" }, { "CameraShake", "Effects" }, { "SpriteEffects", "Effects" }, { "SimulatePrompts", "Panels" },
        { "Typewriters", "HUD" }, { "PickupSpin", "Items" }, { "FixedScripts", "Scripts" }, { "BodyRoles", "Movement" }, { "Physics", "Physics" },
        { "Carrying", "Dragging" }, { "Pickups", "Items" }, { "Checkpoints", "Levels" }, { "Doors", "Levels" },
        { "Dialogues", "Levels" }, { "Switches", "Switches" }, { "Bouncers", "Movement" }, { "Crumbles", "Movement" },
        { "TouchDamage", "Combat" }, { "Health", "Combat" }, { "Respawns", "Levels" }, { "Bars", "HUD" }, { "Stars", "HUD" },
        { "Counters", "HUD" }, { "TextOutputs", "HUD" }, { "Fade", "Scenes" }, { "ScreenFlash", "Effects" },
        { "Destroys", "Lifetimes" }, { "SceneChange", "Scenes" }, { "Rooms", "Atmosphere" }, { "Followers", "Effects" },
        { "LateScripts", "Scripts" }, { "ScreenAnchors", "HUD" }, { "Trails", "Effects" }, { "Lights", "Atmosphere" },
        { "Weather", "Weather" }, { "PresentPrompts", "Panels" }, { "SoundSources", "Atmosphere" },
        { "SoundPlayers", "Atmosphere" }, { "PatrolWalks", "Levels" }, { "Audio", "Atmosphere" }, { "Settings", "Settings" },
        { "EndNetwork", "Network" },
    };

    // Every step Griff has belongs to its system and runs the system's own code, not the game's.
    [Theory]
    [MemberData(nameof(CoreSteps))]
    public void ACoreStep_IsItsSystemsOwn(string step, string system)
    {
        var game = new Game(new Griff.Scenes.Scene(), new InputState());
        var id = (StepId)typeof(Steps).GetField(step)!.GetValue(null)!;

        var found = game.Frame.Single(s => s.Id == id);

        Assert.Equal(system, found.System?.Name);
        Assert.Same(found.System, found.Run.Target);
    }

    // No step of the frame, Griff's own or a module's, is a method of Game (nor a lambda written in it): Game schedules
    // them, and each is the work of the system it belongs to.
    [Fact]
    public void NoFrameStep_IsAMethodOfGame()
    {
        var game = new Game(new Griff.Scenes.Scene(), new InputState());

        var games = game.Frame.Where(s => IsGames(s.Run.Method.DeclaringType)).Select(s => s.Name).ToList();
        var notTheirSystems = game.Frame.Where(s => s.System is null || !ReferenceEquals(s.System, s.Run.Target)).Select(s => s.Name).ToList();

        Assert.Equal(CoreSteps.Count, typeof(Steps).GetFields().Count(f => f.FieldType == typeof(StepId)));
        Assert.True(games.Count == 0, "Steps that are Game's own methods (move them to the system that owns them):\n" + string.Join("\n", games));
        Assert.True(notTheirSystems.Count == 0, "Steps that don't run their own system's method:\n" + string.Join("\n", notTheirSystems));

        static bool IsGames(Type? type)
        {
            for (var t = type; t is not null; t = t.DeclaringType)
            {
                if (t == typeof(Game))
                    return true;
            }

            return false;
        }
    }

    // What any of Griff's systems keeps is its own: private fields, no property another type could change or reach into (a
    // getter for scripts' API, such as the dialogue being said, is all it shows).
    [Fact]
    public void ACoreSystemsState_IsPrivateToIt()
    {
        var game = new Game(new Griff.Scenes.Scene(), new InputState());
        var systems = game.Systems.Select(s => s.GetType()).Where(t => t.Namespace == typeof(Game).Namespace).ToList();
        const System.Reflection.BindingFlags declared = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;

        var shown = systems.SelectMany(type => type.GetFields(declared)
                .Where(f => !f.IsPrivate || f.Name.EndsWith(">k__BackingField", StringComparison.Ordinal))
                .Select(f => $"{type.Name}.{f.Name}"))
            .ToList();

        Assert.Equal(36, systems.Count);
        Assert.True(shown.Count == 0, "A system's state is private fields of its own:\n" + string.Join("\n", shown));
    }

    // Game is the files that schedule and share; everywhere else its members are scripts' API, each a forward to the
    // system that does the work: no state, no logic, nothing private.
    [Fact]
    public void Game_OutsideTheFilesThatScheduleAndShare_OnlyForwards()
    {
        var (compilation, trees) = LayerTests.Core();
        var system = compilation.GetTypeByMetadataName("Griff.Runtime.GameSystem")!;
        var wrong = new List<string>();
        var files = new HashSet<string>();
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            var game = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(c => c.Identifier.Text == "Game" && model.GetDeclaredSymbol(c)?.ToDisplayString() == "Griff.Runtime.Game").ToList();
            if (game.Count == 0)
                continue;
            files.Add(Path.GetFileName(tree.FilePath));
            if (ScheduleAndShare.Contains(Path.GetFileName(tree.FilePath)))
                continue;
            foreach (var member in game.SelectMany(c => c.Members))
            {
                if (!Forwards(member, model, system))
                    wrong.Add($"{Path.GetFileName(tree.FilePath)}: {Describe(member)}");
            }
        }

        Assert.Superset(ScheduleAndShare.ToHashSet(), files);
        Assert.True(wrong.Count == 0,
            "Game is the scheduler and what every system shares (" + string.Join(", ", ScheduleAndShare) + "); elsewhere it keeps " +
            "only scripts' API, each member forwarding to the system that owns the work (move the rest into the system):\n" +
            string.Join("\n", wrong));
    }

    // A genre's settings are its module's, not Game's: the Rhythm module keeps the note speed, the Visual Novels module how
    // the player likes dialogue skipped and read on and the lines they've read. A player's settings saved before are read
    // back from the same keys, and scripts still reach them as Game's (the modules' extension members).
    [Fact]
    public void GenreSettings_AreTheirModules_AndPlayersSavedSettingsCarryOver()
    {
        string[] moved = ["NoteSpeed", "MinNoteSpeed", "MaxNoteSpeed", "NoteSpeedSaveKey", "SkipUnread", "AutoSpeed", "SkipUnreadSaveKey",
            "AutoSpeedSaveKey", "ReadLinesKey", "HasRead"];
        var dialogue = new Griff.Scenes.Entity("Talk").AddComponent<Griff.Scenes.Dialogue>();
        dialogue.Lines = ["Hello."];
        var data = new SaveData();
        data.Set("griff.noteSpeed", 2.5f);
        data.Set("griff.skipUnread", true);
        data.Set("griff.autoSpeed", 0.8f);

        var before = new Game(new Griff.Scenes.Scene(), new InputState()) { SaveData = data };
        Assert.False(before.HasRead(dialogue, 0));
        before.ReadingSystem.NoteLineShown(dialogue, 0, said: true, resume: false);
        before.Quit();
        var game = new Game(new Griff.Scenes.Scene(), new InputState()) { SaveData = data };

        Assert.Empty(moved.SelectMany(name => typeof(Game).GetMember(name)));
        Assert.Equal((2.5f, true, 0.8f), (game.NoteSpeed, game.SkipUnread, game.AutoSpeed));
        Assert.True(game.HasRead(dialogue, 0));
        Assert.NotNull(data.GetText("griff.read"));
        Assert.Equal(("griff.noteSpeed", "griff.skipUnread", "griff.autoSpeed", "griff.read", 0.25f, 4f),
            (Game.NoteSpeedSaveKey, Game.SkipUnreadSaveKey, Game.AutoSpeedSaveKey, Game.ReadLinesKey, Game.MinNoteSpeed, Game.MaxNoteSpeed));
    }

    private static bool Forwards(MemberDeclarationSyntax member, SemanticModel model, INamedTypeSymbol system)
    {
        var isPublic = member.Modifiers.Any(m => m.Text == "public");
        switch (member)
        {
            case FieldDeclarationSyntax field:
                return isPublic && field.Modifiers.Any(m => m.Text == "const");
            case BaseTypeDeclarationSyntax:
                return isPublic;
            case MethodDeclarationSyntax method:
                return (isPublic || method.ExplicitInterfaceSpecifier is not null) && Reaches(method.ExpressionBody?.Expression, model, system);
            case PropertyDeclarationSyntax property:
                if (!isPublic)
                    return false;
                if (property.ExpressionBody is { } body)
                    return Reaches(body.Expression, model, system);
                return property.AccessorList!.Accessors.All(a => Reaches(a.ExpressionBody?.Expression, model, system));
            case EventDeclarationSyntax e:
                return isPublic && e.AccessorList!.Accessors.All(a => Reaches(a.ExpressionBody?.Expression, model, system));
            default:
                return false;
        }
    }

    // An expression that goes straight to a system: a call or member of one of Game's systems (or a system type's static).
    private static bool Reaches(ExpressionSyntax? expression, SemanticModel model, INamedTypeSymbol system)
    {
        if (expression is AssignmentExpressionSyntax assignment)
            expression = assignment.Left;
        while (true)
        {
            switch (expression)
            {
                case InvocationExpressionSyntax call:
                    expression = call.Expression;
                    continue;
                case MemberAccessExpressionSyntax access:
                    if (IsSystem(model.GetSymbolInfo(access.Expression).Symbol, system))
                        return true;
                    expression = access.Expression;
                    continue;
                default:
                    return false;
            }
        }
    }

    private static bool IsSystem(ISymbol? symbol, INamedTypeSymbol system)
    {
        var type = symbol switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            INamedTypeSymbol named => named,
            _ => null,
        };
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, system))
                return true;
        }

        return false;
    }

    private static string Describe(MemberDeclarationSyntax member)
    {
        var text = member.WithoutTrivia().ToString();
        var end = text.IndexOfAny(['\n', '{', '=']);
        return (end > 0 ? text[..end] : text).Trim();
    }
}
