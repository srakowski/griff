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
            .AddCommand<CreateLadderCommand>()
            .AddCommand<CreateSpringCommand>()
            .AddCommand<CreateCrateCommand>()
            .AddCommand<CreateLightCommand>()
            .AddCommand<CreateRoomCommand>()
            .AddCommand<CreateHitboxCommand>()
            .AddCommand<RenameEntityCommand>()
            .AddCommand<DuplicateEntityCommand>()
            .AddCommand<DeleteEntityCommand>()
            .AddCommand<CopyEntityCommand>()
            .AddCommand<CutEntityCommand>()
            .AddCommand<PasteEntityCommand>()
            .AddCommand<SaveAsPrefabCommand>();
    }
}

/// <summary>Creates an entity as a child of the selection (or at the root) and selects it.</summary>
public abstract class CreateEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    protected abstract string EntityName { get; }

    protected virtual void Configure(Entity entity)
    {
    }

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
        document.Execute(new AddEntityEdit(document.Scene, entity, selection.Entity));
        selection.Select(entity);
        return Task.CompletedTask;
    }
}

public sealed class CreateEmptyCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateEmpty";
    public override string Text => "Create _Empty";
    public override string? Icon => Icons.Add;
    public override KeyGesture? Gesture { get; } = new(Key.N, ModifierKeys.Control | ModifierKeys.Alt);
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 0);
    protected override string EntityName => "Entity";
}

public sealed class CreateSpriteCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateSprite";
    public override string Text => "Create _Sprite";
    public override string? Icon => Icons.Image;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 1);
    protected override string EntityName => "Sprite";
    protected override void Configure(Entity entity) => entity.AddComponent<Sprite>();
}

public sealed class CreateCameraCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateCamera";
    public override string Text => "Create _Camera";
    public override string? Icon => Icons.Camera;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 2);
    protected override string EntityName => "Camera";
    protected override void Configure(Entity entity) => entity.AddComponent<Camera>();
}

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
/// out. It's hidden while the game runs until someone talks.
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

/// <summary>Creates a ladder: a tall thin sprite behind things, with a trigger to climb. Stretch it with its Scale.</summary>
public sealed class CreateLadderCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateLadder";
    public override string Text => "Create _Ladder";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 16);
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
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 17);
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

/// <summary>Creates a crate the player pushes along: a solid box with a <see cref="Body"/> and <see cref="Pushable"/>, on the Crate layer.</summary>
public sealed class CreateCrateCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateCrate";
    public override string Text => "Create Cr_ate";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 18);
    protected override string EntityName => "Crate";

    protected override void Configure(Entity entity)
    {
        var sprite = entity.AddComponent<Sprite>();
        sprite.Tint = Griff.Color.FromBytes(0x9C, 0x6B, 0x3C);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Layer = "Crate";
        entity.AddComponent<Body>();
        entity.AddComponent<Pushable>();
    }
}

/// <summary>Creates a light: an entity with a warm <see cref="Light"/>. It shows once the camera's Ambient is dark.</summary>
public sealed class CreateLightCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    public override string Id => "Entity.CreateLight";
    public override string Text => "Create Lig_ht";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 19);
    protected override string EntityName => "Light";
    protected override void Configure(Entity entity) => entity.AddComponent<Light>();
}

/// <summary>Creates a room one camera view big, for a camera with a Follow to keep to and slide between.</summary>
public sealed class CreateRoomCommand(IWorkspace workspace, ISelection selection) : CreateEntityCommand(workspace, selection)
{
    private readonly IWorkspace _workspace = workspace;

    public override string Id => "Entity.CreateRoom";
    public override string Text => "Create Roo_m";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 20);
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
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 0, Order: 21);

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

/// <summary>Copies the selected entity (and its children) right after the original, and selects the copy.</summary>
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
        if (workspace.Scene is not { } document || selection.Entity is not { } original)
            return Task.CompletedTask;

        var copy = workspace.Serializer.Clone(original);
        var index = document.Scene.IndexOf(original) + 1;
        document.Execute(new AddEntityEdit(document.Scene, copy, original.Parent, index));
        selection.Select(copy);
        return Task.CompletedTask;
    }
}

/// <summary>Entities on the clipboard are scene JSON, so they paste across scenes and even projects.</summary>
public static class EntityClipboard
{
    public const string Format = "Griff.Entity";

    public static void Put(string json)
    {
        var data = new System.Windows.DataObject();
        data.SetData(Format, json);
        data.SetText(json);
        try
        {
            System.Windows.Clipboard.SetDataObject(data, copy: true);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Clipboard busy; the copy is lost but nothing else is affected.
        }
    }

    // Another app can hold the clipboard open; treat that as "nothing to paste" rather than failing.
    public static string? Get()
    {
        try
        {
            return System.Windows.Clipboard.ContainsData(Format) ? System.Windows.Clipboard.GetData(Format) as string : null;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }
}

public sealed class CopyEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Copy";
    public override string Text => "_Copy";
    public override string? Icon => Icons.Copy;
    public override KeyGesture? Gesture { get; } = new(Key.C, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 1, Order: 1);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (selection.Entity is { } entity)
            EntityClipboard.Put(workspace.Serializer.SerializeEntity(entity));
        return Task.CompletedTask;
    }
}

public sealed class CutEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Cut";
    public override string Text => "Cu_t";
    public override KeyGesture? Gesture { get; } = new(Key.X, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 1, Order: 0);

    protected override bool CanExecuteCore() => selection.Entity is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is { } document && selection.Entity is { } entity)
        {
            EntityClipboard.Put(workspace.Serializer.SerializeEntity(entity));
            document.Execute(new RemoveEntityEdit(document.Scene, entity));
        }

        return Task.CompletedTask;
    }
}

/// <summary>Pastes after the selected entity (as its sibling), or at the end of the scene.</summary>
public sealed class PasteEntityCommand(IWorkspace workspace, ISelection selection) : EditorCommand
{
    public override string Id => "Entity.Paste";
    public override string Text => "_Paste";
    public override KeyGesture? Gesture { get; } = new(Key.V, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 1, Order: 2);

    protected override bool CanExecuteCore() => workspace.Scene is not null && EntityClipboard.Get() is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (workspace.Scene is not { } document || EntityClipboard.Get() is not { } json)
            return Task.CompletedTask;

        var entity = workspace.Serializer.DeserializeEntity(json);
        var anchor = selection.Entity;
        var index = anchor is null ? (int?)null : document.Scene.IndexOf(anchor) + 1;
        document.Execute(new AddEntityEdit(document.Scene, entity, anchor?.Parent, index));
        selection.Select(entity);
        return Task.CompletedTask;
    }
}

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
        if (workspace.Scene is { } document && selection.Entity is { } entity)
            document.Execute(new RemoveEntityEdit(document.Scene, entity));
        return Task.CompletedTask;
    }
}

/// <summary>Prefab files as the editor uses them: saved from a selection, instantiated by drag-and-drop.</summary>
public static class PrefabFiles
{
    /// <summary>A file name made from an entity name: invalid characters become underscores.</summary>
    public static string SafeName(string entityName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(entityName.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return name.Length == 0 ? "Prefab" : name;
    }

    /// <summary>A new entity from the prefab at <paramref name="assetPath"/> (relative to Assets), or null if it can't be read.</summary>
    public static Entity? Instantiate(IWorkspace workspace, string assetPath, ICollection<string>? warnings = null)
    {
        if (workspace.Project is not { } project)
            return null;

        var path = Path.Combine(project.AssetsPath, assetPath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            return workspace.Serializer.LoadPrefabFromString(File.ReadAllText(path), warnings);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            warnings?.Add(ex.Message);
            return null;
        }
    }
}

/// <summary>
/// Saves the selected entity and its children as Assets/Prefabs/&lt;name&gt;.prefab, ready for
/// <c>Game.Spawn("name")</c> or dragging into a scene. Works in play mode too, to keep a tweaked live entity.
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

        var name = PrefabFiles.SafeName(entity.Name);
        var path = project.DefaultPrefabPath(name);
        if (File.Exists(path) && !dialogs.Confirm("Replace Prefab", $"{name}{Griff.Projects.Project.PrefabExtension} already exists. Replace it with \"{entity.Name}\"?"))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, workspace.Serializer.SavePrefabToString(entity));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialogs.ShowError("Couldn't save the prefab", ex.Message);
            return;
        }

        status.Show($"Saved {project.GetRelativePath(path)}. Spawn it with Game.Spawn(\"{name}\").");
    }
}
