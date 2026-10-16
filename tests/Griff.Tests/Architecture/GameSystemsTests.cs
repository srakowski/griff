using Griff.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards who does gameplay's work: each of Griff's gameplay systems (levels, movement, grid movement, turns, combat,
/// triggers, switches, items, dragging) owns its state and its steps of the frame, and <c>Game</c> keeps only what
/// scripts call, each member forwarding to the system. So a system is the place to read what a feature does, and the
/// way Griff builds a feature is the way a module or script's system does.
/// </summary>
public sealed class GameSystemsTests
{
    // The areas whose Game members only forward: a Game partial of that name may be left holding scripts' API.
    private static readonly string[] Areas = ["Levels", "Movement", "Grid", "Turns", "Combat", "Triggers", "Switches", "Items", "Dragging"];

    // Each gameplay step of the frame and the system that owns it.
    public static TheoryData<string, string> GameplaySteps => new()
    {
        { "Enemies", "Levels" }, { "Shooters", "Levels" }, { "Movers", "Levels" }, { "Checkpoints", "Levels" }, { "Doors", "Levels" },
        { "Dialogues", "Levels" }, { "Respawns", "Levels" }, { "Patrol Walks", "Levels" },
        { "Movement", "Movement" }, { "Bouncers", "Movement" }, { "Crumbles", "Movement" },
        { "Turns", "Turns" }, { "Idle Grid Movers", "Turns" },
        { "Attacks", "Combat" }, { "Touch Damage", "Combat" }, { "Health", "Combat" },
        { "Switch Targets", "Switches" }, { "Switches", "Switches" },
        { "Item Uses", "Items" }, { "Pickups", "Items" }, { "Pickup Spin", "Items" },
        { "Dragging", "Dragging" }, { "Carrying", "Dragging" },
    };

    // A gameplay step belongs to its system and runs the system's own code, not the game's.
    [Theory]
    [MemberData(nameof(GameplaySteps))]
    public void AGameplayStep_IsItsSystemsOwn(string step, string system)
    {
        var game = new Game(new Griff.Scenes.Scene(), new InputState());

        var found = game.Frame.Single(s => s.Name == step);

        Assert.Equal(system, found.System?.Name);
        Assert.Same(found.System, found.Run.Target);
    }

    // What a gameplay system keeps is its own: private fields, no property another type could change or reach into (a
    // getter for scripts' API, such as the dialogue being said, is all it shows).
    [Fact]
    public void AGameplaySystemsState_IsPrivateToIt()
    {
        var game = new Game(new Griff.Scenes.Scene(), new InputState());
        string[] names = ["Levels", "Movement", "Grid Movement", "Turns", "Combat", "Triggers", "Switches", "Items", "Dragging"];
        var systems = game.Systems.Where(s => names.Contains(s.Name)).Select(s => s.GetType()).ToList();
        const System.Reflection.BindingFlags declared = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;

        var shown = systems.SelectMany(type => type.GetFields(declared)
                .Where(f => !f.IsPrivate || f.Name.EndsWith(">k__BackingField", StringComparison.Ordinal))
                .Select(f => $"{type.Name}.{f.Name}"))
            .ToList();

        Assert.Equal(names.Length, systems.Count);
        Assert.True(shown.Count == 0, "A gameplay system's state is private fields of its own:\n" + string.Join("\n", shown));
    }

    // Game's members for the gameplay areas are scripts' API, each a forward to the system that does the work: no state,
    // no logic, nothing private.
    [Fact]
    public void Game_OnlyForwardsToTheGameplaySystems()
    {
        var (compilation, trees) = LayerTests.Core();
        var system = compilation.GetTypeByMetadataName("Griff.Runtime.GameSystem")!;
        var wrong = new List<string>();
        foreach (var tree in trees.Where(t => Areas.Any(a => Path.GetFileName(t.FilePath) == $"Game.{a}.cs")))
        {
            var model = compilation.GetSemanticModel(tree);
            var game = tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Where(c => c.Identifier.Text == "Game");
            foreach (var member in game.SelectMany(c => c.Members))
            {
                if (!Forwards(member, model, system))
                    wrong.Add($"{Path.GetFileName(tree.FilePath)}: {Describe(member)}");
            }
        }

        Assert.True(wrong.Count == 0,
            "Game keeps only scripts' API for its gameplay systems, each member forwarding to the system that owns the work " +
            "(move the rest into the system):\n" + string.Join("\n", wrong));
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
