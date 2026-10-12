using Griff.Editing;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Editing;

public class ComponentEditsTests
{
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    private static Entity WithSpriteBodyHealth(string name)
    {
        var entity = new Entity(name);
        entity.AddComponent(new Sprite());
        entity.AddComponent(new Body());
        entity.AddComponent(new Health());
        return entity;
    }

    private static string[] Order(Entity entity) => [.. entity.Components.Select(c => c.GetType().Name)];

    [Fact]
    public void Reset_SetsEveryValueBackToANewOnes_AsOneStep()
    {
        var health = new Health { Max = 9, Current = 4, FlashOnHit = false };
        var entity = new Entity("Player");
        entity.AddComponent(health);
        var edit = ComponentEdits.Reset(_registry, [health])!;

        edit.Apply();
        var fresh = new Health();
        Assert.Equal((fresh.Max, fresh.Current, true), (health.Max, health.Current, health.FlashOnHit));
        edit.Revert();
        Assert.Equal((9f, 4f, false), (health.Max, health.Current, health.FlashOnHit));

        // Already as new: nothing to do.
        Assert.Null(ComponentEdits.Reset(_registry, [new Health()]));
    }

    [Fact]
    public void CopiedValues_PasteOntoComponentsOfTheirTypeOnly()
    {
        var values = ComponentEdits.Copy(_registry, new Sprite { Tint = Color.Red, Order = 4, Image = "gem.png" })!;
        Assert.Equal("Sprite", values.TypeName);
        var (a, b) = (new Sprite(), new Sprite { Tint = Color.Red });

        var edit = ComponentEdits.Paste(_registry, values, [a, b])!;
        edit.Apply();
        Assert.All([a, b], s => Assert.Equal((Color.Red, 4, "gem.png"), (s.Tint, s.Order, s.Image)));
        edit.Revert();
        Assert.Equal((Color.White, 0), (a.Tint, a.Order));

        Assert.Null(ComponentEdits.Paste(_registry, values, [new Health()]));
    }

    [Fact]
    public void Move_StepsAComponentAlongItsEntity_ButNeverAboveTheTransform()
    {
        var entity = WithSpriteBodyHealth("Player");
        var health = entity.GetComponent<Health>()!;
        var sprite = entity.GetComponent<Sprite>()!;

        var up = ComponentEdits.Move([health], -1)!;
        up.Apply();
        Assert.Equal(["Transform", "Sprite", "Health", "Body"], Order(entity));
        Assert.Equal("Move Health Up", up.Description);
        up.Revert();
        Assert.Equal(["Transform", "Sprite", "Body", "Health"], Order(entity));

        Assert.False(ComponentEdits.CanMove([sprite], -1));
        Assert.False(ComponentEdits.CanMove([health], 1));
        Assert.False(ComponentEdits.CanMove([entity.Transform], 1));
        Assert.Null(ComponentEdits.Move([sprite], -1));
    }

    [Fact]
    public void Move_OnSeveralEntities_MovesEachThatCan()
    {
        var (first, second) = (WithSpriteBodyHealth("A"), new Entity("B"));
        second.AddComponent(new Health());
        second.AddComponent(new Sprite());

        var edit = ComponentEdits.Move([first.GetComponent<Health>()!, second.GetComponent<Health>()!], -1)!;
        edit.Apply();
        Assert.Equal(["Transform", "Sprite", "Health", "Body"], Order(first));
        Assert.Equal(["Transform", "Health", "Sprite"], Order(second));
        edit.Revert();
        Assert.Equal(["Transform", "Sprite", "Body", "Health"], Order(first));
    }

    [Fact]
    public void AddCopies_GivesEachEntityACopyWithItsValues_AndWhatItNeeds()
    {
        var source = new PlatformerMovement { JumpHeight = 5 };
        var owner = new Entity("Hero");
        owner.AddComponent(new Body());
        owner.AddComponent(source);
        var (a, b) = (new Entity("A"), new Entity("B"));

        var edit = ComponentEdits.AddCopies(_registry, source, [a, b])!;
        edit.Apply();
        Assert.All([a, b], e =>
        {
            Assert.NotNull(e.GetComponent<Body>());
            Assert.Equal(5, e.GetComponent<PlatformerMovement>()!.JumpHeight);
        });
        Assert.NotSame(source, a.GetComponent<PlatformerMovement>());
        edit.Revert();
        Assert.All([a, b], e => Assert.Equal(["Transform"], Order(e)));
    }
}
