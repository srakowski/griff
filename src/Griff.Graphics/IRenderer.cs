using Griff.Rendering;

namespace Griff.Graphics;

/// <summary>
/// Draws <see cref="RenderFrame"/>s with a graphics API: OpenGL on the desktop (Griff.Graphics.OpenGL's
/// <c>OpenGLRenderer</c>) and WebGL 2 in the browser (Griff.Web's <c>WebGLRenderer</c>). Each turns the frame into the
/// same <see cref="DrawList"/> (see <see cref="QuadRenderer"/>), so they draw the same pixels; another API (Direct3D 11)
/// is one more class that runs a draw list's passes. A renderer is used on the thread its API's context belongs to.
/// </summary>
public interface IRenderer : IDisposable
{
    /// <summary>Draws <paramref name="frame"/> into the target bound now (the screen, a window or a canvas), this many pixels big.</summary>
    void Render(RenderFrame frame, int width, int height);

    /// <summary>The pixels of what was just drawn, as top-down RGBA rows (for screenshots and tests).</summary>
    byte[] Capture(int width, int height);

    /// <summary>How many draw calls the last frame took, for diagnostics.</summary>
    int LastDrawCalls { get; }
}
