using System.Numerics;
using Griff.Fonts;
using Griff.Graphics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Tests.Graphics;

public sealed partial class DrawListTests
{
    [Fact]
    public void ClippedSprites_AreDrawnInAViewCutToTheirArea_ThenTheWholeViewAgain()
    {
        var clip = new ClipArea(new Box(Vector2.Zero, new Vector2(2, 1)));
        SpriteDraw Sprite(ClipArea? area) => new(Matrix4x4.Identity, null, Color.White, Owner) { Clip = area };
        var list = new DrawList(_ => 1, new GlyphAtlas(null));
        var frame = new RenderFrame
        {
            ViewProjection = Matrix4x4.CreateOrthographic(16, 9, -10, 10),
            Sprites = [Sprite(null), Sprite(clip), Sprite(clip), Sprite(null)],
        };

        list.Build(frame, 960, 540);

        Assert.Equal(
            [DrawOp.Target, DrawOp.View, DrawOp.Clear, DrawOp.Sprites, DrawOp.View, DrawOp.Sprites, DrawOp.View, DrawOp.Sprites],
            list.Commands.ToArray().Select(c => c.Op));
        var views = list.Commands.ToArray().Where(c => c.Op == DrawOp.View).Select(c => list.Values.Slice(c.Value, 4).ToArray()).ToList();
        // 60 pixels a unit: 4 by 2 units round the middle of the screen.
        Assert.Equal([360f, 210, 240, 120], views[1]);
        Assert.Equal([0f, 0, 960, 540], views[2]);
        Assert.Equal(2, list.Commands[5].Count);
    }
}
