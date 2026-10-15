using System.IO;
using System.Windows.Input;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Entities;

// Entity menu groups: 0 = create, 1 = edit selected.
public sealed class EntitiesModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddMenu(Menus.Entity, "E_ntity", order: 3);
        services
            .AddCommand<CreateEmptyCommand>()
            .AddCommand<CreateSpriteCommand>()
            .AddCommand<CreateCameraCommand>()
            .AddCommand<CreateTextCommand>()
            .AddCommand<CreateTilemapCommand>()
            .AddCommand<CreateParticlesCommand>()
            .AddCommand<CreateButtonCommand>()
            .AddCommand<CreateSliderCommand>()
            .AddCommand<CreateToggleCommand>()
            .AddCommand<CreateBarCommand>()
            .AddCommand<CreateDialogueBoxCommand>()
            .AddCommand<CreateSwitchCommand>()
            .AddCommand<CreatePanelCommand>()
            .AddCommand<CreateChoiceCommand>()
            .AddCommand<CreatePlatformerPlayerCommand>()
            .AddCommand<CreateTopDownPlayerCommand>()
            .AddCommand<CreateGridPlayerCommand>()
            .AddCommand<CreateGridEnemyCommand>()
            .AddCommand<CreateTurnOrderCommand>()
            .AddCommand<CreateLadderCommand>()
            .AddCommand<CreateSpringCommand>()
            .AddCommand<CreateCrateCommand>()
            .AddCommand<CreateLightCommand>()
            .AddCommand<CreateRoomCommand>()
            .AddCommand<CreateWeatherCommand>()
            .AddCommand<CreateHitboxCommand>()
            .AddCommand<RenameEntityCommand>()
            .AddCommand<DuplicateEntityCommand>()
            .AddCommand<DeleteEntityCommand>()
            .AddCommand<CopyEntityCommand>()
            .AddCommand<CutEntityCommand>()
            .AddCommand<PasteEntityCommand>()
            .AddCommand<SaveAsPrefabCommand>()
            .AddCommand<UnlinkPrefabCommand>()
            .AddCommand<RevertToPrefabCommand>()
            .AddCommand<LockEntityCommand>()
            .AddCommand<HideEntityCommand>()
            .AddCommand<FitPolygonColliderCommand>()
            .AddCommand<AddHingeCommand>()
            .AddCommand<AddRopeCommand>();

        // The modules' presets (Create Match Board, Create Tactics Unit) and actions (Generate Dungeon), each a command of its own.
        foreach (var preset in EntityPreset.All)
            services.AddSingleton<EditorCommand>(sp => new PresetCommand(preset, sp.GetRequiredService<IWorkspace>(), sp.GetRequiredService<ISelection>()));
        foreach (var action in EntityAction.All)
            services.AddSingleton<EditorCommand>(sp => new EntityActionCommand(action, sp.GetRequiredService<IWorkspace>(), sp.GetRequiredService<ISelection>()));
    }
}

/// <summary>
/// Creates what a module's <see cref="EntityPreset"/> makes, from the Entity menu: a command standing for the preset, with
/// its id, text and description (the preset's /// summary).
/// </summary>
public sealed class PresetCommand(EntityPreset preset, IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public EntityPreset Preset => preset;

    public override string Id => preset.Id;

    public override string Text => preset.Text;

    public override string? Icon => Icons.Add;

    public override Type DescribedBy => preset.GetType();

    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: preset.Order);

    protected override string EntityName => Scene is { } scene ? preset.EntityName(scene) : "Entity";

    protected override void Configure(Entity entity)
    {
        if (Scene is { } scene)
            preset.Configure(entity, scene);
    }

    protected override IEnumerable<Entity> CreateChildren() => Scene is { } scene ? preset.CreateChildren(scene) : [];

    protected override IEnumerable<IEdit> EditScene(Scene scene) => preset.EditScene(scene);

    protected override Entity? InsideOf(Entity? selected) => preset.InsideOf(selected);
}

/// <summary>
/// Does what a module's <see cref="EntityAction"/> does to the selected entity, from the Entity menu: a command standing for
/// the action, with its id, text, icon and description (the action's /// summary), its edits one undo step.
/// </summary>
public sealed class EntityActionCommand(EntityAction action, IWorkspace workspace, ISelection selection) : EditorCommand
{
    public EntityAction Action => action;

    public override string Id => action.Id;

    public override string Text => action.Text;

    public override string? Icon => action.Icon is { } name ? typeof(Icons).GetField(name)?.GetValue(null) as string : null;

    public override Type DescribedBy => action.GetType();

    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: action.Order);

    protected override bool CanExecuteCore() => workspace.Scene is not null && selection.Entity is { } entity && action.AppliesTo(entity);

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is not { } document || selection.Entity is not { } entity || !action.AppliesTo(entity))
            throw new InvalidOperationException(action.NotApplicable);

        using (document.History.BeginGroup(action.Text.Replace("_", "")))
        {
            foreach (var edit in action.Edits(entity, document.Scene, workspace.Components))
                document.Execute(edit);
        }

        document.History.Seal();
        return Task.CompletedTask;
    }
}

/// <summary>Creates an entity as a child of the selection (or at the root) and selects it.</summary>
public abstract class CreateEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    protected abstract string EntityName { get; }

    protected virtual void Configure(Entity entity)
    {
    }

    /// <summary>Entities to put inside the new one, e.g. an enemy's hitbox. Added in the same undo step.</summary>
    protected virtual IEnumerable<Entity> CreateChildren() => [];

    /// <summary>Changes to what's already in the scene that go with the new entity. Made in the same undo step.</summary>
    protected virtual IEnumerable<IEdit> EditScene(Scene scene) => [];

    /// <summary>What the new entity goes inside, given what's selected: the selection itself, unless overridden.</summary>
    protected virtual Entity? InsideOf(Entity? selected) => selected;

    /// <summary>The scene it's created in.</summary>
    protected Scene? Scene => workspace.Scene?.Scene;

    /// <summary>
    /// A draw order for a menu control: at least <paramref name="order"/>, and above anything drawn by the entity it's
    /// created inside (a button made on a panel draws over the panel's backdrop).
    /// </summary>
    protected int OrderAbove(int order)
    {
        for (var e = selection.Entity; e is not null; e = e.Parent)
        {
            if (Griff.Rendering.SceneRenderer.OrderOf(e) is { } under)
                order = Math.Max(order, under + 10);
        }

        return order;
    }

    protected override bool CanExecuteCore() => workspace.Scene is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is not { } document)
            return Task.CompletedTask;

        var entity = new Entity(EntityName);
        Configure(entity);
        var children = CreateChildren().ToList();
        var edits = EditScene(document.Scene).ToList();
        using (children.Count + edits.Count > 0 ? document.History.BeginGroup($"Add {entity.Name}") : null)
        {
            document.Execute(new AddEntityEdit(document.Scene, entity, document.ParentFor(InsideOf(selection.Entity))));
            foreach (var child in children)
                document.Execute(new AddEntityEdit(document.Scene, child, entity));
            foreach (var edit in edits)
                document.Execute(edit);
        }

        selection.Select(entity);
        return Task.CompletedTask;
    }
}

/// <summary>Creates an empty entity (just a transform) inside the selected entity, or at the top level, and selects it.</summary>
public sealed class CreateEmptyCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateEmpty";
    public override string Text => "Create _Empty";
    public override string? Icon => Icons.Add;
    public override KeyGesture? Gesture { get; } = new(Key.N, ModifierKeys.Control | ModifierKeys.Alt);
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 0);
    protected override string EntityName => "Entity";
}

/// <summary>Creates an entity with a Sprite (a white box until it's given an Image) and selects it.</summary>
public sealed class CreateSpriteCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateSprite";
    public override string Text => "Create _Sprite";
    public override string? Icon => Icons.Image;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 1);
    protected override string EntityName => "Sprite";
    protected override void Configure(Entity entity) => entity.AddComponent<Sprite>();
}

/// <summary>Creates a camera: what the game is seen through.</summary>
public sealed class CreateCameraCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateCamera";
    public override string Text => "Create _Camera";
    public override string? Icon => Icons.Camera;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 2);
    protected override string EntityName => "Camera";
    protected override void Configure(Entity entity) => entity.AddComponent<Camera>();
}

/// <summary>Creates an entity with a Text: words in the world, or on the HUD with a ScreenAnchor.</summary>
public sealed class CreateTextCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateText";
    public override string Text => "Create _Text";
    public override string? Icon => Icons.Font;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 3);
    protected override string EntityName => "Text";
    protected override void Configure(Entity entity) => entity.AddComponent<Griff.Scenes.Text>().Order = OrderAbove(0);
}

/// <summary>Creates an empty tilemap; selecting it puts the Scene view into tile painting.</summary>
public sealed class CreateTilemapCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateTilemap";
    public override string Text => "Create Tile_map";
    public override string? Icon => Icons.Palette;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 4);
    protected override string EntityName => "Tilemap";
    protected override void Configure(Entity entity) => entity.AddComponent<Tilemap>().Tint = Griff.Color.FromBytes(0x48, 0x4B, 0x55);
}

/// <summary>Creates a particle emitter: sparks, smoke, dust, magic.</summary>
public sealed class CreateParticlesCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateParticles";
    public override string Text => "Create Pa_rticles";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 5);
    protected override string EntityName => "Particles";
    protected override void Configure(Entity entity) => entity.AddComponent<ParticleEmitter>();
}

/// <summary>Creates a clickable button: a background sprite, a label and a Button set to do nothing until configured.</summary>
public sealed class CreateButtonCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateButton";
    public override string Text => "Create _Button";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 6);
    protected override string EntityName => "Button";

    protected override void Configure(Entity entity)
    {
        var size = new System.Numerics.Vector2(3, 1);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Griff.Color.FromBytes(0x3A, 0x3F, 0x4B);
        sprite.Order = OrderAbove(100);
        var label = entity.AddComponent<Griff.Scenes.Text>();
        label.Value = "Button";
        label.Size = 0.5f;
        entity.AddComponent<Griff.Scenes.Button>().Size = size;
    }
}

/// <summary>Creates a slider for an options menu; set its Setting to a volume and it needs no code.</summary>
public sealed class CreateSliderCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateSlider";
    public override string Text => "Create Sl_ider";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 7);
    protected override string EntityName => "Slider";
    protected override void Configure(Entity entity) => entity.AddComponent<Slider>().Order = OrderAbove(100);
}

/// <summary>Creates an on/off switch: a button whose label says which way it's set.</summary>
public sealed class CreateToggleCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateToggle";
    public override string Text => "Create T_oggle";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 8);
    protected override string EntityName => "Toggle";

    protected override void Configure(Entity entity)
    {
        var size = new System.Numerics.Vector2(4, 1);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Griff.Color.FromBytes(0x3A, 0x3F, 0x4B);
        sprite.Order = OrderAbove(100);
        var label = entity.AddComponent<Griff.Scenes.Text>();
        label.Value = "Option: On";
        label.Size = 0.5f;
        entity.AddComponent<Griff.Scenes.Button>().Size = size;
        var toggle = entity.AddComponent<Toggle>();
        toggle.OnText = "Option: On";
        toggle.OffText = "Option: Off";
    }
}

/// <summary>Creates a bar for a HUD; set its Track to show an entity's health.</summary>
public sealed class CreateBarCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateBar";
    public override string Text => "Create B_ar";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 9);
    protected override string EntityName => "Bar";
    protected override void Configure(Entity entity) => entity.AddComponent<Bar>();
}

/// <summary>
/// Creates the box a <see cref="Dialogue"/> talks in: a panel pinned to the bottom of the screen whose text types itself
/// out, with a name tag and a portrait above it for whoever is talking (see <see cref="Speaker"/>). It's hidden while the
/// game runs until someone talks.
/// </summary>
public sealed class CreateDialogueBoxCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateDialogueBox";
    public override string Text => "Create _Dialogue Box";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 10);
    protected override string EntityName => "Dialogue Box";

    protected override void Configure(Entity entity)
    {
        var anchor = entity.AddComponent<ScreenAnchor>();
        anchor.Anchor = Anchor.Bottom;
        anchor.Offset = new System.Numerics.Vector2(0, 1.4f);
        var panel = entity.AddComponent<Sprite>();
        panel.Size = new System.Numerics.Vector2(14, 2.2f);
        panel.Tint = Griff.Color.FromBytes(0x16, 0x19, 0x20) with { A = 0.9f };
        panel.Order = 200;
        var text = entity.AddComponent<Griff.Scenes.Text>();
        text.Value = "...";
        text.Size = 0.5f;
        text.Order = 201;
        entity.AddComponent<Typewriter>();
        entity.AddComponent<DialogueBox>();
    }

    // A portrait standing on the box's top-left corner, and the speaker's name in a tag beside it.
    protected override IEnumerable<Entity> CreateChildren()
    {
        var portrait = new Entity("Portrait");
        portrait.Transform.Position = new System.Numerics.Vector3(-5.8f, 2.15f, 0);
        var picture = portrait.AddComponent<Sprite>();
        (picture.Size, picture.Order) = (new System.Numerics.Vector2(2, 2), 201);
        yield return portrait;

        var tag = new Entity("Name Tag");
        tag.Transform.Position = new System.Numerics.Vector3(-3.1f, 1.45f, 0);
        var backdrop = tag.AddComponent<Sprite>();
        (backdrop.Size, backdrop.Tint, backdrop.Order) = (new System.Numerics.Vector2(3.2f, 0.7f), Griff.Color.FromBytes(0x16, 0x19, 0x20) with { A = 0.9f }, 200);
        var name = tag.AddComponent<Griff.Scenes.Text>();
        (name.Value, name.Size, name.Color, name.Order) = ("Name", 0.4f, Griff.Color.FromBytes(0xE3, 0xB3, 0x41), 201);
        yield return tag;
    }
}

/// <summary>
/// Creates a lever: a small sprite with a trigger around it and a <see cref="Switch"/>. Name what it opens in its Targets
/// (or Hides), or set its Mode to make it a pressure plate or a button.
/// </summary>
public sealed class CreateSwitchCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateSwitch";
    public override string Text => "Create S_witch";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 11);
    protected override string EntityName => "Switch";

    protected override void Configure(Entity entity)
    {
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = new System.Numerics.Vector2(0.3f, 0.8f);
        sprite.Tint = Griff.Color.FromBytes(0xC2, 0x4E, 0x4E);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = new System.Numerics.Vector2(1.2f, 1.2f);
        collider.IsTrigger = true;
        entity.AddComponent<Switch>();
    }
}

/// <summary>
/// Creates a panel: a backdrop pinned to the middle of the screen with a <see cref="Panel"/>, hidden while the game runs
/// until it's opened. Select it and create buttons to put them on it; set its Open On to "Pause" for a pause menu.
/// </summary>
public sealed class CreatePanelCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreatePanel";
    public override string Text => "Create Pa_nel";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 12);
    protected override string EntityName => "Panel";

    protected override void Configure(Entity entity)
    {
        entity.Transform.Position = new System.Numerics.Vector3(0, 0, 3);
        entity.AddComponent<ScreenAnchor>().Anchor = Anchor.Center;
        var backdrop = entity.AddComponent<Sprite>();
        backdrop.Size = new System.Numerics.Vector2(7, 5);
        backdrop.Tint = Griff.Color.FromBytes(0x16, 0x19, 0x20) with { A = 0.92f };
        backdrop.Order = 300;
        entity.AddComponent<Panel>();
    }
}

/// <summary>Creates a button that picks one of several options, changed with left and right: "Difficulty: Normal".</summary>
public sealed class CreateChoiceCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateChoice";
    public override string Text => "Create C_hoice";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 13);
    protected override string EntityName => "Choice";

    protected override void Configure(Entity entity)
    {
        var size = new System.Numerics.Vector2(4, 1);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Griff.Color.FromBytes(0x3A, 0x3F, 0x4B);
        sprite.Order = OrderAbove(100);
        var label = entity.AddComponent<Griff.Scenes.Text>();
        label.Value = "< Normal >";
        label.Size = 0.5f;
        entity.AddComponent<Griff.Scenes.Button>().Size = size;
        entity.AddComponent<Choice>().Format = "< {0} >";
    }
}

/// <summary>
/// Creates a platformer's player that runs and jumps with no code: a box sprite with a collider on the Player layer, a
/// <see cref="Body"/> and a <see cref="PlatformerMovement"/>. Its Inspector tunes how it moves.
/// </summary>
public sealed class CreatePlatformerPlayerCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreatePlatformerPlayer";
    public override string Text => "Create _Platformer Player";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 14);
    protected override string EntityName => "Player";

    protected override void Configure(Entity entity)
    {
        AddPlayerShape(entity, new System.Numerics.Vector2(0.8f, 1));
        entity.AddComponent<Body>();
        entity.AddComponent<PlatformerMovement>();
    }

    internal static void AddPlayerShape(Entity entity, System.Numerics.Vector2 size)
    {
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Griff.Color.FromBytes(0xF0, 0x8C, 0x2E);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.Layer = "Player";
    }
}

/// <summary>Creates a top-down player that moves in eight directions with no code, ignoring gravity.</summary>
public sealed class CreateTopDownPlayerCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateTopDownPlayer";
    public override string Text => "Create Top-down Pla_yer";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 15);
    protected override string EntityName => "Player";

    protected override void Configure(Entity entity)
    {
        CreatePlatformerPlayerCommand.AddPlayerShape(entity, new System.Numerics.Vector2(0.8f, 0.8f));
        entity.AddComponent<Body>().GravityScale = 0;
        entity.AddComponent<TopDownMovement>();
    }
}

/// <summary>
/// Creates a player that steps tile by tile on the scene's tilemap with no code, blocked by its walls: a box sprite with
/// a collider on the Player layer, a <see cref="Body"/> without gravity and a <see cref="GridMovement"/>.
/// </summary>
public sealed class CreateGridPlayerCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateGridPlayer";
    public override string Text => "Create _Grid Player";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 16);
    protected override string EntityName => "Player";

    protected override void Configure(Entity entity)
    {
        CreatePlatformerPlayerCommand.AddPlayerShape(entity, new System.Numerics.Vector2(0.8f, 0.8f));
        entity.AddComponent<Body>().GravityScale = 0;
        entity.AddComponent<GridMovement>();
        CreateTurnOrderCommand.JoinTurns(Scene, entity);
    }
}

/// <summary>
/// Creates an enemy that steps tile by tile after the player with no code, around walls: a box sprite with a collider on
/// the Enemy layer, a <see cref="Body"/> without gravity, a <see cref="GridMovement"/> a little slower than the player's,
/// a <see cref="GridChase"/>, and an <see cref="Attack"/> whose "Hitbox" child hurts the Player layer once it's next to
/// the player. Add a <see cref="GridPatrol"/> to have it walk a route until it spots the player.
/// </summary>
public sealed class CreateGridEnemyCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateGridEnemy";
    public override string Text => "Create Grid _Enemy";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 16);
    protected override string EntityName => "Enemy";

    protected override void Configure(Entity entity)
    {
        var size = new System.Numerics.Vector2(0.8f, 0.8f);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Griff.Color.FromBytes(0x57, 0xAB, 0x5A);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.Layer = "Enemy";
        entity.AddComponent<Body>().GravityScale = 0;
        entity.AddComponent<GridMovement>().StepTime = 0.3f;
        entity.AddComponent<GridChase>();

        // It swings at the player next to it (not diagonally), with a wind-up to see it coming.
        var attack = entity.AddComponent<Attack>();
        (attack.Action, attack.Target, attack.Range, attack.Windup, attack.Cooldown) = ("", "Player", 1.1f, 0.3f, 0.6f);
        CreateTurnOrderCommand.JoinTurns(Scene, entity);
    }

    protected override IEnumerable<Entity> CreateChildren()
    {
        var hitbox = new Entity("Hitbox");
        hitbox.Transform.Position = new System.Numerics.Vector3(0.9f, 0, 0);
        var box = hitbox.AddComponent<BoxCollider>();
        (box.Size, box.IsTrigger, box.Layer) = (new System.Numerics.Vector2(0.8f, 0.6f), true, "Attack");
        hitbox.AddComponent<DamageOnTouch>().Layers = "Player";
        yield return hitbox;
    }
}

/// <summary>
/// Creates turn-based play: a <see cref="TurnOrder"/> whose text in the top-right corner says whose turn it is, with an
/// End Turn button in the bottom-right corner. Give whatever takes turns a <see cref="TurnTaker"/> naming its side. In a
/// scene with grid movers it sets up a roguelike instead, where everything moves when the player moves: each grid mover
/// gets a turn taker with one action (players on the Player side, chasers and patrols on the Enemy side), turns follow
/// straight on, the text counts turns and the button waits a turn. Grid players and enemies created later join in.
/// </summary>
public sealed class CreateTurnOrderCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateTurnOrder";
    public override string Text => "Create T_urn Order";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 16);
    protected override string EntityName => "Turns";

    private bool Grid => Scene is { } scene && GridMovers(scene).Any();

    protected override void Configure(Entity entity)
    {
        var anchor = entity.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.TopRight, new System.Numerics.Vector2(-0.6f, -0.7f));
        var text = entity.AddComponent<Griff.Scenes.Text>();
        (text.Value, text.Size, text.Align, text.Order) = ("Player's turn", 0.6f, TextAlign.Right, OrderAbove(150));
        var order = entity.AddComponent<TurnOrder>();
        if (Grid)
        {
            (order.Between, order.Format) = (0, "Turn {1}");
            text.Value = "Turn 1";
        }
    }

    protected override IEnumerable<Entity> CreateChildren()
    {
        var size = new System.Numerics.Vector2(3, 0.9f);
        var end = new Entity("End Turn");
        var anchor = end.AddComponent<ScreenAnchor>();
        (anchor.Anchor, anchor.Offset) = (Anchor.BottomRight, new System.Numerics.Vector2(-1.9f, 0.8f));
        var sprite = end.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint, sprite.Order) = (size, Griff.Color.FromBytes(0x3A, 0x3F, 0x4B), OrderAbove(150));
        var label = end.AddComponent<Griff.Scenes.Text>();
        (label.Value, label.Size, label.Order) = (Grid ? "Wait" : "End Turn", 0.45f, OrderAbove(150) + 1);
        var button = end.AddComponent<Griff.Scenes.Button>();
        (button.Size, button.Action, button.Navigable) = (size, ButtonAction.EndTurn, false);
        yield return end;
    }

    // The grid movers that don't take turns yet join in, a step a turn.
    protected override IEnumerable<IEdit> EditScene(Scene scene) =>
        GridMovers(scene).Where(e => TurnTaker.Of(e) is null).Select(e => new AddComponentEdit(e, GridTurnTaker(e, null))).ToList();

    /// <summary>
    /// Gives a grid player or enemy being created a turn taker with one action, if its scene already has a turn order, so
    /// it takes turns with everything else.
    /// </summary>
    internal static void JoinTurns(Scene? scene, Entity entity)
    {
        if (scene?.Entities.Select(e => e.GetComponent<TurnOrder>()).FirstOrDefault(o => o is not null) is { } order)
            entity.AddComponent(GridTurnTaker(entity, order));
    }

    // Grid players, enemies and other movers: not the crates they push, which don't take turns, nor those whose walk is
    // all one go (tactics units), which take theirs side by side.
    internal static IEnumerable<Entity> GridMovers(Scene scene) =>
        scene.Entities.Where(e => e.GetComponent<GridMovement>() is not null && e.GetComponent<Pushable>() is null &&
                                  !e.Components.OfType<IGridDriver>().Any(d => !d.StepTakesAction));

    // One action a turn, on the first side a person plays if it's a player, or the first the computer plays if it moves on
    // its own.
    private static TurnTaker GridTurnTaker(Entity entity, TurnOrder? order)
    {
        var driven = entity.GetComponent<GridChase>() is not null || entity.GetComponent<GridPatrol>() is not null;
        var sides = order?.SideList ?? [];
        var side = driven
            ? sides.FirstOrDefault(s => order!.IsComputer(s)) ?? "Enemy"
            : sides.FirstOrDefault(s => !order!.IsComputer(s)) ?? "Player";
        return new TurnTaker { Side = side, Actions = 1 };
    }
}

/// <summary>Creates a ladder: a tall thin sprite behind things, with a trigger to climb. Stretch it with its Scale.</summary>
public sealed class CreateLadderCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateLadder";
    public override string Text => "Create _Ladder";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 17);
    protected override string EntityName => "Ladder";

    protected override void Configure(Entity entity)
    {
        var size = new System.Numerics.Vector2(0.8f, 4);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Griff.Color.FromBytes(0x9C, 0x6B, 0x3C);
        sprite.Order = -1;
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.IsTrigger = true;
        entity.AddComponent<Ladder>();
    }
}

/// <summary>Creates a spring: a low solid block with a <see cref="Bouncer"/> that sends whatever lands on it flying.</summary>
public sealed class CreateSpringCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateSpring";
    public override string Text => "Create Sprin_g";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 18);
    protected override string EntityName => "Spring";

    protected override void Configure(Entity entity)
    {
        var size = new System.Numerics.Vector2(1.2f, 0.5f);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        sprite.Tint = Griff.Color.FromBytes(0x4C, 0x9A, 0xFF);
        entity.AddComponent<BoxCollider>().Size = size;
        entity.AddComponent<Bouncer>();
    }
}

/// <summary>
/// Creates a crate the player pushes along: a solid box with a <see cref="Body"/> and <see cref="Pushable"/>, on the Crate
/// layer. In a scene with grid movers it's a grid crate, pushed a cell at a time: it gets a <see cref="GridMovement"/> and
/// no gravity.
/// </summary>
public sealed class CreateCrateCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateCrate";
    public override string Text => "Create Cr_ate";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 19);
    protected override string EntityName => "Crate";

    protected override void Configure(Entity entity)
    {
        var sprite = entity.AddComponent<Sprite>();
        sprite.Tint = Griff.Color.FromBytes(0x9C, 0x6B, 0x3C);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Layer = "Crate";
        var body = entity.AddComponent<Body>();
        entity.AddComponent<Pushable>();
        if (Scene is { } scene && CreateTurnOrderCommand.GridMovers(scene).Any())
        {
            (sprite.Size, collider.Size) = (new System.Numerics.Vector2(0.9f, 0.9f), new System.Numerics.Vector2(0.9f, 0.9f));
            body.GravityScale = 0;
            entity.AddComponent<GridMovement>();
        }
    }
}

/// <summary>Creates a light: an entity with a warm <see cref="Light"/>. It shows once the camera's Ambient is dark.</summary>
public sealed class CreateLightCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateLight";
    public override string Text => "Create Lig_ht";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 22);
    protected override string EntityName => "Light";
    protected override void Configure(Entity entity) => entity.AddComponent<Light>();
}

/// <summary>Creates weather: an entity with a <see cref="Weather"/>, rain to start with, that fills the view while the game runs.</summary>
public sealed class CreateWeatherCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateWeather";
    public override string Text => "Create Weather";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 25);
    protected override string EntityName => "Weather";
    protected override void Configure(Entity entity) => entity.AddComponent<Weather>();
}

/// <summary>Creates a room one camera view big, for a camera with a Follow to keep to and slide between.</summary>
public sealed class CreateRoomCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    private readonly IWorkspace _workspace = workspace;

    public override string Id => "Entity.CreateRoom";
    public override string Text => "Create Roo_m";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 23);
    protected override string EntityName => "Room";

    protected override void Configure(Entity entity)
    {
        var size = _workspace.Scene?.Scene.Entities.Select(e => e.GetComponent<Camera>()).OfType<Camera>().FirstOrDefault()?.Size
                   ?? Griff.Rendering.SceneRenderer.DefaultCameraSize;
        entity.AddComponent<Room>().Size = new System.Numerics.Vector2(MathF.Ceiling(size * 2 * 16 / 9), size * 2);
    }
}

/// <summary>
/// Gives the selected entity a sword: a "Hitbox" child to its right (a trigger on the Attack layer with a
/// <see cref="DamageOnTouch"/> that hurts enemies) and an <see cref="Attack"/> that swings it out with the Attack action.
/// One undo step.
/// </summary>
public sealed class CreateHitboxCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.CreateHitbox";
    public override string Text => "Add Atta_ck";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 24);

    protected override bool CanExecuteCore() => workspace.Scene is not null && selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is not { } document || selection.Entity is not { } owner)
            return Task.CompletedTask;

        var hitbox = new Entity("Hitbox");
        hitbox.Transform.Position = new System.Numerics.Vector3(0.9f, 0, 0);
        var collider = hitbox.AddComponent<BoxCollider>();
        collider.Size = new System.Numerics.Vector2(1, 0.8f);
        collider.IsTrigger = true;
        collider.Layer = "Attack";
        var damage = hitbox.AddComponent<DamageOnTouch>();
        damage.Layers = "Enemy";
        damage.Knockback = 6;

        using (document.History.BeginGroup($"Add Attack to {owner.Name}"))
        {
            document.Execute(new AddEntityEdit(document.Scene, hitbox, owner));
            if (owner.GetComponent<Attack>() is null)
                document.Execute(new AddComponentEdit(owner, new Attack()));
        }

        selection.Select(hitbox);
        return Task.CompletedTask;
    }
}

/// <summary>Starts renaming the selected entity in the Hierarchy (agents: set its name with entity_update instead).</summary>
public sealed class RenameEntityCommand(ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Rename";
    public override string Text => "_Rename";
    public override KeyGesture? Gesture { get; } = new(Key.F2);
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 0);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        selection.RequestRename();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Copies the selected entities (and their children), each right after its original, and selects the copies. Several
/// selected entities are duplicated as one undo step.
/// </summary>
public sealed class DuplicateEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Duplicate";
    public override string Text => "_Duplicate";
    public override string? Icon => Icons.Copy;
    public override KeyGesture? Gesture { get; } = new(Key.D, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 1);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        var originals = selection.TopLevel();
        if (workspace.Scene is not { } document || originals.Count == 0)
            return Task.CompletedTask;

        var scene = document.Scene;
        var copies = originals.Select(workspace.Serializer.Clone).ToList();
        // Insert from the bottom up, so each copy's index is still right when it goes in.
        var edits = originals.Select((original, i) => (Original: original, Copy: copies[i], Index: scene.IndexOf(original) + 1))
            .Reverse()
            .Select(x => (IEdit)new AddEntityEdit(scene, x.Copy, x.Original.Parent, x.Index))
            .ToList();
        document.Execute(EntityEdits.Group(edits, "Duplicate", originals));
        selection.SelectEntities(copies);
        return Task.CompletedTask;
    }
}

/// <summary>Edits to several selected entities at once, as one undo step.</summary>
internal static class EntityEdits
{
    /// <summary>One entity's edit as it is; several as one step named like "Delete 3 entities".</summary>
    public static IEdit Group(IReadOnlyList<IEdit> edits, string verb, IReadOnlyList<Entity> entities) =>
        edits.Count == 1 ? edits[0] : new CompositeEdit($"{verb} {entities.Count} entities", edits);

    /// <summary>Removes entities (in scene order) from the bottom up, so each one's place is still right on undo.</summary>
    public static IEdit Remove(Scene scene, IReadOnlyList<Entity> entities, string verb) =>
        Group(entities.Reverse().Select(e => (IEdit)new RemoveEntityEdit(scene, e)).ToList(), verb, entities);

    /// <summary>Locks or unlocks the entities that aren't that way already, as one undo step.</summary>
    public static void SetLocked(SceneDocument document, IEnumerable<Entity> entities, bool locked)
    {
        if (entities.Where(e => e.IsLocked != locked).ToList() is { Count: > 0 } changing)
            document.Execute(new SetEntitiesLockedEdit(changing, locked));
    }

    /// <summary>Hides or shows the entities that aren't that way already, as one undo step.</summary>
    public static void SetHidden(SceneDocument document, IEnumerable<Entity> entities, bool hidden)
    {
        if (entities.Where(e => e.IsHidden != hidden).ToList() is { Count: > 0 } changing)
            document.Execute(new SetEntitiesHiddenEdit(changing, hidden));
    }
}

/// <summary>Entities on the clipboard are scene JSON, so they paste across scenes and even projects.</summary>
public static class EntityClipboard
{
    /// <summary>The clipboard format entities are copied in (they're put there as text too).</summary>
    public const string Format = "Griff.Entity";
}

/// <summary>Copies the selected entities (and their children) to the clipboard.</summary>
public sealed class CopyEntityCommand(IWorkspace workspace, ISelection selection, IClipboard clipboard) : EditorCommand
{
    public override string Id => "Entity.Copy";
    public override string Text => "_Copy";
    public override string? Icon => Icons.Copy;
    public override KeyGesture? Gesture { get; } = new(Key.C, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 1, Order: 1);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (selection.TopLevel() is { Count: > 0 } entities)
            clipboard.Set(EntityClipboard.Format, workspace.Serializer.SerializeEntities(entities));
        return Task.CompletedTask;
    }
}

/// <summary>Copies the selected entities (and their children) to the clipboard and deletes them, as one undo step.</summary>
[Destructive]
public sealed class CutEntityCommand(IWorkspace workspace, ISelection selection, IClipboard clipboard) : EditorCommand
{
    public override string Id => "Entity.Cut";
    public override string Text => "Cu_t";
    public override KeyGesture? Gesture { get; } = new(Key.X, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 1, Order: 0);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is { } document && selection.TopLevel() is { Count: > 0 } entities)
        {
            clipboard.Set(EntityClipboard.Format, workspace.Serializer.SerializeEntities(entities));
            document.Execute(EntityEdits.Remove(document.Scene, entities, "Cut"));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Pastes what's on the clipboard after the selected entity (as its siblings), or at the end of the scene, and selects
/// it. Several entities paste in their order, as one undo step.
/// </summary>
public sealed class PasteEntityCommand(IWorkspace workspace, ISelection selection, IClipboard clipboard) : EditorCommand
{
    public override string Id => "Entity.Paste";
    public override string Text => "_Paste";
    public override KeyGesture? Gesture { get; } = new(Key.V, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 1, Order: 2);

    protected override bool CanExecuteCore() => workspace.Scene is not null && clipboard.Get(EntityClipboard.Format) is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is not { } document || clipboard.Get(EntityClipboard.Format) is not { } json)
            return Task.CompletedTask;

        var entities = workspace.Serializer.DeserializeEntities(json);
        var anchor = selection.Entity;
        var parent = anchor?.Parent;
        var index = anchor is null ? (int?)null : document.Scene.IndexOf(anchor) + 1;

        // In a prefab's tab, what would go at the top goes inside the prefab, at the end.
        if (parent is null && document.ParentFor(null) is { } inside)
            (parent, index) = (inside, null);
        var edits = entities.Select((e, i) => (IEdit)new AddEntityEdit(document.Scene, e, parent, index + i)).ToList();
        document.Execute(EntityEdits.Group(edits, "Paste", entities));
        selection.SelectEntities(entities);
        return Task.CompletedTask;
    }
}

/// <summary>Deletes the selected entities and everything inside them, as one undo step.</summary>
[Destructive]
public sealed class DeleteEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Delete";
    public override string Text => "De_lete";
    public override string? Icon => Icons.Delete;
    public override KeyGesture? Gesture { get; } = new(Key.Delete);
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 2);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is { } document && selection.TopLevel() is { Count: > 0 } entities)
            document.Execute(EntityEdits.Remove(document.Scene, entities, "Delete"));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Locks the selected entities in the Scene view, so clicks and box selections pass through them (and what's inside
/// them) to what's behind: a backdrop or a big tilemap stops catching every drag. Unlocks them if they're all locked
/// already. The Hierarchy still selects locked entities, and the game ignores locking. One undo step.
/// </summary>
public sealed class LockEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Lock";
    public override string Text => "Loc_k";
    public override string? Icon => Icons.Lock;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 7);

    public override bool? IsChecked => selection.Entities.Count > 0 && selection.Entities.All(e => e.IsLocked);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is { } document && selection.Entities.Count > 0)
            EntityEdits.SetLocked(document, selection.Entities, IsChecked != true);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Hides the selected entities in the Scene view, so a pause menu, a dialogue box or a tilemap layer stops covering
/// the level while you work on it: they (and what's inside them) aren't drawn, clicked or box selected there. Shows
/// them again if they're all hidden already. The Hierarchy still selects hidden entities, and the game shows them as
/// usual. One undo step.
/// </summary>
public sealed class HideEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Hide";
    public override string Text => "_Hide";
    public override string? Icon => Icons.Hide;

    // H is handled by the Scene view and the Hierarchy, so typing an H elsewhere doesn't hide anything.
    public override string? GestureText => "H";
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 8);

    public override bool? IsChecked => selection.Entities.Count > 0 && selection.Entities.All(e => e.IsHidden);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is { } document && selection.Entities.Count > 0)
            EntityEdits.SetHidden(document, selection.Entities, IsChecked != true);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Saves the selected entity and its children as a prefab, ready for <c>Game.Spawn("name")</c> or dragging into a
/// scene: a placed copy over its own prefab (applying everything it changed), anything else as
/// Assets/Prefabs/&lt;name&gt;.prefab. The entity becomes a copy of the prefab, and every other copy of it, in the open
/// scene (one undo step) and in every other scene, is brought up to date: it takes the prefab's components, scale and
/// children, keeping its own name and place and whatever it changed from the prefab. Works in play mode too, to keep a
/// tweaked live entity.
/// </summary>
public sealed class SaveAsPrefabCommand(IWorkspace workspace, ISelection selection, IDialogService dialogs, IStatusService status) : EditorCommand
{
    public override string Id => "Entity.SaveAsPrefab";
    public override string Text => "Save as _Prefab";
    public override string? Icon => Icons.Package;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 3);

    protected override bool CanExecuteCore() => selection.Entity is not null && workspace.Project is not null;

    protected override async Task ExecuteCoreAsync()
    {
        if (selection.Entity is not { } entity || workspace.Project is not { } project)
            return;

        // In a prefab's tab, the entity at the top is the prefab: saving it is saving the tab.
        if (workspace.Scene is { IsPrefab: true } tab && tab.Scene.Roots is [var root, ..] && root == entity)
        {
            await workspace.SaveSceneAsync();
            return;
        }

        // A copy saves over its own prefab (wherever it is, whatever the copy is called).
        var linked = entity.Prefab is { } link ? project.FindPrefab(link) : null;
        var path = linked ?? project.DefaultPrefabPath(PrefabFiles.SafeName(entity.Name));
        var name = Path.GetFileNameWithoutExtension(path);
        if (File.Exists(path))
        {
            var replace = linked is not null
                ? dialogs.Confirm("Save Prefab", $"Every copy of the {name} prefab, in every scene, will be updated to match, keeping what each copy changed itself. Save \"{entity.Name}\" over it?")
                : dialogs.Confirm("Replace Prefab", $"{name}{Griff.Projects.Project.PrefabExtension} already exists, and every copy of it, in every scene, will be updated to match, keeping what each copy changed itself. Replace it with \"{entity.Name}\"?");
            if (!replace)
                return;
        }

        var json = workspace.Serializer.SavePrefabToString(entity);
        var oldJson = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialogs.ShowError("Couldn't save the prefab", ex.Message);
            return;
        }

        var update = PrefabFiles.UpdateCopies(workspace, PrefabFiles.AssetPathOf(project, path), json, oldJson, entity);
        if (update.Problems.Count > 0)
            dialogs.ShowError("Some copies weren't updated", string.Join("\n", update.Problems));
        status.Show($"Saved {project.GetRelativePath(path)}. {(update.Copies > 0 ? update.Summary : $"Spawn it with Game.Spawn(\"{name}\").")}");
    }
}

/// <summary>
/// Unlinks the selected copies from their prefab: they become ordinary entities, which saving the prefab no longer
/// changes. One undo step.
/// </summary>
public sealed class UnlinkPrefabCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.UnlinkPrefab";
    public override string Text => "_Unlink from Prefab";
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 5);

    protected override bool CanExecuteCore() => selection.Entities.Any(e => e.Prefab is not null);

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is { } document && selection.Entities.Where(e => e.Prefab is not null).ToList() is { Count: > 0 } copies)
            document.Execute(new SetEntitiesPrefabEdit(copies, null));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Reverts the selected copies to their prefab: every field they changed goes back to the prefab's value, and
/// components and children they added or took away are put back as the prefab has them. Each keeps its own name and
/// place. One undo step.
/// </summary>
public sealed class RevertToPrefabCommand(IWorkspace workspace, ISelection selection, IStatusService status) : EditorCommand
{
    public override string Id => "Entity.RevertToPrefab";
    public override string Text => "_Revert to Prefab";
    public override string? Icon => Icons.Undo;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 6);

    protected override bool CanExecuteCore() => selection.Entities.Any(e => e.Prefab is not null);

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is not { } document)
            return Task.CompletedTask;
        if (PrefabFiles.Revert(workspace, document.Scene, selection.Entities.Where(e => e.Prefab is not null).ToList()) is { } edit)
            document.Execute(edit);
        else
            status.Show("Nothing to revert: the selection is just like its prefab.");
        return Task.CompletedTask;
    }
}
