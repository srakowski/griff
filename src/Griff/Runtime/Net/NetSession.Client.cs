using System.Numerics;
using Griff.Networking;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>A player's machine's copy of one of the host's entities.</summary>
internal sealed class Mirror(uint id, byte incarnation, Entity entity, List<NetSlot> slots)
{
    public uint Id { get; } = id;

    public byte Incarnation { get; set; } = incarnation;

    public Entity Entity { get; } = entity;

    public List<NetSlot> Slots { get; set; } = slots;

    public bool OwnMoved { get; set; }

    public bool IsAvatar { get; set; }

    public int OwnerId { get; set; }

    // Where the host said it was, and when (host time), to glide between.
    public List<(double Time, Vector3 Position, Vector3 Rotation, Vector3 Scale)> Samples { get; } = [];

    public bool Settled { get; set; }

    // This player's own character: what the host has of the values this machine decides.
    public object?[] OwnAcked { get; set; } = [];

    public int[] OwnAckedVersion { get; set; } = [];

    public int[] OwnSentVersion { get; set; } = [];

    public object?[] OwnLastSent { get; set; } = [];

    public double[] OwnLastSentAt { get; set; } = [];

    public uint Corrections { get; set; }

    public void StartOwning()
    {
        var count = Slots.Count;
        OwnAcked = Slots.Select(s => s.Get(Entity)).ToArray();
        OwnAckedVersion = new int[count];
        OwnSentVersion = new int[count];
        OwnLastSent = new object?[count];
        OwnLastSentAt = Enumerable.Repeat(double.NegativeInfinity, count).ToArray();
    }
}

// A player's side: the host's world arriving, shown smoothly, and this player's keys and character going back.
public sealed partial class NetSession
{
    private readonly Dictionary<uint, Mirror> _mirrors = [];
    private readonly Dictionary<Entity, Mirror> _mirrorOf = [];
    private readonly InputReport _report = new();
    private int _clientEpoch = -1;
    private int _loadingEpoch = -1;
    private string? _loadingScene;
    private double _hostOffset;
    private bool _haveOffset;
    private (string? Teams, string? Player, float Smoothing, string? ExitScene)? _hostSettings;
    private List<string> _templateParts = [];

    private void ReadSettings(ref NetReader r) => _hostSettings = (r.String(), r.String(), Math.Clamp(r.Float(), 0, 1), r.String());

    /// <summary>The copy of the host's entity numbered <paramref name="id"/>, on a player's machine.</summary>
    internal Entity? MirrorEntity(uint id) => _mirrors.GetValueOrDefault(id)?.Entity;

    private void OnJoined(NetConnection connection)
    {
        if (connection != _hostConnection)
            return;
        try
        {
            var r = new NetReader(connection.AcceptPayload);
            var id = r.VarInt();
            var epoch = r.VarInt();
            var scene = r.String() ?? "";
            ReadSettings(ref r);
            Role = NetRole.Client;
            IsConnecting = false;
            LocalPlayer = new NetPlayer(id, PlayerName) { IsLocal = true };
            _players.Clear();
            _players.Add(LocalPlayer);
            Status = $"Playing on {connection.RemoteEndPoint}";
            LoadHostScene(epoch, scene);
        }
        catch (NetFormatException)
        {
            Leave("That's a different version of the game.");
        }
    }

    private void OnLostHost(NetConnection connection, string reason)
    {
        if (connection != _hostConnection)
            return;
        var wasPlaying = IsClient;
        if (!wasPlaying)
        {
            Fail(reason);
            return;
        }

        var exit = _hostSettings?.ExitScene is { Length: > 0 } hosts ? hosts : Settings?.ExitScene;
        Leave(reason);
        _network.RaiseEnded(reason);
        _network.LeftHost(exit);
    }

    private void StopMirroring()
    {
        foreach (var mirror in _mirrors.Values)
            mirror.Entity.NetFlags = NetFlags.None;
        _mirrors.Clear();
        _mirrorOf.Clear();
        _clientEpoch = -1;
        _loadingEpoch = -1;
        _loadingScene = null;
        _haveOffset = false;
        _hostSettings = null;
    }

    // ----- The host's scene -----

    private void LoadHostScene(int epoch, string scene)
    {
        _loadingEpoch = epoch;
        _loadingScene = scene;
        foreach (var mirror in _mirrors.Values)
            mirror.Entity.NetFlags = NetFlags.None;
        _mirrors.Clear();
        _mirrorOf.Clear();
        _clientEpoch = -1;
        _network.LoadSceneForNetwork(scene);
    }

    /// <summary>Called by the game once a scene the host asked for has loaded (or straight away if it was already showing).</summary>
    internal void HostSceneLoaded()
    {
        if (!IsClient || _loadingEpoch < 0)
            return;

        // Everything shared comes from the host; this machine keeps its camera, HUD and menus.
        var scene = _network.Scene;
        var name = (_hostSettings?.Player ?? Settings?.Player)?.Trim();
        _templateParts = [];
        if (!string.IsNullOrEmpty(name) && EntityNames.Find(scene, name, new NameLookup(Where: static e => e.Parent is null)) is { } template)
        {
            _templateParts = template.SelfAndDescendants().Skip(1).Where(NetSchema.IsLocalItself)
                .Where(e => !NetSchema.IsLocal(e.Parent!) || e.Parent == template)
                .Select(e => Serializer.SerializeEntity(e, links: false)).ToList();
        }

        var shared = scene.Entities.Where(e => !NetSchema.IsLocal(e) && (e.Parent is null || NetSchema.IsLocal(e.Parent))).ToList();
        foreach (var entity in shared)
            scene.Remove(entity);

        _clientEpoch = _loadingEpoch;
        _loadingEpoch = -1;
        _loadingScene = null;
        _network.NetSceneStarted();
        var w = new NetWriter(8);
        w.Byte((byte)NetMessage.Ready);
        w.VarInt(_clientEpoch);
        _hostConnection?.SendReliable(w.Span);
    }

    /// <summary>The scene name the host is waiting for this machine to load, if any.</summary>
    internal string? LoadingScene => _loadingScene;

    // ----- What the host sends -----

    private void OnClientReceived(NetConnection connection, byte[] data)
    {
        if (connection != _hostConnection || !IsClient)
            return;
        try
        {
            var r = new NetReader(data);
            switch ((NetMessage)r.Byte())
            {
                case NetMessage.Players: ReceivePlayers(ref r); break;
                case NetMessage.Scene:
                {
                    var epoch = r.VarInt();
                    var scene = r.String() ?? "";
                    ReadSettings(ref r);
                    LoadHostScene(epoch, scene);
                    break;
                }

                case NetMessage.Spawn: ReceiveSpawn(ref r); break;
                case NetMessage.Despawn: ReceiveDespawn(ref r); break;
                case NetMessage.Rebuild: ReceiveRebuild(ref r); break;
                case NetMessage.Reparent: ReceiveReparent(ref r); break;
                case NetMessage.State: ReceiveState(ref r); break;
                case NetMessage.Correct: ReceiveCorrect(ref r); break;
                case NetMessage.Message:
                {
                    var from = PlayerById(r.VarInt());
                    var name = r.String() ?? "";
                    var payload = ReadData(ref r);
                    var route = ReadRoute(ref r);
                    if (_clientEpoch >= 0 && name.Length > 0 && route is { } heardBy)
                        Deliver(name, payload, from, route: heardBy);
                    break;
                }

                case NetMessage.Sound:
                {
                    var sound = r.String() ?? "";
                    var volume = r.Float();
                    var pitch = r.Float();
                    Vector2? at = r.Bool() ? r.Vector2() : null;
                    _network.PlayRelayedSound(sound, volume, pitch, at);
                    break;
                }

                case NetMessage.Rumble:
                    _network.Input.Rumble(r.Float(), r.Float());
                    break;
                case NetMessage.Shake:
                    _network.ShakeCamera(r.Float(), r.Float());
                    break;
                case NetMessage.HostCommand:
                {
                    var name = r.String() ?? "";
                    var args = ReadData(ref r);
                    _network.RunHostCommand(name, args);
                    break;
                }
            }
        }
        catch (NetFormatException)
        {
            // Out of step with the host for one message; the next will put it right.
        }
    }

    private void ReceivePlayers(ref NetReader r)
    {
        ReadSettings(ref r);
        var before = _players.ToList();
        var count = r.VarUInt();
        var now = new List<NetPlayer>();
        for (var i = 0; i < count; i++)
        {
            var id = r.VarInt();
            var player = before.FirstOrDefault(p => p.Id == id) ?? new NetPlayer(id, "");
            player.Name = r.String() ?? "";
            player.Team = r.String() ?? "";
            player.Color = new Color(r.Float(), r.Float(), r.Float());
            player.Score = r.VarInt();
            player.Ping = r.VarInt();
            player.IsHost = r.Bool();
            player.AvatarId = r.VarUInt();
            player.IsLocal = player.Id == LocalPlayer?.Id;
            player.Avatar = MirrorEntity(player.AvatarId);
            now.Add(player);
        }

        _players.Clear();
        _players.AddRange(now);
        if (now.FirstOrDefault(p => p.IsLocal) is { } me)
            LocalPlayer = me;
        _network.NetPlayersChanged();
        foreach (var joined in now.Where(p => !before.Contains(p) && !p.IsLocal))
            _network.RaisePlayerJoined(joined);
        foreach (var left in before.Where(p => !now.Contains(p) && !p.IsLocal))
            _network.RaisePlayerLeft(left);
    }

    private void ReceiveSpawn(ref NetReader r)
    {
        if (r.VarInt() != _clientEpoch)
            return;
        var id = r.VarUInt();
        var incarnation = r.Byte();
        var parentId = r.VarUInt();
        var index = r.VarInt();
        var flags = r.Byte();
        var owner = r.VarInt();
        var json = r.String() ?? "{}";

        var scene = _network.Scene;
        if (_mirrors.TryGetValue(id, out var old))
            RemoveMirror(old);
        var entity = Serializer.DeserializeEntityKeepingIds(json);
        if (scene.Find(entity.Id) is { } clash)
        {
            if (_mirrorOf.TryGetValue(clash, out var clashing))
                RemoveMirror(clashing);
            else if (clash.Scene == scene)
                scene.Remove(clash);
        }

        var parent = parentId != 0 ? MirrorEntity(parentId) : null;
        var siblings = parent?.Children ?? scene.Roots;
        scene.Add(entity, parent, Math.Clamp(index, 0, siblings.Count));

        var ownerMoves = (flags & 2) != 0;
        var mine = LocalPlayer is { } me && owner == me.Id;
        var mirror = new Mirror(id, incarnation, entity, NetSchema.Build(entity, Registry, ownerMoves))
        {
            OwnMoved = ownerMoves && mine,
            IsAvatar = (flags & 1) != 0,
            OwnerId = owner,
        };
        entity.NetFlags = NetFlags.Replicated | (mirror.OwnMoved ? NetFlags.OwnMoved : NetFlags.None);
        _mirrors[id] = mirror;
        _mirrorOf[entity] = mirror;
        // Where it starts, with no time yet: the first update says when that was.
        mirror.Samples.Add((double.NaN, entity.Transform.Position, entity.Transform.Rotation, entity.Transform.Scale));
        if (mirror.OwnMoved)
            mirror.StartOwning();

        // This player's own character gets the camera or HUD its scene copy had.
        if (mirror.IsAvatar && mine)
        {
            foreach (var part in _templateParts)
                scene.Add(Serializer.DeserializeEntity(part), entity);
        }

        foreach (var player in _players.Where(p => p.AvatarId == id))
            player.Avatar = entity;
    }

    private void ReceiveDespawn(ref NetReader r)
    {
        if (r.VarInt() != _clientEpoch)
            return;
        if (_mirrors.TryGetValue(r.VarUInt(), out var mirror))
            RemoveMirror(mirror);
    }

    private void RemoveMirror(Mirror mirror)
    {
        var entity = mirror.Entity;
        foreach (var e in entity.SelfAndDescendants())
        {
            if (_mirrorOf.Remove(e, out var inner))
                _mirrors.Remove(inner.Id);
        }

        if (entity.Scene == _network.Scene)
            _network.RemoveForNetwork(entity);
        foreach (var player in _players.Where(p => p.Avatar == entity))
            player.Avatar = null;
    }

    private void ReceiveRebuild(ref NetReader r)
    {
        if (r.VarInt() != _clientEpoch)
            return;
        var id = r.VarUInt();
        var incarnation = r.Byte();
        var ownerMoves = (r.Byte() & 2) != 0;
        var json = r.String() ?? "{}";
        if (!_mirrors.TryGetValue(id, out var mirror))
            return;
        var entity = mirror.Entity;
        var fresh = Serializer.DeserializeEntityKeepingIds(json);
        foreach (var component in entity.Components.Skip(1).ToList())
            entity.RemoveComponent(component);
        foreach (var component in fresh.Components.Skip(1).ToList())
        {
            fresh.RemoveComponent(component);
            entity.AddComponent(component);
        }

        entity.Name = fresh.Name;
        entity.IsEnabled = fresh.IsEnabled;
        mirror.Incarnation = incarnation;
        mirror.Slots = NetSchema.Build(entity, Registry, ownerMoves);
        if (mirror.OwnMoved)
            mirror.StartOwning();
    }

    private void ReceiveReparent(ref NetReader r)
    {
        if (r.VarInt() != _clientEpoch)
            return;
        var id = r.VarUInt();
        var parentId = r.VarUInt();
        var index = r.VarInt();
        if (!_mirrors.TryGetValue(id, out var mirror))
            return;
        var parent = parentId != 0 ? MirrorEntity(parentId) : null;
        if (parent is not null && parent.IsSelfOrDescendantOf(mirror.Entity))
            return;
        var siblings = parent?.Children ?? _network.Scene.Roots;
        _network.Scene.Move(mirror.Entity, parent, Math.Clamp(index, 0, siblings.Count));
    }

    private void ReceiveState(ref NetReader r)
    {
        if (r.VarInt() != _clientEpoch)
            return;
        var hostTime = r.Double();
        _statesReceived++;

        // How far the host's clock is ahead of ours, give or take the trip: smoothed, so one slow packet doesn't jolt it.
        var offset = hostTime - _clock;
        if (!_haveOffset || offset > _hostOffset + 0.25 || offset < _hostOffset - 0.5)
            (_hostOffset, _haveOffset) = (offset, true);
        else
            _hostOffset += (offset - _hostOffset) * 0.05;

        while (!r.AtEnd)
        {
            var id = r.VarUInt();
            var incarnation = r.Byte();
            var count = r.VarUInt();
            var mirror = _mirrors.GetValueOrDefault(id);
            if (mirror is null || mirror.Incarnation != incarnation)
            {
                _entriesSkipped++;
                return; // Can't read past values for something we don't have.
            }

            var entity = mirror.Entity;
            Vector3? position = null, rotation = null, scale = null;
            for (var i = 0; i < count; i++)
            {
                var index = (int)r.VarUInt();
                if (index >= mirror.Slots.Count)
                    return;
                var slot = mirror.Slots[index];
                var value = NetCodec.Read(ref r, slot.Type, slot.Get(entity));
                if (mirror.OwnMoved && slot.OwnerOwned)
                    continue;
                switch (slot.Kind)
                {
                    case SlotKind.Position when !mirror.OwnMoved: position = (Vector3)value!; break;
                    case SlotKind.Rotation when !mirror.OwnMoved: rotation = (Vector3)value!; break;
                    case SlotKind.Scale when !mirror.OwnMoved: scale = (Vector3)value!; break;
                    default: slot.Set(entity, value); break;
                }
            }

            if (position is not null || rotation is not null || scale is not null)
            {
                var samples = mirror.Samples;
                var last = samples.Count > 0 ? samples[^1] : (Time: double.NaN, entity.Transform.Position, entity.Transform.Rotation, entity.Transform.Scale);

                // The spawn's position held until just before this first update, then it glides on from there.
                if (double.IsNaN(last.Time))
                {
                    samples.Clear();
                    samples.Add(last with { Time = hostTime - 1.0 / 30 });
                }

                if (hostTime >= samples[^1].Time)
                {
                    samples.Add((hostTime, position ?? last.Position, rotation ?? last.Rotation, scale ?? last.Scale));
                    mirror.Settled = false;
                }
            }
        }
    }

    private void ReceiveCorrect(ref NetReader r)
    {
        if (r.VarInt() != _clientEpoch)
            return;
        var id = r.VarUInt();
        var incarnation = r.Byte();
        var corrections = r.VarUInt();
        var count = r.VarUInt();
        if (!_mirrors.TryGetValue(id, out var mirror) || mirror.Incarnation != incarnation)
            return;
        for (var i = 0; i < count; i++)
        {
            var index = (int)r.VarUInt();
            if (index >= mirror.Slots.Count)
                return;
            var slot = mirror.Slots[index];
            slot.Set(mirror.Entity, NetCodec.Read(ref r, slot.Type, slot.Get(mirror.Entity)));
        }

        mirror.Corrections = Math.Max(mirror.Corrections, corrections);
    }

    private double HostNow() => _haveOffset ? _clock + _hostOffset : 0;

    // ----- Each frame -----

    private void BeginClientFrame()
    {
        if (_clientEpoch < 0)
            return;
        foreach (var player in _players)
            player.Avatar = MirrorEntity(player.AvatarId);

        // Everything else is shown a moment behind the host, between two updates, so it moves smoothly.
        var smoothing = _hostSettings?.Smoothing ?? Settings?.Smoothing ?? 0.1f;
        var at = HostNow() - Math.Max(0, smoothing);
        foreach (var mirror in _mirrors.Values)
        {
            if (mirror.OwnMoved || mirror.Settled || mirror.Samples.Count == 0)
                continue;
            var samples = mirror.Samples;
            while (samples.Count > 2 && samples[1].Time <= at)
                samples.RemoveAt(0);
            var transform = mirror.Entity.Transform;

            // Not hearing from the host yet (no clock to go by): the latest it said.
            if (!_haveOffset)
            {
                var (_, latest, turned, sized) = samples[^1];
                (transform.Position, transform.Rotation, transform.Scale) = (latest, turned, sized);
                continue;
            }

            var (t0, p0, r0, s0) = samples[0];
            if (samples.Count == 1 || at <= t0 || double.IsNaN(t0))
            {
                (transform.Position, transform.Rotation, transform.Scale) = (p0, r0, s0);
                mirror.Settled = samples.Count == 1;
                continue;
            }

            var (t1, p1, r1, s1) = samples[1];
            var span = t1 - t0;
            var k = span > 1e-6 ? (float)Math.Clamp((at - t0) / span, 0, 1) : 1;

            // A jump too far to have been walked (a respawn, a teleport) snaps instead of sliding there.
            var jumped = span > 0 && Vector3.Distance(p0, p1) / span > 60;
            if (jumped)
                k = 1;
            transform.Position = Vector3.Lerp(p0, p1, k);
            transform.Rotation = new Vector3(Angle(r0.X, r1.X, k), Angle(r0.Y, r1.Y, k), Angle(r0.Z, r1.Z, k));
            transform.Scale = Vector3.Lerp(s0, s1, k);
            if (jumped)
                transform.Jumped();
            if (k >= 1 && samples.Count == 2)
            {
                samples.RemoveAt(0);
                mirror.Settled = true;
            }
        }

        static float Angle(float from, float to, float k)
        {
            var delta = ((to - from) % 360 + 540) % 360 - 180;
            return from + delta * k;
        }
    }

    private void EndClientFrame()
    {
        if (_hostConnection is not { State: NetConnectionState.Connected } connection || _clientEpoch < 0)
            return;

        var input = _network.Input;
        var blocked = _network.LocalInputBlocked;
        _report.Sample(input, blocked);
        var (data, acked) = _report.Write(input, blocked, _network.MouseOnUi, _network.View);
        connection.SendUnreliable(data, acked);

        SendOwnState(connection);
    }

    // This player's own character: where this machine moved it, for the host to pass on.
    private void SendOwnState(NetConnection connection)
    {
        var resend = Math.Clamp(connection.RoundTrip * 1.25 + 0.02, 0.05, 0.5);
        var w = new NetWriter(256);
        var entries = new NetWriter(256);
        var count = 0u;
        var carried = new List<(Mirror Mirror, int Slot, int Version, object? Value)>();
        foreach (var mirror in _mirrors.Values.Where(m => m.OwnMoved))
        {
            var changes = new List<int>();
            for (var i = 0; i < mirror.Slots.Count; i++)
            {
                if (!mirror.Slots[i].OwnerOwned)
                    continue;
                var value = mirror.Slots[i].Get(mirror.Entity);
                if (NetCodec.Same(value, mirror.OwnAcked[i]))
                    continue;
                if (NetCodec.Same(value, mirror.OwnLastSent[i]) && _clock - mirror.OwnLastSentAt[i] < resend)
                    continue;
                changes.Add(i);
            }

            if (changes.Count == 0)
                continue;
            count++;
            entries.VarUInt(mirror.Id);
            entries.Byte(mirror.Incarnation);
            entries.VarUInt(mirror.Corrections);
            entries.VarUInt((uint)changes.Count);
            foreach (var i in changes)
            {
                var slot = mirror.Slots[i];
                var value = slot.Get(mirror.Entity);
                entries.VarUInt((uint)i);
                NetCodec.Write(entries, slot.Type, value);
                mirror.OwnLastSent[i] = value;
                mirror.OwnLastSentAt[i] = _clock;
                carried.Add((mirror, i, ++mirror.OwnSentVersion[i], value));
            }
        }

        if (count == 0)
            return;
        w.Byte((byte)NetMessage.OwnerState);
        w.VarInt(_clientEpoch);
        w.VarUInt(count);
        w.Raw(entries.Span);
        if (w.Length > NetConnection.MaxUnreliable)
            return;
        connection.SendUnreliable(w.Span, () =>
        {
            foreach (var (mirror, slot, version, value) in carried)
            {
                if (slot < mirror.OwnAcked.Length && version > mirror.OwnAckedVersion[slot])
                {
                    mirror.OwnAcked[slot] = value;
                    mirror.OwnAckedVersion[slot] = version;
                }
            }
        });
    }

    // For diagnostics: how the copies are doing.
    private int _statesReceived;
    private int _entriesSkipped;

    /// <summary>A line about the session for logs: who's here, and on a player's machine how the host's updates are arriving.</summary>
    public string Describe()
    {
        var players = string.Join(", ", _players.Select(p => $"{p.Name}#{p.Id}{(p.IsLocal ? "*" : "")} avatar {p.AvatarId}{(p.Avatar is { } a ? $" at {a.Transform.Position.X:0.0},{a.Transform.Position.Y:0.0}" : "")}"));
        if (!IsClient)
            return $"{Role} epoch {_epoch}: {players}; {_objects.Count} objects";
        return $"{Role} epoch {_clientEpoch}: {players}; {_mirrors.Count} copies, {_statesReceived} updates ({_entriesSkipped} cut short), host clock {HostNow():0.00}";
    }

    /// <summary>True on a player's machine for a copy of the host's entity (the host runs it; this machine only shows it).</summary>
    internal bool IsMirror(Entity entity) => _mirrorOf.ContainsKey(entity);
}
