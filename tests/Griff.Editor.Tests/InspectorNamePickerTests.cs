using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Fields naming prefabs, scenes, input actions, layers and messages: picked from what the project has, flagged when
/// they name something it hasn't; and every field's list narrowed by typing.
/// </summary>
public sealed class InspectorNamePickerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void Wait(Task task)
    {
        EditorHost.Pump(task);
    }

    // A project with Bullet and Zombie prefabs, the bullet on the Shot layer, and in its scene a player on the Player
    // layer, a turret, a gem, a score counter and a camera.
    private (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;
        var serializer = new SceneSerializer(workspace.Components);
        Directory.CreateDirectory(Path.GetDirectoryName(project.DefaultPrefabPath("Bullet"))!);
        var bullet = new Entity("Bullet");
        bullet.AddComponent(new CircleCollider { Layer = "Shot" });
        File.WriteAllText(project.DefaultPrefabPath("Bullet"), serializer.SavePrefabToString(bullet));
        File.WriteAllText(project.DefaultPrefabPath("Zombie"), serializer.SavePrefabToString(new Entity("Zombie")));

        var document = workspace.Scene!;
        var scene = document.Scene;
        var player = new Entity("Player");
        player.AddComponent(new BoxCollider { Layer = "Player" });
        scene.Add(player);
        var turret = new Entity("Turret");
        turret.AddComponent(new Shooter { Prefab = "Bulet" });
        scene.Add(turret);
        var gem = new Entity("Gem");
        gem.AddComponent(new BoxCollider { IsTrigger = true });
        gem.AddComponent(new Pickup { Layers = "Player", Message = "Gems" });
        scene.Add(gem);
        var score = new Entity("Score");
        score.AddComponent(new Counter { Message = "Score" });
        scene.Add(score);
        var camera = new Entity("Camera");
        camera.AddComponent(new Follow());
        scene.Add(camera);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static PropertyField Field(InspectorPanel inspector, string component, string label) =>
        inspector.Components.Single(c => c.Title == component).Properties.Single(p => p.Label == label);

    [Fact]
    public void FieldsNamingPrefabsAndTheLike_ArePickers_FlaggedWhenTheProjectHasNoSuch() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Turret"));

        var prefab = Field(inspector, "Shooter", "Prefab");
        Assert.Equal(FieldKind.Name, prefab.Kind);
        Assert.True(prefab.HasPicker);
        Assert.Equal(["Bulet"], prefab.UnmatchedNames);
        Assert.Equal("There's no prefab called \"Bulet\" in the project", prefab.UnmatchedTip);
        Assert.Equal(FieldKind.Name, Field(inspector, "Shooter", "Action").Kind);
        Assert.False(Field(inspector, "Shooter", "Action").IsUnmatched);

        prefab.TextValue = "Bullet";
        Assert.False(Field(inspector, "Shooter", "Prefab").IsUnmatched);

        // A counter hears a message nothing sends, until a gem sends it.
        selection.Select(Named(document, "Score"));
        var message = Field(inspector, "Counter", "Message");
        Assert.Equal("Nothing sends \"Score\": no component's message, dialogue's [send] or script's Game.Send", message.UnmatchedTip);
        selection.Select(Named(document, "Gem"));
        Field(inspector, "Pickup", "Message").TextValue = "Score";
        selection.Select(Named(document, "Score"));
        Assert.False(Field(inspector, "Counter", "Message").IsUnmatched);
    });

    [Fact]
    public void Picker_ListsThePrefabs_TypingNarrowsIt_AndEnterPicksTheBest() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Turret"));
        var prefab = Field(inspector, "Shooter", "Prefab");

        prefab.IsPickerOpen = true;
        var picker = prefab.Picker!;
        Assert.Equal(["Bullet", "Zombie"], picker.Matches.Select(c => c.Name));
        Assert.Equal("Type to find a prefab", picker.Placeholder);
        Assert.Contains(picker.Actions, a => a.Label == "None");

        picker.Search = "zo";
        Assert.Equal(["Zombie"], picker.Matches.Select(c => c.Name));
        Assert.Equal("Zombie", picker.Selected?.Name);
        picker.Search = "xyz";
        Assert.Empty(picker.Matches);
        Assert.Equal("Nothing matches", picker.Note);
        picker.Search = "bul";
        picker.PickSelected();

        Assert.Equal("Bullet", Named(document, "Turret").GetComponent<Shooter>()!.Prefab);
        Assert.False(prefab.IsPickerOpen);
        document.History.Undo();
        Assert.Equal("Bulet", Named(document, "Turret").GetComponent<Shooter>()!.Prefab);
    });

    [Fact]
    public void Picker_OnAFieldNamingSeveralLayers_TicksThemInTurn_AndTakesANewOneAsTyped() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var pickup = Named(document, "Gem").GetComponent<Pickup>()!;
        selection.Select(pickup.Entity!);
        var layers = Field(inspector, "Pickup", "Layers");

        layers.IsPickerOpen = true;
        var picker = layers.Picker!;
        // Default, and the layers colliders here and in the prefabs are on.
        Assert.Equal(["Default", "Player", "Shot"], picker.Matches.Select(c => c.Name));
        Assert.True(picker.Matches.Single(c => c.Name == "Player").IsPicked);

        picker.Pick("Shot");
        Assert.Equal("Player, Shot", pickup.Layers);
        Assert.True(layers.IsPickerOpen);
        Assert.True(picker.Matches.Single(c => c.Name == "Shot").IsPicked);
        picker.Pick("player");
        Assert.Equal("Shot", pickup.Layers);

        picker.Search = "Ghost";
        Assert.Equal("Use \"Ghost\"", picker.TypedOffer);
        picker.PickSelected();
        Assert.Equal("Shot, Ghost", pickup.Layers);
        Assert.Equal(["Ghost"], Field(inspector, "Pickup", "Layers").UnmatchedNames);
    });

    [Fact]
    public void Picker_ForMessages_ListsWhatsSentAndHeard() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Score"));
        var message = Field(inspector, "Counter", "Message");

        message.IsPickerOpen = true;
        Assert.Equal(["Gems sent", "Score heard"], message.Picker!.Matches.Select(c => $"{c.Name} {c.Detail}"));
        message.Picker.Pick("Gems");
        Assert.Equal("Gems", Named(document, "Score").GetComponent<Counter>()!.Message);
        Assert.False(Field(inspector, "Counter", "Message").IsUnmatched);
    });

    [Fact]
    public void EntityLists_NarrowByTyping_AndNoneClearsAField() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var follow = Named(document, "Camera").GetComponent<Follow>()!;
        selection.Select(follow.Entity!);
        var target = Field(inspector, "Follow", "Target");

        target.IsPickerOpen = true;
        target.Picker!.Search = "pla";
        Assert.Equal("Player", target.Picker.Selected?.Name);
        target.Picker.PickSelected();
        Assert.Equal("Player", follow.Target);

        target.IsPickerOpen = true;
        target.Picker!.Do("None");
        Assert.Null(follow.Target);
        Assert.False(target.IsPickerOpen);
    });

    [Fact]
    public void APrefabDroppedFromTheAssetsPanel_IsNamed() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Turret"));
        var prefab = Field(inspector, "Shooter", "Prefab");

        Assert.True(prefab.TakesAsset("Prefabs/Zombie.prefab"));
        Assert.False(prefab.TakesAsset("Sprites/zombie.png"));
        Assert.False(Field(inspector, "Shooter", "Action").TakesAsset("Prefabs/Zombie.prefab"));
        prefab.DropAsset("Prefabs/Zombie.prefab");
        Assert.Equal("Zombie", Named(document, "Turret").GetComponent<Shooter>()!.Prefab);
    });

    [Fact]
    public void Agents_OpenAListSearchItAndPick() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Turret"));

        // What ui_set and ui_invoke run: the list opened and searched, then the best match picked.
        using var open = JsonDocument.Parse("true");
        Griff.Agent.ObjectSurface.Set(inspector, "Components[Shooter].Properties[Prefab].IsPickerOpen", open.RootElement);
        using var search = JsonDocument.Parse("\"zom\"");
        Griff.Agent.ObjectSurface.Set(inspector, "Components[Shooter].Properties[Prefab].Picker.Search", search.RootElement);
        var seen = Griff.Agent.ObjectSurface.Describe(inspector, "Components[Shooter].Properties[Prefab].Picker").ToJsonString();
        Assert.Contains("Zombie", seen);
        Griff.Agent.ObjectSurface.InvokeAsync(inspector, "Components[Shooter].Properties[Prefab].Picker.PickSelected", null).GetAwaiter().GetResult();

        Assert.Equal("Zombie", Named(document, "Turret").GetComponent<Shooter>()!.Prefab);
    });

    [Fact]
    public void Agents_InspectOneChoice() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Gem"));
        Field(inspector, "Pickup", "Layers").IsPickerOpen = true;

        var choice = Griff.Agent.ObjectSurface.Describe(inspector, "Components[Pickup].Properties[Layers].Picker.Matches[1]").ToJsonString();

        Assert.Contains("Player", choice);
        Assert.Contains("\"IsPicked\":true", choice.Replace(" ", ""));
    });
}
