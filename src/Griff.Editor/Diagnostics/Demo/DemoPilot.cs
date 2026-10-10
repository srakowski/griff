#if DEBUG
using System.Numerics;
using System.Windows.Media;
using Griff.Runtime;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>Runs a <see cref="DemoPilotBrain"/> once per displayed frame against the game playing in the editor.</summary>
internal sealed class DemoPilot : IDisposable
{
    private readonly Func<Game?> _game;
    private readonly DemoPilotBrain _brain;

    public DemoPilot(Func<Game?> game, DemoOverlay overlay)
    {
        _game = game;
        _brain = new DemoPilotBrain(overlay.ShowKeys);
        CompositionTarget.Rendering += OnRendering;
    }

    public bool Enabled
    {
        get => _brain.Enabled;
        set => _brain.Enabled = value;
    }

    public float? StopAt
    {
        get => _brain.StopAt;
        set => _brain.StopAt = value;
    }

    public bool Attacks
    {
        get => _brain.Attacks;
        set => _brain.Attacks = value;
    }

    public Vector2 PlayerPosition => _brain.PlayerPosition;

    public void Dispose()
    {
        CompositionTarget.Rendering -= OnRendering;
        _brain.ReleaseAll(_game());
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_game() is { } game)
            _brain.Step(game);
    }
}
#endif
