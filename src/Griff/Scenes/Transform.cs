using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// Position, rotation and scale relative to the parent entity. Every entity has exactly one.
/// Values are 3D so 2D and 3D share one model: 2D games use X/Y and rotate around Z.
/// </summary>
[ComponentName("Transform")]
public sealed class Transform : Component
{
    public Vector3 Position { get; set; }

    /// <summary>Euler angles in degrees, applied Z, then X, then Y. In 2D, Z is the rotation angle.</summary>
    public Vector3 Rotation { get; set; }

    public Vector3 Scale { get; set; } = Vector3.One;

    [DoNotSerialize]
    public Quaternion RotationQuaternion
    {
        get => Quaternion.CreateFromYawPitchRoll(ToRadians(Rotation.Y), ToRadians(Rotation.X), ToRadians(Rotation.Z));
        set => Rotation = ToEulerDegrees(value);
    }

    /// <summary>Transform from this entity's space to its parent's space.</summary>
    public Matrix4x4 LocalMatrix =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(RotationQuaternion) *
        Matrix4x4.CreateTranslation(Position);

    /// <summary>Transform from this entity's space to world space.</summary>
    public Matrix4x4 WorldMatrix =>
        Entity?.Parent is { } parent ? LocalMatrix * parent.Transform.WorldMatrix : LocalMatrix;

    public Vector3 WorldPosition => WorldMatrix.Translation;

    private static float ToRadians(float degrees) => degrees * (MathF.PI / 180f);

    private static float ToDegrees(float radians) => radians * (180f / MathF.PI);

    // Inverse of Quaternion.CreateFromYawPitchRoll (yaw = Y, pitch = X, roll = Z).
    private static Vector3 ToEulerDegrees(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        var sinPitch = Math.Clamp(2f * (q.W * q.X - q.Y * q.Z), -1f, 1f);
        var pitch = MathF.Asin(sinPitch);
        var yaw = MathF.Atan2(2f * (q.W * q.Y + q.X * q.Z), 1f - 2f * (q.X * q.X + q.Y * q.Y));
        var roll = MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.X * q.X + q.Z * q.Z));
        return new Vector3(ToDegrees(pitch), ToDegrees(yaw), ToDegrees(roll));
    }
}
