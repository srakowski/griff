using System.Numerics;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Runs each <see cref="SelectionControls"/>: clicks, boxes, double-clicks, control groups, Esc and right-click commands on
/// the <see cref="Selectable"/>s in its camera's view (the rules are <see cref="SelectionState"/>'s), and draws the
/// markers round what's selected and the box being dragged out. With something selected it claims Back, so Esc clears the
/// selection before it opens the game menu; a window open over the game has it first.
/// </summary>
internal sealed class SelectionSystem : GameSystem, IClaimsBack
{
    public override string Name => "Selection";

    // While something's selected (and the controls take the player's input), Esc clears it before any menu opens on it.
    public bool ClaimsBack
    {
        get
        {
            foreach (var controls in Game.Scene.ActiveNow<SelectionControls>())
            {
                if (controls.Selected.Count > 0 && TakesInput(controls))
                    return true;
            }

            return false;
        }
    }

    internal void RunSelection()
    {
        var all = Game.Scene.ActiveNow<SelectionControls>();
        if (all.Count == 0)
            return;
        foreach (var controls in all)
        {
            DropGone(controls);
            if (TakesInput(controls))
                Run(controls);
            else
                controls.State.DragFrom = null;
        }
    }

    // Not while the game is paused here, the controls don't have the player's input, or the mouse is busy with a tool.
    private bool TakesInput(SelectionControls controls) =>
        controls.Controllable && !Game.PausedThisFrame && !Game.LocalInputBlocked && !controls.Entity!.IsPaused && !IsBusy(controls);

    private bool IsBusy(SelectionControls controls) =>
        controls.Busy || (!string.IsNullOrWhiteSpace(controls.BusyFlag) && Game.FlagHolds(controls.BusyFlag));

    // What's gone (destroyed, hidden, or no longer selectable) stops being selected.
    private void DropGone(SelectionControls controls)
    {
        var selected = controls.Selected;
        for (var i = 0; i < selected.Count; i++)
        {
            if (!IsThere(selected[i]))
            {
                controls.Change(() => controls.State.Set(controls.Selected.Where(IsThere)));
                return;
            }
        }
    }

    private bool IsThere(Entity entity) => entity.Scene == Game.Scene && entity.IsActive;

    private void Run(SelectionControls controls)
    {
        var input = Game.Input;
        var camera = CameraOf(controls);
        var inView = camera is null ? input.IsMouseInView : Game.MouseIn(camera, out _);
        var mouse = camera is null ? input.MouseWorld : MouseIn(camera);
        var size = camera?.GetComponent<Camera>()?.Size ?? Views.DefaultCameraSize;
        var rules = new Rules(this, controls, ViewOf(camera, size));
        var state = controls.State;
        state.DoubleClickSeconds = state.DoubleTapSeconds = Math.Max(0, controls.DoubleClickSeconds);
        var now = Game.Time.RealTotal;
        var add = input.IsDown("AddToSelection");
        var before = controls.Selected.ToList();

        // Ctrl and a number keeps the selection as that group; the number alone picks it again, twice quickly moves there.
        var assign = input.IsDown("AssignGroup");
        for (var n = 1; n <= InputActions.GroupActions.Count; n++)
        {
            if (!input.WasPressed(InputActions.GroupActions[n - 1]))
                continue;
            if (assign)
                state.KeepGroup(rules, n);
            else if (state.PickGroup(rules, n, add, now, out var center) && center is { } middle && camera is not null)
                Follow.SetWorld(camera.Transform, middle);
        }

        // Back clears the selection, unless a window open over the game took it first (closing).
        if (controls.Selected.Count > 0 && (input.WasPressed(Key.Escape) || input.WasPressed(GamepadButton.B)))
        {
            state.Set([]);
            PanelSystem.UseBack(Game.MachineInput);
        }

        if (input.WasPressed("Select") && inView && !OnUi(camera, mouse, forCommand: false))
            (state.DragFrom, controls.DragTo) = (mouse, mouse);
        if (state.DragFrom is not null)
        {
            if (inView)
                controls.DragTo = mouse;
            if (!input.IsDown("Select"))
                state.Release(rules, controls.DragTo, size / 40, add, now);
        }

        // A right-click with something selected is a command; the press is used up, so it doesn't drag the camera too.
        if (input.WasPressed("Command") && inView && controls.Selected.Count > 0 && !OnUi(camera, mouse, forCommand: true))
        {
            controls.Changed(before);
            before = controls.Selected.ToList();
            controls.RaiseCommanded(rules.TargetAt(mouse), mouse);
            Game.MachineInput.Use("Command");
        }

        controls.Changed(before);
    }

    // The camera the controls select through: theirs if they're on one, else the scene's.
    private Entity? CameraOf(SelectionControls controls) =>
        controls.Entity!.GetComponent<Camera>() is not null ? controls.Entity : Views.FindCamera(Game.Scene);

    private Vector2 MouseIn(Entity camera)
    {
        Game.MouseIn(camera, out var world);
        return world;
    }

    // The part of the world in the camera's view.
    private Box ViewOf(Entity? camera, float size)
    {
        var center = camera is null ? Vector2.Zero : Game.Flat(camera.Transform.WorldPosition);
        var aspect = camera is null ? Game.ViewAspect : Game.AspectOf(camera);
        return new Box(center, new Vector2(size * (aspect > 0 ? aspect : 1), size));
    }

    // The mouse is on the HUD, not the world: over what a system shows, a pop-up, a button on the screen or a panel, or a
    // sprite pinned to the screen (a bar's backdrop, an open window). A button in the world is the world's to a command, but a
    // left click on one is the button's, unless it's on something selectable.
    private bool OnUi(Entity? camera, Vector2 mouse, bool forCommand)
    {
        if (Game.MouseOnHud || Game.PanelSystem.PressedOnPopup)
            return true;
        foreach (var button in Game.Scene.ActiveNow<Button>())
        {
            if (!button.IsHovered)
                continue;
            var entity = button.Entity!;
            if (!InWorld(entity, camera) || (!forCommand && entity.GetComponent<Selectable>() is null))
                return true;
        }

        var main = Views.FindCamera(Game.Scene);
        var screenMouse = Game.Input.MouseWorld;
        foreach (var sprite in Game.Scene.ActiveNow<Sprite>())
        {
            var entity = sprite.Entity!;
            if (Views.IsOnScreen(entity, main) && Matrix4x4.Invert(entity.Transform.WorldMatrix, out var toSprite) &&
                Vector2.Transform(screenMouse, toSprite) is var at && MathF.Abs(at.X) <= MathF.Abs(sprite.Size.X) / 2 && MathF.Abs(at.Y) <= MathF.Abs(sprite.Size.Y) / 2)
                return true;
        }

        return false;
    }

    // In the world: not pinned to the screen, nor on a panel.
    private static bool InWorld(Entity entity, Entity? camera)
    {
        if (Views.IsOnScreen(entity, camera))
            return false;
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Panel>() is not null)
                return false;
        }

        return true;
    }

    // The selectable on top at `point` of those `include` takes: the one drawn last where several overlap.
    private Entity? TopAt(Vector2 point, Func<Selectable, bool> include)
    {
        Entity? only = null;
        List<Entity>? several = null;
        foreach (var selectable in Game.Scene.ActiveNow<Selectable>())
        {
            if (!include(selectable) || !selectable.Area().Contains(point))
                continue;
            if (only is null)
                only = selectable.Entity;
            else
                (several ??= [only]).Add(selectable.Entity!);
        }

        if (several is null)
            return only;
        var top = only;
        foreach (var entity in Views.DrawOrder(Game.Scene))
        {
            if (several.Contains(entity))
                top = entity;
        }

        return top;
    }

    // ----- Drawing -----

    protected override void Draw(FrameDrawing frame)
    {
        foreach (var controls in Game.Scene.Active<SelectionControls>())
        {
            // In its camera's view only.
            var camera = CameraOf(controls);
            if (frame.Camera != camera && !(camera is null && frame.Viewport is null))
                continue;
            var draws = new List<SpriteDraw>();
            var line = frame.LineWidth * 1.2f;
            foreach (var entity in controls.Selected)
            {
                if (!frame.ShowsInWorld(entity) || AreaOf(entity) is not { } area)
                    continue;
                var size = area.HalfSize * 2 + new Vector2(controls.MarkerPadding * 2);
                AddFrame(draws, area.Center, size, controls.MarkerImage, controls.MarkerSlice, controls.MarkerSliceSize, controls.MarkerColor, line, fill: false);
            }

            if (controls.State.DragFrom is { } from)
            {
                var to = controls.DragTo;
                AddFrame(draws, (from + to) / 2, Vector2.Abs(to - from), controls.BoxImage, controls.BoxSlice, controls.BoxSliceSize, controls.BoxColor, frame.LineWidth, fill: true);
            }

            if (draws.Count > 0)
                frame.AddOverWorld(draws);
        }
    }

    // Where the marker goes round something selected: its selectable's area, or (selected by a script) its collider's or sprite's.
    private static Box? AreaOf(Entity entity) => entity.GetComponent<Selectable>()?.Area() ?? Follow.AreaOf(entity);

    // A picture stretched (nine-sliced) over a box, or with none an outline, filled faintly if `fill`.
    private static void AddFrame(List<SpriteDraw> draws, Vector2 center, Vector2 size, string? image, float slice, float sliceSize, Color color, float line, bool fill)
    {
        if (size.X <= 0 || size.Y <= 0)
            return;
        if (!string.IsNullOrWhiteSpace(image))
        {
            var space = Matrix4x4.CreateTranslation(center.X, center.Y, 0);
            foreach (var (world, source) in Quads.NineSlice(size, slice, sliceSize, new Vector4(0, 0, 1, 1), space))
                draws.Add(new SpriteDraw(world, image.Trim(), color, Views.ScreenEntity) { Source = source });
            return;
        }

        if (fill)
            draws.Add(FrameDrawing.Quad(center, size, color with { A = color.A * 0.12f }));
        draws.AddRange(FrameDrawing.Outline(center, size, line, color));
    }

    // What the rules of selecting go by for one Selection Controls: the selectables, and what's in its camera's view.
    private sealed class Rules(SelectionSystem system, SelectionControls controls, Box view) : ISelectionRules
    {
        public Entity? PickAt(Vector2 point) => system.TopAt(point, s => s.Clickable);

        public IEnumerable<Entity> InBox(Box box) =>
            system.Game.Scene.ActiveNow<Selectable>().Where(s => s.InBox && s.Clickable && s.Area().Overlaps(box)).Select(s => s.Entity!).ToList();

        public IEnumerable<Entity>? SameKind(Entity picked)
        {
            if (picked.GetComponent<Selectable>() is not { } selectable)
                return null;
            var kind = selectable.KindName;
            return system.Game.Scene.ActiveNow<Selectable>()
                .Where(s => s.Clickable && string.Equals(s.KindName, kind, StringComparison.OrdinalIgnoreCase) && s.Area().Overlaps(view))
                .Select(s => s.Entity!).ToList();
        }

        public bool CanAdd(Entity picked, IReadOnlyList<Entity> selected) => true;

        public bool Keeps(Entity entity) => true;

        public bool StillThere(Entity entity) => system.IsThere(entity);

        public void SelectionSet()
        {
        }

        // What a command at `point` is aimed at: the selectable on top there (one a click doesn't pick too), else a button in
        // the world under the mouse, else nothing (the ground).
        public Entity? TargetAt(Vector2 point)
        {
            if (system.TopAt(point, _ => true) is { } target)
                return target;
            var camera = system.CameraOf(controls);
            foreach (var button in system.Game.Scene.ActiveNow<Button>())
            {
                if (button.IsHovered && InWorld(button.Entity!, camera))
                    return button.Entity;
            }

            return null;
        }
    }
}
