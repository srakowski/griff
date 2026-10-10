using System.Numerics;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

public class SceneSerializerTests
{
    private readonly SceneSerializer _serializer;

    public SceneSerializerTests()
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<TestComponent>();
        registry.Register<RenamedComponent>();
        _serializer = new SceneSerializer(registry);
    }

    [Fact]
    public void RoundTrip_PreservesHierarchyIdentityAndValues()
    {
        var scene = new Scene();
        var player = new Entity("Player");
        var weapon = new Entity("Weapon") { IsEnabled = false };
        var camera = new Entity("Camera");
        scene.Add(player);
        scene.Add(weapon, player);
        scene.Add(camera);

        player.Transform.Position = new Vector3(1.5f, -2, 0);
        player.Transform.Rotation = new Vector3(0, 0, 45);
        player.Transform.Scale = new Vector3(2, 2, 1);
        var test = player.AddComponent<TestComponent>();
        test.Flag = true;
        test.Count = 7;
        test.Speed = 0.1f;
        test.Precise = Math.PI;
        test.Label = "Hero";
        test.Mode = TestMode.Chase;
        test.Offset = new Vector2(3, 4);
        test.Direction = new Vector3(0, 1, 0);
        test.Tint = new Vector4(1, 0.5f, 0.25f, 1);
        test.Orientation = Quaternion.CreateFromYawPitchRoll(0.1f, 0.2f, 0.3f);
        test.Ignored = 99;

        var loaded = _serializer.LoadFromString(_serializer.SaveToString(scene));

        Assert.Equal(["Player", "Camera"], loaded.Roots.Select(e => e.Name));
        var loadedPlayer = loaded.Roots[0];
        var loadedWeapon = Assert.Single(loadedPlayer.Children);
        Assert.Equal(player.Id, loadedPlayer.Id);
        Assert.Equal(weapon.Id, loadedWeapon.Id);
        Assert.False(loadedWeapon.IsEnabled);
        Assert.Same(loadedWeapon, loaded.Find(weapon.Id));

        Assert.Equal(player.Transform.Position, loadedPlayer.Transform.Position);
        Assert.Equal(player.Transform.Rotation, loadedPlayer.Transform.Rotation);
        Assert.Equal(player.Transform.Scale, loadedPlayer.Transform.Scale);

        var loadedTest = Assert.IsType<TestComponent>(loadedPlayer.Components[1]);
        Assert.True(loadedTest.Flag);
        Assert.Equal(7, loadedTest.Count);
        Assert.Equal(0.1f, loadedTest.Speed);
        Assert.Equal(Math.PI, loadedTest.Precise);
        Assert.Equal("Hero", loadedTest.Label);
        Assert.Equal(TestMode.Chase, loadedTest.Mode);
        Assert.Equal(test.Offset, loadedTest.Offset);
        Assert.Equal(test.Direction, loadedTest.Direction);
        Assert.Equal(test.Tint, loadedTest.Tint);
        Assert.Equal(test.Orientation, loadedTest.Orientation);
        Assert.Equal(0, loadedTest.Ignored);
    }

    [Fact]
    public void RoundTrip_KeepsClipsFollowParallaxAndSortByY()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().SortByY = true;
        var follow = camera.AddComponent<Follow>();
        follow.Target = "Hero";
        follow.Smoothing = 3;
        follow.Offset = new Vector2(0, 2);
        follow.DeadZone = new Vector2(1, 2);
        follow.LookAhead = 0.2f;
        follow.StayInside = "Level";
        scene.Add(camera);

        var hero = new Entity("Hero");
        hero.AddComponent<Sprite>();
        var animation = hero.AddComponent<Animation>();
        animation.Clips = "Idle 0-3, Run 4-9 @12";
        animation.Clip = "Run";
        var parallax = hero.AddComponent<Parallax>();
        parallax.Factor = new Vector2(0.3f, 1);
        parallax.RepeatX = true;
        scene.Add(hero);

        var loaded = _serializer.LoadFromString(_serializer.SaveToString(scene));

        var loadedCamera = loaded.Entities.Single(e => e.Name == "Camera");
        Assert.True(loadedCamera.GetComponent<Camera>()!.SortByY);
        var loadedFollow = loadedCamera.GetComponent<Follow>()!;
        Assert.Equal(("Hero", 3f, new Vector2(0, 2), new Vector2(1, 2), 0.2f, "Level"),
            (loadedFollow.Target, loadedFollow.Smoothing, loadedFollow.Offset, loadedFollow.DeadZone, loadedFollow.LookAhead, loadedFollow.StayInside));
        var loadedHero = loaded.Entities.Single(e => e.Name == "Hero");
        var loadedAnimation = loadedHero.GetComponent<Animation>()!;
        Assert.Equal("Run", loadedAnimation.Clip);
        Assert.Equal(2, loadedAnimation.ClipList.Count);
        Assert.Equal(new Vector2(0.3f, 1), loadedHero.GetComponent<Parallax>()!.Factor);
        Assert.True(loadedHero.GetComponent<Parallax>()!.RepeatX);
    }

    [Fact]
    public void Save_IsDeterministic()
    {
        var scene = new Scene();
        var entity = new Entity("Thing");
        scene.Add(entity);
        entity.AddComponent<TestComponent>().Speed = 1.25f;

        var first = _serializer.SaveToString(scene);
        var second = _serializer.SaveToString(_serializer.LoadFromString(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Save_IsReadable()
    {
        var scene = new Scene();
        var entity = new Entity("Player");
        scene.Add(entity);
        entity.Transform.Position = new Vector3(1, 2, 0);
        entity.AddComponent<RenamedComponent>();

        var json = _serializer.SaveToString(scene);

        Assert.Contains("\"name\": \"Player\"", json);
        Assert.Contains("\"type\": \"Transform\"", json);
        Assert.Contains("\"position\": [1, 2, 0]", json);
        Assert.Contains("\"scale\": [1, 1, 1]", json);
        Assert.Contains("\"type\": \"Health\"", json);
        Assert.DoesNotContain("\"enabled\"", json);
        Assert.DoesNotContain("\"children\"", json);
        Assert.DoesNotContain("rotationQuaternion", json);
    }

    [Fact]
    public void UnknownComponent_IsKeptAndWrittenBackUnchanged()
    {
        var json = """
            {
              "formatVersion": 1,
              "entities": [
                {
                  "id": "6f1c4f0e-8a51-4f7e-9a0c-1b7f6e8d2a10",
                  "name": "Enemy",
                  "components": [
                    { "type": "Transform", "position": [0, 0, 0], "rotation": [0, 0, 0], "scale": [1, 1, 1] },
                    { "type": "MyGame.Brain", "aggression": 0.8, "targets": ["player"] }
                  ]
                }
              ]
            }
            """;
        var warnings = new List<string>();

        var scene = _serializer.LoadFromString(json, warnings);
        var resaved = _serializer.SaveToString(scene);

        var missing = Assert.IsType<MissingComponent>(scene.Roots[0].Components[1]);
        Assert.Equal("MyGame.Brain", missing.TypeName);
        Assert.Contains(warnings, w => w.Contains("MyGame.Brain"));
        Assert.Contains("\"aggression\": 0.8", resaved);
        Assert.Contains("\"player\"", resaved);
    }

    [Fact]
    public void BadValue_KeepsDefaultAndWarns()
    {
        var json = """
            {
              "entities": [
                {
                  "id": "6f1c4f0e-8a51-4f7e-9a0c-1b7f6e8d2a10",
                  "name": "Thing",
                  "components": [
                    { "type": "Health", "value": "lots" }
                  ]
                }
              ]
            }
            """;
        var warnings = new List<string>();

        var scene = _serializer.LoadFromString(json, warnings);

        Assert.Equal(100, scene.Roots[0].GetComponent<RenamedComponent>()!.Value);
        Assert.Contains(warnings, w => w.Contains("Value"));
    }

    [Fact]
    public void DuplicateIds_AreReplacedWithWarning()
    {
        var json = """
            {
              "entities": [
                { "id": "6f1c4f0e-8a51-4f7e-9a0c-1b7f6e8d2a10", "name": "A", "components": [] },
                { "id": "6f1c4f0e-8a51-4f7e-9a0c-1b7f6e8d2a10", "name": "B", "components": [] }
              ]
            }
            """;
        var warnings = new List<string>();

        var scene = _serializer.LoadFromString(json, warnings);

        Assert.Equal(2, scene.Count);
        Assert.NotEqual(scene.Roots[0].Id, scene.Roots[1].Id);
        Assert.Single(warnings);
    }

    [Fact]
    public void NonFiniteValues_SaveAndLoadAsDefaultsWithWarning()
    {
        var scene = new Scene();
        var entity = new Entity("Broken");
        scene.Add(entity);
        entity.Transform.Position = new Vector3(float.NaN, 1, 2);
        entity.AddComponent<TestComponent>().Speed = float.PositiveInfinity;
        var warnings = new List<string>();

        var loaded = _serializer.LoadFromString(_serializer.SaveToString(scene), warnings);

        Assert.Equal(Vector3.Zero, loaded.Roots[0].Transform.Position);
        Assert.Equal(0, loaded.Roots[0].GetComponent<TestComponent>()!.Speed);
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void BuiltInComponents_RoundTrip_WithReadableColors()
    {
        var scene = new Scene();
        var entity = new Entity("Hero");
        scene.Add(entity);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Image = "Sprites/hero.png";
        sprite.Tint = Color.FromBytes(0xF0, 0x8C, 0x2E);
        sprite.Order = 3;
        entity.AddComponent<Camera>().Size = 8;

        var json = _serializer.SaveToString(scene);
        var loaded = _serializer.LoadFromString(json).Roots[0];

        Assert.Contains("\"tint\": \"#F08C2EFF\"", json);
        Assert.Equal(sprite.Tint, loaded.GetComponent<Sprite>()!.Tint);
        Assert.Equal("Sprites/hero.png", loaded.GetComponent<Sprite>()!.Image);
        Assert.Equal(3, loaded.GetComponent<Sprite>()!.Order);
        Assert.Equal(8, loaded.GetComponent<Camera>()!.Size);
    }

    [Fact]
    public void Clone_DeepCopiesWithNewIds()
    {
        var scene = new Scene();
        var parent = new Entity("Parent");
        var child = new Entity("Child");
        scene.Add(parent);
        scene.Add(child, parent);
        parent.Transform.Position = new Vector3(1, 2, 3);
        parent.AddComponent<TestComponent>().Label = "copy me";

        var clone = _serializer.Clone(parent);

        Assert.NotEqual(parent.Id, clone.Id);
        Assert.NotEqual(child.Id, clone.Children[0].Id);
        Assert.Null(clone.Scene);
        Assert.Null(clone.Parent);
        Assert.Equal("Child", clone.Children[0].Name);
        Assert.Equal(new Vector3(1, 2, 3), clone.Transform.Position);
        Assert.Equal("copy me", clone.GetComponent<TestComponent>()!.Label);
        Assert.NotSame(parent.GetComponent<TestComponent>(), clone.GetComponent<TestComponent>());

        scene.Add(clone);
        Assert.Equal(4, scene.Count);
    }

    [Fact]
    public void SerializeEntities_RoundTripsSeveral_AndReadsOne()
    {
        var a = new Entity("A");
        a.AddComponent<TestComponent>().Label = "first";
        var b = new Entity("B");
        var scene = new Scene();
        scene.Add(a);
        scene.Add(b);
        scene.Add(new Entity("B1"), b);

        var copies = _serializer.DeserializeEntities(_serializer.SerializeEntities([a, b]));

        Assert.Equal(["A", "B"], copies.Select(e => e.Name));
        Assert.Equal("first", copies[0].GetComponent<TestComponent>()!.Label);
        Assert.Equal("B1", Assert.Single(copies[1].Children).Name);
        Assert.DoesNotContain(copies, c => c.Id == a.Id || c.Id == b.Id);

        // One entity is plain entity JSON, as the clipboard has always held.
        var one = _serializer.SerializeEntities([a]);
        Assert.Equal(_serializer.SerializeEntity(a), one);
        Assert.Equal("A", Assert.Single(_serializer.DeserializeEntities(one)).Name);

        Assert.Throws<InvalidDataException>(() => _serializer.DeserializeEntities("[1, 2]"));
        Assert.Throws<InvalidDataException>(() => _serializer.DeserializeEntities("[]"));
    }

    [Fact]
    public void NewerFormat_IsRejected()
    {
        var json = $$"""{ "formatVersion": {{SceneSerializer.FormatVersion + 1}}, "entities": [] }""";

        Assert.Throws<InvalidDataException>(() => _serializer.LoadFromString(json));
    }

    [Fact]
    public void NotAScene_IsRejected()
    {
        Assert.Throws<InvalidDataException>(() => _serializer.LoadFromString("{ \"hello\": 1 }"));
        Assert.Throws<InvalidDataException>(() => _serializer.LoadFromString("not json"));
    }
}
