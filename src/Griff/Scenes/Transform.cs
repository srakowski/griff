using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// Position, rotation and scale relative to the parent entity. Every entity has exactly one.
/// Values are 3D so 2D and 3D share one model: 2D games use X/Y and rotate around Z.
/// </summary>
[Category("Basics")]
[ComponentName("Transform")]
public sealed class Transform : Component
{
    private Vector3 _position;
    private Vector3 _rotation;
    private Vector3 _scale = Vector3.One;

    // The matrices, kept until something moves: this transform's own, and the world one with the parent's it was made from.
    private Matrix4x4 _local;
    private bool _localKept;
    private Matrix4x4 _world;
    private bool _worldKept;
    private Transform? _worldParent;
    private int _worldParentStamp;

    // Goes up each time the world matrix is worked out again, so children know theirs is out of date.
    private int _worldStamp;

    /// <summary>Where it is, in world units, from its parent's position (or the world's middle, with none). Y is up.</summary>
    public Vector3 Position
    {
        get => _position;
        set
        {
            if (_position == value)
                return;
            _position = value;
            Moved();
        }
    }

    /// <summary>Euler angles in degrees, applied Z, then X, then Y. In 2D, Z is the rotation angle.</summary>
    public Vector3 Rotation
    {
        get => _rotation;
        set
        {
            if (_rotation == value)
                return;
            _rotation = value;
            Moved();
        }
    }

    /// <summary>How many times its own size it's drawn and collides: 2 is twice as big, -1 flips it. Children scale with it.</summary>
    public Vector3 Scale
    {
        get => _scale;
        set
        {
            if (_scale == value)
                return;
            _scale = value;
            Moved();
        }
    }

    [DoNotSerialize]
    public Quaternion RotationQuaternion
    {
        get => Quaternion.CreateFromYawPitchRoll(ToRadians(Rotation.Y), ToRadians(Rotation.X), ToRadians(Rotation.Z));
        set => Rotation = ToEulerDegrees(value);
    }

    /// <summary>Transform from this entity's space to its parent's space.</summary>
    public Matrix4x4 LocalMatrix
    {
        get
        {
            if (!_localKept)
            {
                _local = Matrix4x4.CreateScale(Scale) *
                         Matrix4x4.CreateFromQuaternion(RotationQuaternion) *
                         Matrix4x4.CreateTranslation(Position);
                _localKept = true;
            }

            return _local;
        }
    }

    /// <summary>Transform from this entity's space to world space. Kept until it, or something it's inside, moves.</summary>
    public Matrix4x4 WorldMatrix
    {
        get
        {
            var parent = Entity?.Parent?.Transform;
            if (parent is null)
            {
                if (!_worldKept || _worldParent is not null)
                    Keep(LocalMatrix, null);
                return _world;
            }

            var parentWorld = parent.WorldMatrix;
            if (!_worldKept || _worldParent != parent || _worldParentStamp != parent._worldStamp)
                Keep(LocalMatrix * parentWorld, parent);
            return _world;
        }
    }

    private void Keep(Matrix4x4 world, Transform? parent)
    {
        _world = world;
        _worldParent = parent;
        _worldParentStamp = parent?._worldStamp ?? 0;
        _worldKept = true;
        _worldStamp++;
    }

    private void Moved()
    {
        _localKept = false;
        _worldKept = false;
    }

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
