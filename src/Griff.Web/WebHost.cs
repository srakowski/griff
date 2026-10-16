using System.Runtime.InteropServices;
using Griff.Audio;
using Griff.Graphics;
using Griff.Hosting;
using Griff.Rendering;
using Griff.Runtime;

namespace Griff.Web;

/// <summary>The page's canvas, as the core's <see cref="GameLoop"/> draws on it: WebGL 2 (<see cref="WebGLRenderer"/>) at the canvas's size.</summary>
public sealed class WebView(WebGLRenderer renderer) : IGameView
{
    /// <summary>The canvas's size in pixels, as the page says each frame.</summary>
    public (int Width, int Height) Size { get; set; }

    public float Aspect => Size.Width > 0 && Size.Height > 0 ? (float)Size.Width / Size.Height : 16f / 9;

    public void Render(RenderFrame frame) => renderer.Render(frame, Size.Width, Size.Height);

    public (byte[] Pixels, int Width, int Height)? Capture(RenderFrame frame)
    {
        var (width, height) = Size;
        renderer.Render(frame, width, height);
        return (renderer.Capture(width, height), width, height);
    }
}

/// <summary>
/// The game's sound in the browser: what its mixer plays, handed to Web Audio a little ahead once a frame, or, until the
/// browser lets the page make sound, played silently in time (and the page says a click or key starts it).
/// </summary>
public sealed class WebSound(BufferedOutput output) : IFrameSound
{
    private bool _waiting;

    /// <summary>The buffer between the game's mixer and Web Audio.</summary>
    public BufferedOutput Output => output;

    public void Pump()
    {
        var state = Browser.AudioState();
        var block = output.Pump(state >= 0 ? state : null, state >= 0 ? Browser.AudioLatency() : 0);
        if (!block.IsEmpty)
            Browser.AudioPush(MemoryMarshal.AsBytes(block));
        var waiting = state == -1 && output.Mixer.Playing > 0;
        if (waiting != _waiting)
            Browser.SoundWaiting(_waiting = waiting);
    }
}
