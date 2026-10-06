using System.Numerics;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

public class EntityClipboardTests
{
    private readonly SceneSerializer _serializer = new(ComponentRegistry.CreateDefault());

    [Fact]
    public void SerializeThenDeserialize_CopiesWithNewIds()
    {
        var parent = new Entity("Crate");
        parent.Transform.Position = new Vector3(2, 3, 0);
        parent.AddComponent<Sprite>().Order = 4;
        var child = new Entity("Label");
        var scene = new Scene();
        scene.Add(parent);
        scene.Add(child, parent);

        var json = _serializer.SerializeEntity(parent);
        var copy = _serializer.DeserializeEntity(json);

        Assert.Contains("\"name\": \"Crate\"", json);
        Assert.NotEqual(parent.Id, copy.Id);
        Assert.Equal(new Vector3(2, 3, 0), copy.Transform.Position);
        Assert.Equal(4, copy.GetComponent<Sprite>()!.Order);
        Assert.Equal("Label", Assert.Single(copy.Children).Name);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("{ \"name\": \"no components\" }")]
    [InlineData("[1, 2]")]
    public void Deserialize_RejectsNonEntities(string text)
    {
        Assert.Throws<InvalidDataException>(() => _serializer.DeserializeEntity(text));
    }
}
