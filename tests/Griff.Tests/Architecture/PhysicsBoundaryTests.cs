using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Griff.Tests.Architecture;

/// <summary>
/// Guards what physics knows. The solver (every part of Griff.Runtime.Physics) works out bodies, colliders, hinges and
/// ropes (the Physics category's components) and the tilemaps they meet, and names no other component: what a crate or a
/// player is to the game reaches it as the body's own settings (Body.GivesWay, Body.HoldsRopes), set by the system that
/// owns them, and what it moved reports back (touches, impacts, pushes). Nor does it branch on a tilemap's grid shape: a
/// map whose cells aren't square boxes hands it walls (Tilemap.Walls, a TileWalls), so a new grid shape is one more
/// TileWalls class and physics doesn't change.
/// </summary>
public sealed class PhysicsBoundaryTests
{
    [Fact]
    public void Physics_NamesNoComponentButBodiesAndColliders()
    {
        var (compilation, trees) = PhysicsTrees();
        var component = compilation.GetTypeByMetadataName("Griff.Scenes.Component")!;
        var collider = compilation.GetTypeByMetadataName("Griff.Scenes.Collider")!;
        var tilemap = compilation.GetTypeByMetadataName("Griff.Scenes.Tilemap")!;
        var transform = compilation.GetTypeByMetadataName("Griff.Scenes.Transform")!;

        // What physics works out: the Physics category (bodies, colliders, hinges, ropes), the colliders' base, the tilemaps
        // whose tiles it meets, and every entity's place.
        bool Physical(INamedTypeSymbol type) =>
            SymbolEqualityComparer.Default.Equals(type, collider) || SymbolEqualityComparer.Default.Equals(type, tilemap) ||
            SymbolEqualityComparer.Default.Equals(type, transform) ||
            type.GetAttributes().Any(a => a.AttributeClass?.Name == "CategoryAttribute" && a.ConstructorArguments is [{ Value: "Physics" }]);

        var named = Named(compilation, trees)
            .Where(t => !SymbolEqualityComparer.Default.Equals(t.Type, component) && DerivesFrom(t.Type, component) && !Physical(t.Type))
            .Select(t => $"{t.File}: {t.Type.Name}")
            .Distinct()
            .Order()
            .ToList();

        Assert.True(trees.Count >= 6, $"Found {trees.Count} files of Physics");
        Assert.True(named.Count == 0,
            "Physics names a component that isn't a body, a collider, a hinge, a rope or a tilemap (let the system that owns " +
            "it set the body's settings before the step, and react to what physics reports):\n" + string.Join("\n", named));
    }

    [Fact]
    public void Physics_NamesNoGridShape()
    {
        var (compilation, trees) = PhysicsTrees();
        var shape = compilation.GetTypeByMetadataName("Griff.Scenes.TileShape")!;
        var walls = compilation.GetTypeByMetadataName("Griff.Scenes.TileWalls")!;

        var named = Named(compilation, trees)
            .Where(t => SymbolEqualityComparer.Default.Equals(t.Type, shape) || (DerivesFrom(t.Type, walls) && !SymbolEqualityComparer.Default.Equals(t.Type, walls)))
            .Select(t => $"{t.File}: {t.Type.Name}")
            .Distinct()
            .Order()
            .ToList();

        Assert.True(named.Count == 0,
            "Physics branches on a tilemap's grid shape (ask the map's walls, Tilemap.Walls, what they are instead):\n" +
            string.Join("\n", named));
    }

    // Every kind of walls is made by the tilemap from its shape, and each grid shape that isn't square has one.
    [Fact]
    public void EveryGridShape_ButSquare_HasWallsOfItsOwn()
    {
        foreach (var shape in Enum.GetValues<Griff.Scenes.TileShape>())
        {
            var level = new Griff.Scenes.Entity("Level");
            var map = level.AddComponent<Griff.Scenes.Tilemap>();
            map.Shape = shape;
            map.Tiles = ["11"];
            Assert.Equal(shape == Griff.Scenes.TileShape.Square, map.TilesAreBoxes);
            Assert.Equal(shape == Griff.Scenes.TileShape.Square, map.Walls() is null);
        }
    }

    // The files that declare part of Physics (its partial class), with the compilation they're in.
    private static (Compilation Compilation, List<SyntaxTree> Trees) PhysicsTrees()
    {
        var (compilation, trees) = LayerTests.Core();
        var physics = compilation.GetTypeByMetadataName("Griff.Runtime.Physics")!;
        var declaring = physics.DeclaringSyntaxReferences.Select(r => r.SyntaxTree).Distinct().ToList();
        return (compilation, trees.Where(declaring.Contains).ToList());
    }

    private static IEnumerable<(string File, INamedTypeSymbol Type)> Named(Compilation compilation, List<SyntaxTree> trees)
    {
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (LayerTests.Used(model.GetSymbolInfo(name).Symbol) is INamedTypeSymbol type)
                    yield return (Path.GetFileName(tree.FilePath), type);
            }
        }
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, baseType))
                return true;
        }

        return false;
    }
}
