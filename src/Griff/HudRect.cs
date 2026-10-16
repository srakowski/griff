using System.Numerics;

namespace Griff;

/// <summary>A rectangle on a HUD (a strategy game's, a sandbox's), in world units: a HUD is laid out over the camera's view.</summary>
public readonly record struct HudRect(float X, float Y, float Width, float Height)
{
    public Vector2 Center => new(X + Width / 2, Y + Height / 2);

    public float Top => Y + Height;

    public float Right => X + Width;

    public bool Contains(Vector2 point) => point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Y + Height;
}
