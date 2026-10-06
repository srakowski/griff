using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

public class ComponentRegistryTests
{
    [Fact]
    public void Descriptor_ListsSerializableProperties_InDeclarationOrder()
    {
        var descriptor = ComponentRegistry.CreateDefault().Register<TestComponent>();

        Assert.Equal(
            ["Flag", "Count", "Speed", "Precise", "Label", "Mode", "Offset", "Direction", "Tint", "Orientation"],
            descriptor.Properties.Select(p => p.Name));
        Assert.Equal("Griff.Tests.Scenes.TestComponent", descriptor.Name);
    }

    [Fact]
    public void Transform_IsBuiltIn()
    {
        var descriptor = ComponentRegistry.CreateDefault().Get(typeof(Transform));

        Assert.Equal("Transform", descriptor.Name);
        Assert.Equal(["Position", "Rotation", "Scale"], descriptor.Properties.Select(p => p.Name));
    }

    [Fact]
    public void ComponentNameAttribute_SetsSavedName()
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<RenamedComponent>();

        Assert.Equal(typeof(RenamedComponent), registry.Find("Health")?.Type);
    }

    [Fact]
    public void UnsupportedPropertyType_IsRejectedWithGuidance()
    {
        var ex = Assert.Throws<NotSupportedException>(() => ComponentRegistry.CreateDefault().Register<UnsupportedComponent>());

        Assert.Contains("Items", ex.Message);
        Assert.Contains("DoNotSerialize", ex.Message);
    }

    [Fact]
    public void MissingComponent_CannotBeRegistered()
    {
        Assert.Throws<ArgumentException>(() => ComponentRegistry.CreateDefault().Register(typeof(MissingComponent)));
    }
}
