using System.Numerics;
using System.Text.RegularExpressions;
using Griff.Editing;
using Griff.Tests.Architecture;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

/// <summary>
/// A name finds an entity one way (<see cref="EntityNames"/>): inside the entity that holds it first, then the scene, the
/// local player's before another in a network game, the nearest when a component says so; the running game, renaming, the
/// Inspector's flags and agents agree, and a name several entities answer to where one is needed is flagged.
/// </summary>
public sealed partial class EntityNamesTests
{
    private static readonly ComponentRegistry Registry = ComponentRegistry.CreateDefault();

    private static (Scene Scene, Entity Holder, Entity Inner, Entity Outer) Sample()
    {
        var scene = new Scene();
        var outer = new Entity("Hint");
        var holder = new Entity("Lever");
        var inner = new Entity("Hint");
        scene.Add(outer);
        scene.Add(holder);
        scene.Add(inner, holder);
        return (scene, holder, inner, outer);
    }

    [Fact]
    public void AName_IsTrimmed_AndMeansWhatIsInsideTheHolderFirst()
    {
        var (scene, holder, inner, outer) = Sample();
        Assert.Same(inner, EntityNames.Find(scene, " Hint ", new NameLookup(holder)));
        Assert.Same(inner, scene.FindNamed("Hint", near: holder));
        Assert.Same(outer, EntityNames.Find(scene, "Hint"));
        Assert.Same(outer, EntityNames.Find(scene, "Hint", new NameLookup(holder, Scope: NameScope.Scene)));
        Assert.Null(EntityNames.Find(scene, "  "));
    }

    [Fact]
    public void EveryWayOfFindingAName_MeansTheSameEntity()
    {
        var (scene, holder, inner, _) = Sample();
        var other = new Entity("Hint");
        scene.Add(other);

        Assert.Same(inner, scene.FindNamed("Hint", near: holder));
        Assert.Same(inner, Griff.Runtime.TriggerSystem.NamedFrom(holder, "Hint"));
        Assert.Same(inner, EntityReferences.Resolve(holder, new EntityNameAttribute(), "Hint"));
        Assert.Contains(inner, EntityNames.All(holder, "Hint"));
        Assert.Equal([inner], EntityNames.All(holder, "Hint"));
        Assert.Equal([inner], Switch.Named(holder, "Hint").ToList());
    }

    [Fact]
    public void ALocalPlayer_ComesFirst_ButNotWhenTheNearestIsAsked()
    {
        var scene = new Scene();
        var near = new Entity("Player");
        var mine = new Entity("Player");
        var seeker = new Entity("Seeker");
        scene.Add(near);
        scene.Add(mine);
        scene.Add(seeker);
        near.Transform.Position = new Vector3(1, 0, 0);
        mine.Transform.Position = new Vector3(50, 0, 0);
        scene.PreferNamed = e => e == mine;

        Assert.Same(mine, scene.FindNamed("Player"));
        Assert.Same(near, EntityNames.Find(scene, "Player", new NameLookup(seeker, Scope: NameScope.Scene,
            Centre: e => new Vector2(e.Transform.Position.X, 0))));
    }

    [Fact]
    public void ANameSeveralAnswerTo_IsFlaggedWhereOneIsNeeded_AndNotWhereAllAreMeant()
    {
        var (scene, holder, inner, _) = Sample();
        var follow = holder.AddComponent<Follow>();
        follow.Target = "Lamp";
        scene.Add(new Entity("Lamp"));
        Assert.Empty(NameChecks.Lines(scene, Registry));

        scene.Add(new Entity("Lamp"));
        var line = Assert.Single(NameChecks.Lines(scene, Registry));
        Assert.Contains("Lever: Follow.Target: Several entities in the scene are called \"Lamp\"", line);

        // A switch means all of them; a name that is inside the holder is that one.
        holder.RemoveComponent(follow);
        holder.AddComponent<Switch>().Targets = "Lamp";
        Assert.Empty(NameChecks.Lines(scene, Registry));
        holder.AddComponent<Follow>().Target = "Hint";
        Assert.Empty(NameChecks.Lines(scene, Registry));
        _ = inner;
    }

    [Fact]
    public void RenamingAnEntity_LeavesAlone_AFieldThatMeansAnother()
    {
        var (scene, holder, inner, outer) = Sample();
        holder.AddComponent<Follow>().Target = "Hint";
        RenameEntityEdit.WithReferences(outer, "Sign", Registry).Apply();
        Assert.Equal("Hint", holder.GetComponent<Follow>()!.Target);
        RenameEntityEdit.WithReferences(inner, "Note", Registry).Apply();
        Assert.Equal("Note", holder.GetComponent<Follow>()!.Target);
        _ = scene;
    }

    [Fact]
    public void NoCodeLooksUpAnEntityByNameItself()
    {
        // Every lookup goes through EntityNames: no lambda or loop in the engine compares an entity's Name with a name it was
        // given. (Comparing with a constant, or with another entity's name, is not a lookup.)
        var lookup = LookupRegex();
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(LayerTests.RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(LayerTests.RepoRoot(), file).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.EndsWith("Scenes/EntityNames.cs", StringComparison.Ordinal) || relative.Contains("/obj/") || relative.Contains("/Diagnostics/") ||
                relative.StartsWith("src/Griff.Agent/", StringComparison.Ordinal) || relative.StartsWith("src/Griff.Editor/Features/Agent/", StringComparison.Ordinal) ||
                IsGenreDemoDriver(relative))
                continue;
            found.AddRange(File.ReadLines(file).Select((line, i) => (line, i)).Where(l => lookup.IsMatch(l.line))
                .Select(l => $"{relative}:{l.i + 1}: {l.line.Trim()}"));
        }

        Assert.Empty(found);
    }

    // The demo player's genre parts (Debug only) moved beside their genre's editor; like the rest of Diagnostics, they drive
    // the editor as a person would, finding entities by the names a demo script gives.
    private static bool IsGenreDemoDriver(string relative) =>
        relative.StartsWith("src/Griff.Editor/Genres/", StringComparison.Ordinal) &&
        (Path.GetFileName(relative).StartsWith("DemoUi.", StringComparison.Ordinal) || relative.EndsWith("Demo.cs", StringComparison.Ordinal));

    [GeneratedRegex(@"\b(e|entity|child)\s*=>\s*[a-z]+\.Name\s*(==\s*[a-z]\w*\b(?!\.)|\.Equals\([a-z]\w*\b(?!\.))|\b(entity|entities\[i\])\.Name\s*[!=]=\s*[a-z]\w*\b(?!\.)")]
    private static partial Regex LookupRegex();
}
