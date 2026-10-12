using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

/// <summary>Subheadings ([Group]) and fields only used while another is on ([ShownWhen]), as descriptors read them.</summary>
public class FieldGroupTests
{
    public enum Shape
    {
        None,
        Circle,
        Box,
    }

    public class GroupedBase : Component
    {
        public float Speed { get; set; }

        [Group("Base")]
        public float Weight { get; set; }
    }

    public sealed class Grouped : GroupedBase
    {
        public bool Glide { get; set; }

        [Group("Glide")]
        [ShownWhen(nameof(Glide))]
        public float GlideSpeed { get; set; }

        [ShownWhen(nameof(Target))]
        public float Range { get; set; }

        public string? Target { get; set; }

        [Group("Shape")]
        public Shape Shape { get; set; }

        [ShownWhen(nameof(Shape))]
        public float Size { get; set; }

        [ShownWhen(nameof(Shape), Is = [Shape.Circle])]
        public float Radius { get; set; }

        [ShownWhen("Nothing")]
        public float Lost { get; set; }
    }

    private static ComponentDescriptor Descriptor() => ComponentRegistry.CreateDefault().Register<Grouped>();

    private static PropertyDescriptor Property(string name) => Descriptor().Properties.Single(p => p.Name == name);

    [Fact]
    public void AGroup_RunsOnToTheNext_WithinItsClass()
    {
        var groups = Descriptor().Properties.ToDictionary(p => p.Name, p => p.Group);

        Assert.Null(groups["Speed"]);
        Assert.Equal("Base", groups["Weight"]);
        // The subclass's own fields start ungrouped again.
        Assert.Null(groups["Glide"]);
        Assert.Equal("Glide", groups["GlideSpeed"]);
        Assert.Equal("Glide", groups["Target"]);
        Assert.Equal("Shape", groups["Shape"]);
        Assert.Equal("Shape", groups["Lost"]);
    }

    [Fact]
    public void ShownWhen_FollowsABool_AText_AndAnEnum()
    {
        var component = new Grouped();
        Assert.False(Property("GlideSpeed").IsShown(component));
        Assert.False(Property("Range").IsShown(component));
        Assert.False(Property("Size").IsShown(component));
        Assert.False(Property("Radius").IsShown(component));

        component.Glide = true;
        component.Target = "Player";
        component.Shape = Shape.Box;
        Assert.True(Property("GlideSpeed").IsShown(component));
        Assert.True(Property("Range").IsShown(component));
        Assert.True(Property("Size").IsShown(component));
        Assert.False(Property("Radius").IsShown(component));

        component.Shape = Shape.Circle;
        Assert.True(Property("Radius").IsShown(component));
        // Naming nothing, it's always used; without ShownWhen, too.
        Assert.True(Property("Lost").IsShown(component));
        Assert.True(Property("Speed").IsShown(component));
    }

    [Fact]
    public void ShownWhenText_SaysWhatHasToBeSo()
    {
        Assert.Equal("Glide is on", Property("GlideSpeed").ShownWhenText());
        Assert.Equal("Target is set", Property("Range").ShownWhenText());
        Assert.Equal("Shape isn't None", Property("Size").ShownWhenText());
        Assert.Equal("shape is circle", Property("Radius").ShownWhenText(p => p.ToLowerInvariant(), v => v.ToLowerInvariant()));
        Assert.Null(Property("Lost").ShownWhenText());
        Assert.Null(Property("Speed").ShownWhenText());
    }

    [Fact]
    public void PlatformerMovement_IsGrouped_AndItsDashFieldsFollowDash()
    {
        var descriptor = ComponentRegistry.CreateDefault().Get(typeof(PlatformerMovement));
        var groups = descriptor.Properties.Select(p => p.Group).Distinct().ToList();
        Assert.Equal([null, "Jump", "Walls", "Dash", "Ladders and Platforms", "Control", "Sounds and Effects"], groups);

        var movement = new PlatformerMovement();
        var dashSpeed = descriptor.Properties.Single(p => p.Name == nameof(PlatformerMovement.DashSpeed));
        var wallSlideSpeed = descriptor.Properties.Single(p => p.Name == nameof(PlatformerMovement.WallSlideSpeed));
        Assert.False(dashSpeed.IsShown(movement));
        Assert.False(wallSlideSpeed.IsShown(movement));
        movement.Dash = true;
        Assert.True(dashSpeed.IsShown(movement));
        Assert.False(wallSlideSpeed.IsShown(movement));
    }

    [Fact]
    public void ButtonFields_FollowItsAction()
    {
        var descriptor = ComponentRegistry.CreateDefault().Get(typeof(Button));
        bool Shown(string name, ButtonAction action) =>
            descriptor.Properties.Single(p => p.Name == name).IsShown(new Button { Action = action });

        Assert.True(Shown(nameof(Button.Scene), ButtonAction.LoadScene));
        Assert.False(Shown(nameof(Button.Scene), ButtonAction.Quit));
        Assert.True(Shown(nameof(Button.Fade), ButtonAction.RestartScene));
        Assert.True(Shown(nameof(Button.Panel), ButtonAction.OpenPanel));
        Assert.False(Shown(nameof(Button.Panel), ButtonAction.ClosePanel));
        Assert.True(Shown(nameof(Button.InputAction), ButtonAction.Rebind));
        Assert.Equal("Action is LoadScene, HostGame or LeaveGame",
            descriptor.Properties.Single(p => p.Name == nameof(Button.Scene)).ShownWhenText());
    }

    [Fact]
    public void EveryBuiltInShownWhen_NamesAPropertyOfItsComponent()
    {
        var registry = ComponentRegistry.CreateDefault();
        var broken = registry.All
            .SelectMany(d => d.Properties.Where(p => p.ShownWhen is not null && p.Condition is null).Select(p => $"{d.DisplayName}.{p.Name}"))
            .ToList();

        Assert.Empty(broken);
        Assert.Contains(registry.All, d => d.Properties.Any(p => p.Condition is not null));
    }

    [Fact]
    public void EveryBuiltInEnumShownWhen_ListsValuesOfThatEnum()
    {
        var wrong = ComponentRegistry.CreateDefault().All
            .SelectMany(d => d.Properties.Where(p => p.ShownWhen?.Is is { } values && p.Condition is { } c && values.Any(v => v.GetType() != c.Type)))
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(wrong);
    }
}
