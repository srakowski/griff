using System.IO;
using System.Numerics;
using Griff.Graphics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Editor.Tests;

/// <summary>
/// The desktop's renderer drawing for real, off the screen (CI draws with Mesa's software OpenGL): sprites, the dark with
/// its lights and shadows, and screen effects, with no garbage a frame. That the browser draws the same pixels is
/// <c>tools\web.ps1 -Compare</c>'s to check.
/// </summary>
public sealed class OpenGLRendererTests
{
    private const int Width = 160, Height = 90;
    private static readonly string NoAssets = Path.GetTempPath();

    [Fact]
    public void ASprite_IsDrawnInItsTint_OverTheBackground()
    {
        var scene = Scene();
        Add(scene, Vector2.Zero, new Vector2(2, 2)).Tint = new Color(1, 0, 0);

        var pixels = Draw(scene);

        Assert.Equal((255, 0, 0), At(pixels, Vector2.Zero));
        var background = SceneRenderer.DefaultBackground;
        Assert.Equal(((int)MathF.Round(background.R * 255), (int)MathF.Round(background.G * 255), (int)MathF.Round(background.B * 255)), At(pixels, new Vector2(-7, 4)));
    }

    [Fact]
    public void TheDark_IsLitByLights_AndShadowedBehindWhatBlocksThem()
    {
        var scene = Scene(ambient: Color.Black);
        Add(scene, Vector2.Zero, new Vector2(20, 10));
        var lamp = new Entity("Lamp");
        var light = lamp.AddComponent<Light>();
        (light.Radius, light.Shadows, light.Softness) = (7, true, 0.2f);
        scene.Add(lamp);
        var wall = new Entity("Wall");
        wall.Transform.Position = new Vector3(2, 0, 0);
        wall.AddComponent<BoxCollider>().Size = new Vector2(0.5f, 3);
        scene.Add(wall);

        var pixels = Draw(scene);

        // Lit near the lamp, on both sides of it; dark behind the wall, out of reach, and where it reaches no further.
        Assert.True(At(pixels, new Vector2(-3, 0)).R > 150, $"Lit in front: {At(pixels, new Vector2(-3, 0))}");
        Assert.True(At(pixels, new Vector2(0, 2)).R > 150, $"Lit above: {At(pixels, new Vector2(0, 2))}");
        Assert.True(At(pixels, new Vector2(3.5f, 0)).R < 10, $"Shadowed: {At(pixels, new Vector2(3.5f, 0))}");
        Assert.True(At(pixels, new Vector2(-8, 4.5f)).R < 10, $"Out of reach: {At(pixels, new Vector2(-8, 4.5f))}");
    }

    [Fact]
    public void ScreenEffects_TurnTheWholeScreen_IntoTheirPalette()
    {
        var scene = Scene();
        Add(scene, Vector2.Zero, new Vector2(4, 4)).Tint = new Color(0.9f, 0.8f, 0.2f);
        var effects = scene.Entities[0].AddComponent<ScreenEffects>();
        (effects.Vignette, effects.Palette) = (0, ScreenPalette.OneBit);

        var pixels = Draw(scene);

        for (var i = 0; i < Width * Height; i++)
            Assert.True(pixels[i * 4] is 0 or 255 && pixels[i * 4] == pixels[i * 4 + 1] && pixels[i * 4] == pixels[i * 4 + 2], $"Pixel {i} isn't black or white.");
        Assert.Equal((255, 255, 255), At(pixels, Vector2.Zero));
        Assert.Equal((0, 0, 0), At(pixels, new Vector2(-7, 4)));
    }

    [Fact]
    public void Frames_MakeNoGarbage()
    {
        var scene = Scene(ambient: new Color(0.2f, 0.2f, 0.3f));
        for (var i = 0; i < 50; i++)
            Add(scene, new Vector2(i % 10 - 5, i / 10 - 2), Vector2.One);
        var lamp = new Entity("Lamp");
        var light = lamp.AddComponent<Light>();
        (light.Radius, light.Shadows) = (6, true);

        scene.Add(lamp);
        scene.Entities[0].AddComponent<ScreenEffects>().Scanlines = 0.5f;
        var frame = SceneRenderer.Build(scene, (float)Width / Height);
        using var gl = new OffscreenGL(Width, Height, NoAssets);
        for (var i = 0; i < 10; i++)
            gl.Render(frame);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++)
            gl.Render(frame);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(gl.Renderer.LastDrawCalls > 0);
    }

    private static Scene Scene(Color? ambient = null)
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        camera.AddComponent<Camera>().Ambient = ambient ?? Color.White;
        scene.Add(camera);
        return scene;
    }

    // A white sprite with no image.
    private static Sprite Add(Scene scene, Vector2 at, Vector2 size)
    {
        var entity = new Entity("Sprite");
        entity.Transform.Position = new Vector3(at, 0);
        var sprite = entity.AddComponent<Sprite>();
        sprite.Size = size;
        scene.Add(entity);
        return sprite;
    }

    private static byte[] Draw(Scene scene)
    {
        using var gl = new OffscreenGL(Width, Height, NoAssets);
        gl.Render(SceneRenderer.Build(scene, (float)Width / Height));
        return gl.Capture();
    }

    // The pixel at a place in the world: the view is 10 high, around the origin.
    private static (int R, int G, int B) At(byte[] pixels, Vector2 world)
    {
        var x = (int)((world.X / (10f * Width / Height) + 0.5f) * Width);
        var y = (int)((0.5f - world.Y / 10f) * Height);
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        var i = (y * Width + x) * 4;
        return (pixels[i], pixels[i + 1], pixels[i + 2]);
    }
}
