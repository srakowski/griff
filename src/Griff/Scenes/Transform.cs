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

    // Drawing a running game between its steps: where it was as the step it last moved in began, which step that was,
    // and the last step it jumped in (teleported, added to the scene, moved to another parent), which it isn't glided
    // over. Then the matrix it's drawn with in the frame being drawn, which drawing that is, and whether that's simply its
    // world matrix (neither it nor anything it's in glides).
    private Vector3 _fromPosition;
    private Vector3 _fromRotation;
    private Vector3 _fromScale = Vector3.One;
    private long _movedStep = -1;
    private long _jumpedStep = -1;
    private Matrix4x4 _drawn;
    private long _drawnFor;
    private bool _drawnAsIs;

    /// <summary>Where it is, in world units, from its parent's position (or the world's middle, with none). Y is up.</summary>
    public Vector3 Position
    {
        get => _position;
        set
        {
            if (_position == value)
                return;
            Moving();
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
            Moving();
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
            Moving();
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
                (_local, _localKept) = (Local(), true);
            return _local;
        }
    }

    // Its local matrix as it is now, kept or worked out (and not kept): what drawing a frame reads, writing nothing.
    private Matrix4x4 LocalNow() => _localKept ? _local : Local();

    private Matrix4x4 Local()
    {
        // Most things in a 2D game never turn: they're only scaled and moved, with no turn to work out.
        if (_rotation == Vector3.Zero)
        {
            var local = Matrix4x4.CreateScale(_scale);
            local.Translation = _position;
            return local;
        }

        return Matrix4x4.CreateScale(_scale) *
               Matrix4x4.CreateFromQuaternion(RotationQuaternion) *
               Matrix4x4.CreateTranslation(_position);
    }

    /// <summary>
    /// Transform from this entity's space to world space. Kept until it, or something it's inside, moves. While a running
    /// game's frame is being drawn, it's where the entity is drawn: part way from where it was at the game's last step but
    /// one to where it is (see <see cref="Runtime.Time.Blend"/>).
    /// </summary>
    public Matrix4x4 WorldMatrix => Entity?.Scene is { DrawStamp: not 0 } scene ? Drawn(scene) : World();

    private Matrix4x4 World()
    {
        var parent = Entity?.Parent?.Transform;
        if (parent is null)
        {
            if (!_worldKept || _worldParent is not null)
                Keep(LocalMatrix, null);
            return _world;
        }

        var parentWorld = parent.World();
        if (!_worldKept || _worldParent != parent || _worldParentStamp != parent._worldStamp)
            Keep(LocalMatrix * parentWorld, parent);
        return _world;
    }

    /// <summary>
    /// Moves it straight to <paramref name="position"/> (in its parent's space), drawn there from the next frame on rather
    /// than glided there over the step as ordinary movement is: for a respawn, a portal, wrapping round the screen.
    /// (Moves too far to have been made in one step are drawn that way anyway.)
    /// </summary>
    public void Teleport(Vector3 position)
    {
        Position = position;
        Jumped();
    }

    /// <summary>Like <see cref="Teleport(Vector3)"/>, keeping its Z.</summary>
    public void Teleport(Vector2 position) => Teleport(new Vector3(position, _position.Z));

    // Not glided over the step the game is taking: drawn where it is at the end of it.
    internal void Jumped()
    {
        if (Entity?.Scene is { } scene)
            _jumpedStep = scene.Step;
    }

    // Whether it jumped in `step`, so what it draws then isn't glided either (see StepBlend).
    internal bool JumpedIn(long step) => _jumpedStep == step;

    // About to move: the first time in a step, it remembers where it was as the step began.
    private void Moving()
    {
        if (Entity?.Scene is not { } scene || scene.Step == _movedStep)
            return;
        (_fromPosition, _fromRotation, _fromScale, _movedStep) = (_position, _rotation, _scale, scene.Step);
    }

    // Where it's drawn in the frame being drawn: glided from where it was to where it is if it moved in the last step, in
    // wherever its parent is drawn.
    private Matrix4x4 Drawn(Scene scene)
    {
        if (_drawnFor == scene.DrawStamp)
            return _drawn;

        var glides = _movedStep == scene.Step && _jumpedStep != scene.Step;
        var parent = Entity!.Parent?.Transform;
        Matrix4x4 drawn;
        bool asIs;
        if (parent is null)
        {
            asIs = !glides;
            drawn = asIs ? World() : Glided(scene);
        }
        else
        {
            var parentDrawn = parent.Drawn(scene);
            asIs = !glides && parent._drawnAsIs;
            drawn = asIs ? World() : (glides ? Glided(scene) : LocalMatrix) * parentDrawn;
        }

        (_drawn, _drawnAsIs, _drawnFor) = (drawn, asIs, scene.DrawStamp);
        return drawn;
    }

    /// <summary>
    /// For an entity at the top of the hierarchy: its <see cref="WorldMatrix"/>, without keeping anything it works out, so
    /// threads drawing a big scene's entities at once each write nothing but what they draw (see SceneRenderer).
    /// </summary>
    internal Matrix4x4 RootDrawn(Scene scene)
    {
        if (scene.DrawStamp != 0)
        {
            if (_drawnFor == scene.DrawStamp)
                return _drawn;
            if (_movedStep == scene.Step && _jumpedStep != scene.Step)
                return Glided(scene);
        }

        return _worldKept && _worldParent is null ? _world : LocalNow();
    }

    // Its own matrix part way from where it was as the last step began to where it is. A move too far to have been made
    // in one step (a respawn, a jump across the screen), a big turn or a flip is drawn as it ends up.
    private Matrix4x4 Glided(Scene scene)
    {
        var t = scene.DrawBlend;
        var position = Vector3.DistanceSquared(_fromPosition, _position) <= scene.GlideLimit * scene.GlideLimit
            ? Vector3.Lerp(_fromPosition, _position, t)
            : _position;
        var rotation = GlidedRotation(t);
        var scale = _fromScale == _scale || !SameSigns(_fromScale, _scale) ? _scale : Vector3.Lerp(_fromScale, _scale, t);
        if (rotation == _rotation && scale == _scale)
        {
            var local = LocalNow();
            local.Translation = position;
            return local;
        }

        return Matrix4x4.CreateScale(scale) *
               Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromYawPitchRoll(ToRadians(rotation.Y), ToRadians(rotation.X), ToRadians(rotation.Z))) *
               Matrix4x4.CreateTranslation(position);

        static bool SameSigns(Vector3 a, Vector3 b) =>
            MathF.Sign(a.X) == MathF.Sign(b.X) && MathF.Sign(a.Y) == MathF.Sign(b.Y) && MathF.Sign(a.Z) == MathF.Sign(b.Z);
    }

    // Its turn part way from how it was turned as the last step began, `t` of the way; a big turn is drawn as it ends up.
    private Vector3 GlidedRotation(float t) =>
        _fromRotation == _rotation
            ? _rotation
            : new Vector3(StepBlend.TurnBetween(_fromRotation.X, _rotation.X, t), StepBlend.TurnBetween(_fromRotation.Y, _rotation.Y, t),
                StepBlend.TurnBetween(_fromRotation.Z, _rotation.Z, t));

    /// <summary>
    /// Its own <see cref="Rotation"/> (in its parent's space) in the frame being drawn: between how it was turned as the
    /// last step began and how it is, as <see cref="WorldMatrix"/> turns it.
    /// </summary>
    internal Vector3 RotationDrawn =>
        Entity?.Scene is not { DrawStamp: not 0 } scene || _movedStep != scene.Step || _jumpedStep == scene.Step
            ? _rotation
            : GlidedRotation(scene.DrawBlend);

    /// <summary><see cref="RotationDrawn"/> as a quaternion, like <see cref="RotationQuaternion"/>: for the camera's view.</summary>
    internal Quaternion RotationQuaternionDrawn
    {
        get
        {
            var rotation = RotationDrawn;
            return Quaternion.CreateFromYawPitchRoll(ToRadians(rotation.Y), ToRadians(rotation.X), ToRadians(rotation.Z));
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

    /// <summary>A rotation as angles in degrees about X, Y and Z, as <see cref="Rotation"/> holds them (none for an empty quaternion).</summary>
    public static Vector3 EulerDegrees(Quaternion rotation) => rotation == default ? Vector3.Zero : ToEulerDegrees(rotation);

    /// <summary>The rotation that angles in degrees about X, Y and Z make, as <see cref="Rotation"/> holds them.</summary>
    public static Quaternion FromEulerDegrees(Vector3 degrees) =>
        Quaternion.CreateFromYawPitchRoll(ToRadians(degrees.Y), ToRadians(degrees.X), ToRadians(degrees.Z));

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
