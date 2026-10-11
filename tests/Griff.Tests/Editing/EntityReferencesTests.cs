using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Editing;

/// <summary>Fields that name entities follow a rename, and names that match nothing are found.</summary>
public class EntityReferencesTests
{
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    // A lever that turns on the bridge and the lift, a camera following the player, and a fighter with its hitbox.
    private static Scene Level()
    {
        var scene = new Scene();
        var lever = new Entity("Lever");
        lever.AddComponent(new Switch { Targets = "Bridge, Lift", Prompt = "Hint" });
        scene.Add(lever);
        scene.Add(new Entity("Bridge"));
        scene.Add(new Entity("Lift"));
        scene.Add(new Entity("Hint"));
        scene.Add(new Entity("Player"));
        var camera = new Entity("Camera");
        camera.AddComponent(new Follow { Target = "Player" });
        scene.Add(camera);
        var fighter = new Entity("Fighter");
        fighter.AddComponent(new Attack { Target = "Player" });
        scene.Add(fighter);
        scene.Add(new Entity("Hitbox"), fighter);
        return scene;
    }

    private static Entity Named(Scene scene, string name) => scene.Entities.First(e => e.Name == name);

    [Fact]
    public void Rename_RenamesItInTheFieldsThatNameIt_AsOneUndoStep()
    {
        var scene = Level();
        var history = new EditHistory();

        history.Execute(EntityReferences.Rename(Named(scene, "Player"), "Hero", _registry));

        Assert.Equal("Hero", Named(scene, "Camera").GetComponent<Follow>()!.Target);
        Assert.Equal("Hero", Named(scene, "Fighter").GetComponent<Attack>()!.Target);
        Assert.Equal("Rename Player", history.UndoDescription);

        history.Undo();
        Assert.Equal("Player", Named(scene, "Camera").GetComponent<Follow>()!.Target);
        Assert.Equal("Player", Named(scene, "Fighter").GetComponent<Attack>()!.Target);
        Assert.Contains(scene.Entities, e => e.Name == "Player");
    }

    [Fact]
    public void Rename_ReplacesOneNameInAList_KeepingTheOthers()
    {
        var scene = Level();

        EntityReferences.Rename(Named(scene, "Lift"), "Elevator", _registry).Apply();

        Assert.Equal("Bridge, Elevator", Named(scene, "Lever").GetComponent<Switch>()!.Targets);
        Assert.Equal("Hint", Named(scene, "Lever").GetComponent<Switch>()!.Prompt);
    }

    [Fact]
    public void Rename_LeavesFieldsAlone_WhenAnotherEntityStillHasTheOldName()
    {
        var scene = Level();
        scene.Add(new Entity("Player")); // a second one: the fields still name that

        EntityReferences.Rename(Named(scene, "Player"), "Hero", _registry).Apply();

        Assert.Equal("Player", Named(scene, "Camera").GetComponent<Follow>()!.Target);
    }

    [Fact]
    public void Rename_OfAnOwnPrompt_RenamesItOnlyWhereItsOwn()
    {
        // A lever and a sign each with a "Hint" inside: each one's Prompt means its own, as in the game.
        var scene = new Scene();
        var lever = new Entity("Lever");
        lever.AddComponent(new Switch { Prompt = "Hint" });
        scene.Add(lever);
        scene.Add(new Entity("Hint"), lever);
        var sign = new Entity("Sign");
        sign.AddComponent(new Dialogue { Prompt = "Hint" });
        scene.Add(sign);
        scene.Add(new Entity("Hint"), sign);

        EntityReferences.Rename(lever.Children[0], "Lever Hint", _registry).Apply();

        Assert.Equal("Lever Hint", lever.GetComponent<Switch>()!.Prompt);
        Assert.Equal("Hint", sign.GetComponent<Dialogue>()!.Prompt);
    }

    [Fact]
    public void Rename_OfSomethingInside_OnlyRenamesItForWhatItsInside()
    {
        var scene = Level();
        var other = new Entity("Other Fighter");
        other.AddComponent(new Attack()); // its Hitbox is "Hitbox" too, but it has none of its own
        scene.Add(other);

        EntityReferences.Rename(Named(scene, "Hitbox"), "Sword", _registry).Apply();

        Assert.Equal("Sword", Named(scene, "Fighter").GetComponent<Attack>()!.Hitbox);
        Assert.Equal("Hitbox", other.GetComponent<Attack>()!.Hitbox);
    }

    [Fact]
    public void Rename_WithNothingNamingIt_IsJustARename()
    {
        var scene = Level();

        var edit = EntityReferences.Rename(Named(scene, "Bridge"), "Span", _registry);
        Assert.IsType<CompositeEdit>(edit); // the lever names it

        Assert.IsType<RenameEntityEdit>(EntityReferences.Rename(Named(scene, "Camera"), "Eye", _registry));
    }

    [Fact]
    public void Unmatched_IsTheNamesThatMatchNothing()
    {
        var scene = Level();
        Named(scene, "Lever").GetComponent<Switch>()!.Targets = "Bridge, Gate, Lift";
        Named(scene, "Camera").GetComponent<Follow>()!.Target = "Playr";

        var unmatched = EntityReferences.UnmatchedIn(scene, _registry).ToDictionary(u => u.Reference.Label, u => u.Names);

        Assert.Equal(["Gate"], unmatched["Switch.Targets"]);
        Assert.Equal(["Playr"], unmatched["Follow.Target"]);
        Assert.Equal(2, unmatched.Count);
    }

    [Fact]
    public void Unmatched_CountsPrefabs_AndLooksInsideForAHitbox()
    {
        var scene = Level();
        Named(scene, "Camera").GetComponent<Follow>()!.Target = "Bullet";
        Named(scene, "Fighter").GetComponent<Attack>()!.Hitbox = "Lift"; // in the scene, but not inside the fighter

        var unmatched = EntityReferences.UnmatchedIn(scene, _registry, prefabs: ["Bullet"]).ToList();

        var only = Assert.Single(unmatched);
        Assert.Equal("Attack.Hitbox", only.Reference.Label);
        Assert.Equal(["Lift"], only.Names);
    }

    [Fact]
    public void Unmatched_IgnoresEmptyFields()
    {
        var scene = Level();
        Named(scene, "Lever").GetComponent<Switch>()!.Targets = null;
        Named(scene, "Camera").GetComponent<Follow>()!.Target = " ";

        Assert.Empty(EntityReferences.UnmatchedIn(scene, _registry));
    }
}
