using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Tests.Rendering;

public class SliceAndBarRenderingTests
{
    private const float Tolerance = 1e-4f;
    private readonly Scene _scene = new();

    private static Vector2 Center(SpriteDraw draw) => new(draw.World.M41, draw.World.M42);

    private static Vector2 Size(SpriteDraw draw) => new(MathF.Abs(draw.World.M11), MathF.Abs(draw.World.M22));

    private Entity Add(string name, Vector3 position = default)
    {
        var entity = new Entity(name);
        entity.Transform.Position = position;
        _scene.Add(entity);
        return entity;
    }

    [Fact]
    public void SlicedSprite_KeepsItsCornersAtTheSliceSize_AndStretchesTheRest()
    {
        var panel = Add("Panel", new Vector3(10, 0, 0));
        var sprite = panel.AddComponent<Sprite>();
        sprite.Image = "panel.png";
        sprite.Size = new Vector2(6, 4);
        sprite.Slice = 0.25f;
        sprite.SliceSize = 0.5f;

        var draws = SceneRenderer.Build(_scene, 1).Sprites;

        Assert.Equal(9, draws.Count);
        // Top left corner: half a unit square in the corner, showing the image's top-left quarter-by-quarter.
        Assert.Equal(new Vector2(0.5f, 0.5f), Size(draws[0]));
        Assert.Equal(new Vector2(10 - 2.75f, 1.75f), Center(draws[0]));
        Assert.Equal(new Vector4(0, 0, 0.25f, 0.25f), draws[0].Source);
        // The middle stretches over everything else.
        Assert.Equal(new Vector2(5, 3), Size(draws[4]));
        Assert.Equal(new Vector2(10, 0), Center(draws[4]));
        Assert.Equal(new Vector4(0.25f, 0.25f, 0.5f, 0.5f), draws[4].Source);
        // Bottom right corner.
        Assert.Equal(new Vector2(10 + 2.75f, -1.75f), Center(draws[8]));
        Assert.Equal(new Vector4(0.75f, 0.75f, 0.25f, 0.25f), draws[8].Source);
    }

    [Fact]
    public void SlicedSprite_SlicesTheCurrentFrame_AndNeverOverlapsItsBorders()
    {
        var sprite = Add("Tiny").AddComponent<Sprite>();
        sprite.Size = new Vector2(1, 4);
        sprite.Slice = 0.5f;
        sprite.SliceSize = 2;
        sprite.Columns = 2;
        sprite.Frame = 1;

        var draws = SceneRenderer.Build(_scene, 1).Sprites;

        // Borders are capped at half the sprite, leaving no middle: just the four corners, covering it exactly.
        Assert.Equal(4, draws.Count);
        Assert.All(draws, d => Assert.InRange(d.Source.X, 0.5f - Tolerance, 1));
        Assert.Equal(4f, draws.Sum(d => Size(d).X * Size(d).Y), 3);
    }

    [Fact]
    public void SlicedSprite_RepeatsAsAWhole_InARepeatingParallaxLayer()
    {
        var camera = Add("Camera");
        camera.AddComponent<Camera>().Size = 5;
        var hills = Add("Hills");
        var sprite = hills.AddComponent<Sprite>();
        sprite.Size = new Vector2(4, 2);
        sprite.Slice = 0.25f;
        var parallax = hills.AddComponent<Parallax>();
        parallax.Factor = Vector2.One;
        parallax.RepeatX = true;

        var draws = SceneRenderer.Build(_scene, 2).Sprites;

        // A view 20 wide shows the 4-wide panel six times (-10..10, centered copies at -8..8 overlap the edges), nine pieces each.
        Assert.Equal(0, draws.Count % 9);
        var copies = draws.Count / 9;
        Assert.InRange(copies, 5, 7);
        Assert.Equal(copies, draws.Count(d => Size(d) == new Vector2(3.5f, 1.5f))); // one middle each
    }

    [Fact]
    public void UnslicedSprite_IsOneQuad()
    {
        Add("Plain").AddComponent<Sprite>().SliceSize = 1;

        Assert.Single(SceneRenderer.Build(_scene, 1).Sprites);
    }

    [Fact]
    public void Bar_DrawsBackgroundTrailAndFill_TowardsItsDirection()
    {
        var entity = Add("Health", new Vector3(0, 3, 0));
        var bar = entity.AddComponent<Bar>();
        bar.Size = new Vector2(4, 0.5f);
        bar.Max = 4;
        bar.Value = 1;

        var draws = SceneRenderer.Build(_scene, 1).Sprites;
        Assert.Equal(2, draws.Count); // no trail yet
        Assert.Equal(bar.Background, draws[0].Tint);
        Assert.Equal(new Vector2(4, 0.5f), Size(draws[0]));
        Assert.Equal(bar.Fill, draws[1].Tint);
        Assert.Equal(new Vector2(1, 0.5f), Size(draws[1]));
        Assert.Equal(new Vector2(-1.5f, 3), Center(draws[1]));

        bar.Direction = BarDirection.Down;
        var down = SceneRenderer.Build(_scene, 1).Sprites[1];
        Assert.Equal(new Vector2(4, 0.125f), Size(down));
        Assert.Equal(new Vector2(0, 3 + 0.25f - 0.0625f), Center(down));
    }

    [Fact]
    public void Slider_DrawsItsBarAndAKnobAtTheValue()
    {
        var slider = Add("Volume").AddComponent<Slider>();
        slider.Size = new Vector2(4, 0.5f);
        slider.Value = 0.75f;

        var draws = SceneRenderer.Build(_scene, 1).Sprites;

        Assert.Equal(3, draws.Count);
        Assert.Equal(new Vector2(3, 0.5f), Size(draws[1]));
        Assert.Equal(new Vector2(1, 0), Center(draws[2]));
        Assert.Equal(slider.Knob, draws[2].Tint);
    }

    [Fact]
    public void BarsAndSliders_SortWithTheirOrder()
    {
        var back = Add("Back").AddComponent<Sprite>();
        back.Order = 150;
        Add("Bar").AddComponent<Bar>().Order = 100;

        var draws = SceneRenderer.Build(_scene, 1).Sprites;

        Assert.Equal("Bar", draws[0].Entity.Name);
        Assert.Equal("Back", draws[^1].Entity.Name);
    }
}
