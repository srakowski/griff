using System.Net;
using System.Numerics;
using System.Text.Json;
using Griff.Networking;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>An entity the host sends to players, with the values it had at the last send.</summary>
internal sealed class NetObject(uint id, Entity entity)
{
    public uint Id { get; } = id;

    public Entity Entity { get; } = entity;

    public List<NetSlot> Slots { get; set; } = [];

    public object?[] Current { get; set; } = [];

    // The components it had when its slots were made, to notice one added or removed.
    public Component[] Shape { get; set; } = [];

    public int ShapeVersion { get; set; }

    public uint ParentId { get; set; }

    public int ParentVersion { get; set; }

    public int SeenTick { get; set; }

    public byte Spawns { get; set; }

    public NetPlayer? Owner { get; set; }

    public bool OwnerMoves { get; set; }

    public bool IsAvatar { get; set; }

    public bool Renderable { get; set; }

    public bool AlwaysSent { get; set; }

    // For a character its player moves: the values that player last reported, and how many times the host has overruled them.
    public object?[]? FromOwner { get; set; }

    public uint Corrections { get; set; }
}

/// <summary>What one player's machine has of a <see cref="NetObject"/>, as far as the host knows.</summary>
internal sealed class Ghost
{
    public Ghost(NetObject obj, byte incarnation)
    {
        Object = obj;
        Incarnation = incarnation;
        Reset(obj);
    }

    public NetObject Object { get; }

    public byte Incarnation { get; }

    /// <summary>True once the player's machine has it (and everything sent before it).</summary>
    public bool Spawned { get; set; }

    public int ShapeVersion { get; set; }

    public int ParentVersion { get; set; }

    public object?[] Acked { get; private set; } = [];

    public int[] AckedVersion { get; private set; } = [];

    public int[] SentVersion { get; private set; } = [];

    public object?[] LastSent { get; private set; } = [];

    public double[] LastSentAt { get; private set; } = [];

    public List<(int Version, object? Value)>?[] InFlight { get; private set; } = [];

    /// <summary>The player has everything as it is now (it was just sent whole).</summary>
    public void Reset(NetObject obj)
    {
        var count = obj.Slots.Count;
        Acked = (object?[])obj.Current.Clone();
        AckedVersion = new int[count];
        SentVersion = new int[count];
        LastSent = new object?[count];
        LastSentAt = Enumerable.Repeat(double.NegativeInfinity, count).ToArray();
        InFlight = new List<(int, object?)>?[count];
        ShapeVersion = obj.ShapeVersion;
        ParentVersion = obj.ParentVersion;
    }
}

// The host's side: who's in, their characters, and sending each of them what changed.
public sealed partial class NetSession
{
    private static readonly Color[] PlayerColors =
    [
        Color.FromBytes(0x4C, 0x8D, 0xFF), Color.FromBytes(0xFF, 0x5A, 0x4C), Color.FromBytes(0x57, 0xD3, 0x6B),
        Color.FromBytes(0xF2, 0xC9, 0x4C), Color.FromBytes(0xB5, 0x7B, 0xFF), Color.FromBytes(0xFF, 0x9A, 0x3C),
        Color.FromBytes(0x4C, 0xD9, 0xE8), Color.FromBytes(0xFF, 0x7E, 0xB6),
    ];

    private const string RememberedSaveKey = "griff.net.players";

    private readonly Dictionary<Entity, NetObject?> _known = [];
    private readonly List<NetObject> _objects = [];
    private readonly Dictionary<uint, NetObject> _objectsById = [];
    private readonly Dictionary<Entity, NetPlayer> _avatarOwners = [];
    private readonly Dictionary<EndPoint, NetPlayer> _joining = [];
    private uint _nextObjectId = 1;
    private int _syncTick;
    private int _epoch;
    private int _nextPlayerId = 1;
    private bool _playersDirty;
    private double _sinceSync;
    private double _sincePlayers;
    private Entity? _template;
    private string? _templateJson;

    /// <summary>The players' characters' owners, for the game's own lookups.</summary>
    internal NetPlayer? AvatarOwner(Entity entity) => _avatarOwners.GetValueOrDefault(entity);

    /// <summary>
    /// Gives a player a new character (on the host), e.g. after theirs was destroyed: at a <see cref="PlayerSpawn"/>, like
    /// when they joined. Replaces any they have.
    /// </summary>
    public Entity? Respawn(NetPlayer player)
    {
        if (!IsHost)
            return null;
        if (player.Avatar is { } old && old.Scene == _game.Scene && old != _template)
            _game.Destroy(old);
        player.Avatar = null;
        return SpawnAvatar(player, fresh: true);
    }

    /// <summary>
    /// Tells one player's machine something (what the host's run of their tools wants shown there): <paramref name="name"/>
    /// is run there with <paramref name="data"/> (see <see cref="GameSetup.OnHostCommand"/>), once they're in this scene.
    /// </summary>
    internal void SendCommandTo(NetPlayer player, string name, object? data)
    {
        if (!IsHost || player.Connection is not { } connection || player.ReadyEpoch != _epoch)
            return;
        var w = new NetWriter();
        w.Byte((byte)NetMessage.HostCommand);
        w.String(name);
        WriteData(w, data);
        connection.SendReliable(w.Span);
    }

    /// <summary>Shakes one player's camera, on their own machine.</summary>
    internal void SendShake(NetPlayer player, float strength, float duration)
    {
        if (!IsHost || player.Connection is not { } connection)
            return;
        var w = new NetWriter(16);
        w.Byte((byte)NetMessage.Shake);
        w.Float(strength);
        w.Float(duration);
        connection.SendUnreliable(w.Span);
    }

    /// <summary>
    /// Something with health was defeated (on the host): a point for the player whose blow it was, if it wasn't their own
    /// (see <see cref="Multiplayer.ScoreKills"/>).
    /// </summary>
    internal void CountDefeat(Entity defeated, Entity? by)
    {
        if (!IsHost || Settings is not { ScoreKills: true } || by is null || OwnerOf(by) is not { } scorer)
            return;
        var victim = OwnerOf(defeated);
        if (victim == scorer || (victim is not null && victim.Team.Length > 0 && victim.Team.Equals(scorer.Team, StringComparison.OrdinalIgnoreCase)))
            return;
        AddScore(scorer, 1);
    }

    // ----- Joining and leaving -----

    private NetConnectReply AcceptPlayer(NetConnectRequest request)
    {
        var max = Math.Max(1, Settings?.MaxPlayers ?? 8);
        if (_players.Count + _joining.Count >= max)
            return NetConnectReply.Refuse($"The game is full ({max} players).");

        string name;
        try
        {
            var reader = new NetReader(request.Payload);
            name = (reader.String() ?? "").Trim();
        }
        catch (NetFormatException)
        {
            return NetConnectReply.Refuse("That's a different version of the game.");
        }

        if (name.Length == 0)
            name = "Player";
        if (name.Length > 24)
            name = name[..24];
        var unique = name;
        for (var n = 2; _players.Any(p => p.Name.Equals(unique, StringComparison.OrdinalIgnoreCase)) ||
                        _joining.Values.Any(p => p.Name.Equals(unique, StringComparison.OrdinalIgnoreCase)); n++)
            unique = $"{name} {n}";

        var player = new NetPlayer(_nextPlayerId++, unique);
        _joining[request.From] = player;
        var w = new NetWriter();
        w.VarInt(player.Id);
        w.VarInt(_epoch);
        w.String(_game.SceneName ?? "");
        WriteSettings(w);
        return NetConnectReply.Accept(w.ToArray());
    }

    private void OnPlayerConnected(NetConnection connection)
    {
        if (!_joining.Remove(connection.RemoteEndPoint, out var player))
        {
            connection.Disconnect("Something went wrong joining.");
            return;
        }

        connection.Tag = player;
        player.Connection = connection;
        player.Input = new RemoteInput(_game.Input);
        _players.Add(player);
        ChooseTeam(player);
        SpawnAvatar(player, fresh: true);
        SendPlayers();
        Joined(player);
    }

    private void Joined(NetPlayer player)
    {
        PlayerJoined?.Invoke(player);
        _game.Send("PlayerJoined", player);
    }

    private void OnPlayerDisconnected(NetConnection connection, string reason)
    {
        if (connection.Tag is not NetPlayer player || !_players.Contains(player))
            return;
        PlayerLeft?.Invoke(player);
        _game.Send("PlayerLeft", player);
        Remember(player);
        if (player.Avatar is { } avatar && avatar.Scene == _game.Scene)
            _game.Destroy(avatar);
        player.Avatar = null;
        _players.Remove(player);
        player.Connection = null;
        SendPlayers();
    }

    private void StopHosting()
    {
        foreach (var player in _players.Where(p => !p.IsLocal))
        {
            Remember(player);
            if (player.Avatar is { } avatar && avatar.Scene == _game.Scene)
                _game.Destroy(avatar);
        }

        foreach (var entity in _avatarOwners.Keys)
            entity.NetFlags &= ~NetFlags.RemoteMoved;
        _known.Clear();
        _objects.Clear();
        _objectsById.Clear();
        _avatarOwners.Clear();
        _joining.Clear();
        _template = null;
        _templateJson = null;
    }

    // ----- Scenes -----

    /// <summary>The host's scene changed (or hosting began): everyone loads it, and each player gets a character in it.</summary>
    internal void StartHostScene()
    {
        _epoch++;
        _known.Clear();
        _objects.Clear();
        _objectsById.Clear();
        _avatarOwners.Clear();
        _sinceSync = double.PositiveInfinity;

        var w = new NetWriter();
        w.Byte((byte)NetMessage.Scene);
        w.VarInt(_epoch);
        w.String(_game.SceneName ?? "");
        WriteSettings(w);
        foreach (var player in _players.Where(p => p.Connection is not null))
        {
            player.Ghosts.Clear();
            player.ReadyEpoch = -1;
            player.Connection!.SendReliable(w.Span);
        }

        var name = Settings?.Player?.Trim();
        _template = string.IsNullOrEmpty(name) ? null : _game.Scene.Roots.FirstOrDefault(e => e.Name == name);
        _templateJson = _template is null ? null : Serializer.SerializeEntity(_template);
        if (Role == NetRole.Server && _template is not null)
            _game.Destroy(_template);

        foreach (var player in _players)
        {
            player.Avatar = null;
            SpawnAvatar(player, fresh: false);
        }

        _game.NetSceneStarted();
    }

    private Entity? SpawnAvatar(NetPlayer player, bool fresh)
    {
        var name = Settings?.Player?.Trim();
        if (string.IsNullOrEmpty(name))
            return null;

        Entity? avatar = null;
        if (player.IsLocal && !fresh && _template is { } own && own.Scene == _game.Scene)
        {
            avatar = own;
            Place(avatar, player);
        }
        else
        {
            var remembered = Recall(player);
            try
            {
                avatar = remembered is not null ? Serializer.DeserializeEntity(remembered)
                    : _templateJson is not null ? Serializer.DeserializeEntity(_templateJson)
                    : _game.PrefabLoader?.Invoke(name);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or JsonException)
            {
                avatar = null;
            }

            if (avatar is null)
                return null;
            if (!player.IsLocal)
                RemoveLocalParts(avatar);
            if (remembered is null)
                Place(avatar, player);
            _game.Spawn(avatar);
        }

        var networked = avatar.GetComponent<Networked>() ?? avatar.AddComponent<Networked>();
        networked.Owner = player.Id;
        player.Avatar = avatar;
        _avatarOwners[avatar] = player;
        if (!player.IsLocal && OwnerMoves(avatar))
            avatar.NetFlags |= NetFlags.RemoteMoved;
        Paint(avatar, player);
        return avatar;
    }

    private static bool OwnerMoves(Entity avatar) => avatar.GetComponent<Networked>()?.Movement switch
    {
        NetMovement.Owner => true,
        NetMovement.Host => false,
        _ => NetSchema.HasMovement(avatar),
    };

    // A camera or HUD on the character is for the player playing it, on their own machine.
    private static void RemoveLocalParts(Entity avatar)
    {
        foreach (var child in avatar.SelfAndDescendants().Skip(1).Where(NetSchema.IsLocalItself).ToList())
        {
            if (child.Parent is { } parent)
                parent.ChildList.Remove(child);
            child.Parent = null;
        }
    }

    private void Place(Entity avatar, NetPlayer player)
    {
        var spawns = _game.Scene.Active<PlayerSpawn>()
            .Where(s => string.IsNullOrWhiteSpace(s.Team) || s.Team.Trim().Equals(player.Team, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var others = _players.Where(p => p != player && p.Avatar?.Scene == _game.Scene).Select(p => Flat(p.Avatar!.Transform.WorldPosition)).ToList();
        Vector2 at;
        if (spawns.Count > 0)
        {
            // The spawn furthest from everyone else, so players don't land on top of each other.
            var best = spawns.OrderByDescending(s => others.Count == 0 ? -Array.IndexOf(spawns.ToArray(), s)
                : others.Min(o => Vector2.Distance(o, Flat(s.Entity!.Transform.WorldPosition)))).First();
            at = Flat(best.Entity!.Transform.WorldPosition);
        }
        else
        {
            var index = Math.Max(0, _players.IndexOf(player));
            at = Flat(avatar.Transform.Position) + new Vector2(index * 1.5f, 0);
        }

        avatar.Transform.Position = new Vector3(at, avatar.Transform.Position.Z);
    }

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Y);

    private static int IndexIn(IReadOnlyList<Entity> siblings, Entity entity)
    {
        for (var i = 0; i < siblings.Count; i++)
        {
            if (siblings[i] == entity)
                return i;
        }

        return -1;
    }

    private void ChooseTeam(NetPlayer player)
    {
        var teams = TeamNames();
        if (teams.Count > 0)
        {
            player.Team = teams.OrderBy(t => _players.Count(p => p != player && p.Team.Equals(t, StringComparison.OrdinalIgnoreCase)))
                .ThenBy(teams.IndexOf).First();
        }

        player.Color = ColorFor(player);
    }

    internal List<string> TeamNames() =>
        ((IsClient ? _hostSettings?.Teams : null) ?? Settings?.Teams ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // The host's Multiplayer settings that players' machines go by, whatever their own copy of the scene says.
    private void WriteSettings(NetWriter w)
    {
        var settings = Settings;
        w.String(settings?.Teams);
        w.String(settings?.Player);
        w.Float(settings?.Smoothing ?? 0.1f);
        w.String(settings?.ExitScene);
    }

    private Color ColorFor(NetPlayer player)
    {
        var teams = TeamNames();
        var index = teams.FindIndex(t => t.Equals(player.Team, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? PlayerColors[index % PlayerColors.Length] : PlayerColors[(player.Id - 1) % PlayerColors.Length];
    }

    private void Paint(Entity avatar, NetPlayer player)
    {
        if (Settings is not { ColorPlayers: true })
            return;
        var parts = avatar.SelfAndDescendants().Where(e => e.Name == "Team Color").SelectMany(e => e.GetComponents<Sprite>()).ToList();
        if (parts.Count == 0 && avatar.GetComponent<Sprite>() is { } sprite)
            parts.Add(sprite);
        foreach (var part in parts)
            part.Tint = player.Color with { A = part.Tint.A };
    }

    // ----- Persistent players -----

    private void Remember(NetPlayer player)
    {
        if (Settings is not { RememberPlayers: true } || player.Avatar is not { } avatar || avatar.Scene != _game.Scene)
            return;
        var all = LoadRemembered();
        all[RememberKey(player)] = Serializer.SerializeEntity(avatar);
        _game.SaveData.Set(RememberedSaveKey, JsonSerializer.Serialize(all));
    }

    private string? Recall(NetPlayer player) =>
        Settings is { RememberPlayers: true } && LoadRemembered().TryGetValue(RememberKey(player), out var json) ? json : null;

    private string RememberKey(NetPlayer player) => $"{_game.SceneName}/{player.Name.ToLowerInvariant()}";

    private Dictionary<string, string> LoadRemembered()
    {
        try
        {
            return _game.SaveData.GetString(RememberedSaveKey) is { } json
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ----- Each frame -----

    private void BeginHostFrame(float seconds)
    {
        foreach (var player in _players)
            player.Input?.BeginFrame(seconds);
    }

    private void EndHostFrame(float seconds)
    {
        foreach (var player in _players)
        {
            if (player.Input?.PendingRumble is { } rumble && player.Connection is { } connection)
            {
                var w = new NetWriter(16);
                w.Byte((byte)NetMessage.Rumble);
                w.Float(rumble.Strength);
                w.Float(rumble.Seconds);
                connection.SendUnreliable(w.Span);
                player.Input.PendingRumble = null;
            }
        }

        var rate = Math.Clamp(Settings?.SendRate ?? 30, 1, 120);
        _sinceSync += seconds;
        if (_sinceSync >= 1 / rate)
        {
            _sinceSync = _sinceSync > 2 / rate ? 0 : _sinceSync - 1 / rate;
            Sync();
        }

        _sincePlayers += seconds;
        if (_playersDirty || _sincePlayers >= 1)
            SendPlayers();
    }

    private void SendPlayers()
    {
        _playersDirty = false;
        _sincePlayers = 0;
        foreach (var player in _players)
        {
            if (player.Connection is { } c)
                player.Ping = c.Ping;
        }

        var w = new NetWriter();
        w.Byte((byte)NetMessage.Players);
        WriteSettings(w);
        w.VarUInt((uint)_players.Count);
        foreach (var player in _players)
        {
            w.VarInt(player.Id);
            w.String(player.Name);
            w.String(player.Team);
            w.Float(player.Color.R);
            w.Float(player.Color.G);
            w.Float(player.Color.B);
            w.VarInt(player.Score);
            w.VarInt(player.Ping);
            w.Bool(player.IsHost);
            w.VarUInt(player.Avatar is { } avatar && _known.GetValueOrDefault(avatar) is { } obj ? obj.Id : 0);
        }

        foreach (var player in _players.Where(p => p.Connection is not null))
            player.Connection!.SendReliable(w.Span);
    }

    // ----- What each player gets -----

    private void Sync()
    {
        _syncTick++;
        var scene = _game.Scene;
        var order = new List<NetObject>(_objects.Count);
        foreach (var entity in scene.Entities)
        {
            if (!_known.TryGetValue(entity, out var obj))
            {
                obj = Track(entity);
                _known[entity] = obj;
            }

            if (obj is null)
                continue;
            obj.SeenTick = _syncTick;
            order.Add(obj);
            CheckShape(obj);
            CheckParent(obj);
            Capture(obj);
        }

        foreach (var gone in _objects.Where(o => o.SeenTick != _syncTick).ToList())
            Gone(gone);
        if (_syncTick % 60 == 0)
        {
            foreach (var stale in _known.Keys.Where(e => e.Scene != scene).ToList())
                _known.Remove(stale);
        }

        // An avatar that has just been added is in the list now: tell everyone whose it is.
        if (_players.Any(p => p.Avatar is { } a && _known.GetValueOrDefault(a) is { } o && o.Id != p.AvatarId))
        {
            foreach (var player in _players)
                player.AvatarId = player.Avatar is { } a && _known.GetValueOrDefault(a) is { } o ? o.Id : 0;
            _playersDirty = true;
        }

        foreach (var player in _players)
        {
            if (player.Connection is { State: NetConnectionState.Connected } && player.ReadyEpoch == _epoch)
                SendTo(player, order);
        }
    }

    private NetObject? Track(Entity entity)
    {
        if (NetSchema.IsLocal(entity))
            return null;
        var owner = _avatarOwners.GetValueOrDefault(entity);
        var ownerMoves = owner is { IsLocal: false } && entity.NetFlags.HasFlag(NetFlags.RemoteMoved);
        var obj = new NetObject(_nextObjectId++, entity)
        {
            Owner = owner,
            OwnerMoves = ownerMoves,
            IsAvatar = owner is not null,
            Renderable = entity.Components.Any(c => c is Sprite or Text or Tilemap or ParticleEmitter or Light or Trail),
            AlwaysSent = entity.GetComponent<Networked>() is { AlwaysSent: true } || entity.GetComponent<Tilemap>() is not null,
            ParentId = entity.Parent is { } parent && _known.GetValueOrDefault(parent) is { } p ? p.Id : 0,
        };
        MakeSlots(obj);
        _objects.Add(obj);
        _objectsById[obj.Id] = obj;
        return obj;
    }

    private void MakeSlots(NetObject obj)
    {
        obj.Slots = NetSchema.Build(obj.Entity, Registry, obj.OwnerMoves);
        obj.Shape = [.. obj.Entity.Components];
        obj.Current = new object?[obj.Slots.Count];
        for (var i = 0; i < obj.Slots.Count; i++)
            obj.Current[i] = obj.Slots[i].Get(obj.Entity);
        obj.FromOwner = obj.OwnerMoves ? (object?[])obj.Current.Clone() : null;
    }

    private void CheckShape(NetObject obj)
    {
        var components = obj.Entity.Components;
        var same = components.Count == obj.Shape.Length;
        for (var i = 0; same && i < components.Count; i++)
            same = ReferenceEquals(components[i], obj.Shape[i]);
        if (same)
            return;
        MakeSlots(obj);
        obj.ShapeVersion++;
    }

    private void CheckParent(NetObject obj)
    {
        var parent = obj.Entity.Parent is { } p && _known.GetValueOrDefault(p) is { } po ? po.Id : 0;
        if (parent == obj.ParentId)
            return;
        obj.ParentId = parent;
        obj.ParentVersion++;
    }

    private void Capture(NetObject obj)
    {
        List<int>? overruled = null;
        for (var i = 0; i < obj.Slots.Count; i++)
        {
            var value = obj.Slots[i].Get(obj.Entity);
            obj.Current[i] = value;

            // The host changed something the player's own machine decides (respawned them, knocked them back): tell it.
            if (obj.FromOwner is { } from && obj.Slots[i].OwnerOwned && !NetCodec.Same(value, from[i]))
            {
                from[i] = value;
                (overruled ??= []).Add(i);
            }
        }

        if (overruled is null || obj.Owner is not { Connection: { } connection } owner || owner.Ghosts.GetValueOrDefault(obj) is not { Spawned: true } ghost)
            return;
        obj.Corrections++;
        var w = new NetWriter();
        w.Byte((byte)NetMessage.Correct);
        w.VarInt(_epoch);
        w.VarUInt(obj.Id);
        w.Byte(ghost.Incarnation);
        w.VarUInt(obj.Corrections);
        w.VarUInt((uint)overruled.Count);
        foreach (var i in overruled)
        {
            w.VarUInt((uint)i);
            NetCodec.Write(w, obj.Slots[i].Type, obj.Current[i]);
        }

        connection.SendReliable(w.Span);
    }

    private void Gone(NetObject obj)
    {
        foreach (var player in _players)
        {
            if (player.Ghosts.Remove(obj) && player.Connection is { } connection)
                SendDespawn(connection, obj);
        }

        _objects.Remove(obj);
        _objectsById.Remove(obj.Id);
        _known.Remove(obj.Entity);
        if (_avatarOwners.Remove(obj.Entity, out var owner) && owner.Avatar == obj.Entity)
        {
            owner.Avatar = null;
            _playersDirty = true;
        }
    }

    private void SendTo(NetPlayer player, List<NetObject> order)
    {
        var connection = player.Connection!;
        var eyes = Eyes(player);
        var range = Settings?.ViewDistance ?? 0;
        var relevant = new Dictionary<NetObject, bool>();
        var state = new StateWriter(this, player, connection);
        foreach (var obj in order)
        {
            var parent = obj.ParentId != 0 ? _objectsById.GetValueOrDefault(obj.ParentId) : null;
            var seen = player.Ghosts.TryGetValue(obj, out var ghost);
            var visible = (parent is null || relevant.GetValueOrDefault(parent)) && IsRelevant(obj, player, eyes, range, seen);
            relevant[obj] = visible;
            if (!seen)
            {
                if (!visible || (parent is not null && !player.Ghosts.ContainsKey(parent)))
                    continue;
                SendSpawn(player, connection, obj, parent);
                continue;
            }

            if (!visible)
            {
                SendDespawn(connection, obj);
                ForgetTree(player, obj);
                continue;
            }

            if (ghost!.ShapeVersion != obj.ShapeVersion)
            {
                SendRebuild(player, connection, obj, ghost);
                continue;
            }

            if (ghost.ParentVersion != obj.ParentVersion)
            {
                if (parent is not null && !player.Ghosts.ContainsKey(parent))
                {
                    SendDespawn(connection, obj);
                    ForgetTree(player, obj);
                    continue;
                }

                SendReparent(connection, obj);
                ghost.ParentVersion = obj.ParentVersion;
            }

            if (ghost.Spawned)
                state.Add(obj, ghost, skipOwned: obj.OwnerMoves && obj.Owner == player);
        }

        state.Flush();
    }

    // Where a player is looking from: their character, and the middle of their camera's view.
    private List<Vector2> Eyes(NetPlayer player)
    {
        var eyes = new List<Vector2>();
        if (player.Avatar is { } avatar && avatar.Scene == _game.Scene)
            eyes.Add(Flat(avatar.Transform.WorldPosition));
        if (player.Input is { HasView: true } input)
            eyes.Add(input.View.Center);
        return eyes;
    }

    private static bool IsRelevant(NetObject obj, NetPlayer player, List<Vector2> eyes, float range, bool alreadySent)
    {
        if (range <= 0 || eyes.Count == 0 || obj.AlwaysSent || !obj.Renderable || obj.Owner == player)
            return true;
        // A little further to stop being sent than to start, so things on the edge don't flicker.
        var reach = alreadySent ? range * 1.15f : range;
        var at = Flat(obj.Entity.Transform.WorldPosition);
        return eyes.Any(e => Vector2.DistanceSquared(e, at) <= reach * reach);
    }

    private static void ForgetTree(NetPlayer player, NetObject obj)
    {
        player.Ghosts.Remove(obj);
        foreach (var other in player.Ghosts.Keys.Where(o => o.Entity != obj.Entity && o.Entity.IsSelfOrDescendantOf(obj.Entity)).ToList())
            player.Ghosts.Remove(other);
    }

    private void SendSpawn(NetPlayer player, NetConnection connection, NetObject obj, NetObject? parent)
    {
        var ghost = new Ghost(obj, ++obj.Spawns);
        player.Ghosts[obj] = ghost;
        var entity = obj.Entity;
        var siblings = entity.Parent?.Children ?? _game.Scene.Roots;
        var w = new NetWriter(512);
        w.Byte((byte)NetMessage.Spawn);
        w.VarInt(_epoch);
        w.VarUInt(obj.Id);
        w.Byte(ghost.Incarnation);
        w.VarUInt(parent?.Id ?? 0);
        w.VarInt(IndexIn(siblings, entity));
        w.Byte((byte)((obj.IsAvatar ? 1 : 0) | (obj.OwnerMoves ? 2 : 0)));
        w.VarInt(obj.Owner?.Id ?? 0);
        w.String(Serializer.SerializeEntityAlone(entity));
        connection.SendReliable(w.Span, () =>
        {
            if (player.Ghosts.GetValueOrDefault(obj) == ghost)
                ghost.Spawned = true;
        });
    }

    private void SendRebuild(NetPlayer player, NetConnection connection, NetObject obj, Ghost ghost)
    {
        ghost.Spawned = false;
        ghost.Reset(obj);
        var w = new NetWriter(512);
        w.Byte((byte)NetMessage.Rebuild);
        w.VarInt(_epoch);
        w.VarUInt(obj.Id);
        w.Byte(ghost.Incarnation);
        w.Byte((byte)(obj.OwnerMoves ? 2 : 0));
        w.String(Serializer.SerializeEntityAlone(obj.Entity));
        connection.SendReliable(w.Span, () =>
        {
            if (player.Ghosts.GetValueOrDefault(obj) == ghost)
                ghost.Spawned = true;
        });
    }

    private void SendReparent(NetConnection connection, NetObject obj)
    {
        var entity = obj.Entity;
        var siblings = entity.Parent?.Children ?? _game.Scene.Roots;
        var w = new NetWriter(16);
        w.Byte((byte)NetMessage.Reparent);
        w.VarInt(_epoch);
        w.VarUInt(obj.Id);
        w.VarUInt(obj.ParentId);
        w.VarInt(IndexIn(siblings, entity));
        connection.SendReliable(w.Span);
    }

    private void SendDespawn(NetConnection connection, NetObject obj)
    {
        var w = new NetWriter(8);
        w.Byte((byte)NetMessage.Despawn);
        w.VarInt(_epoch);
        w.VarUInt(obj.Id);
        connection.SendReliable(w.Span);
    }

    /// <summary>Packs changed values into packets for one player, noting what each carried so arrivals can be counted.</summary>
    private sealed class StateWriter(NetSession session, NetPlayer player, NetConnection connection)
    {
        private readonly NetWriter _packet = new(NetConnection.MaxUnreliable);
        private readonly NetWriter _entry = new(256);
        private List<(Ghost Ghost, int Slot, int Version, object? Value)> _carried = [];

        public void Add(NetObject obj, Ghost ghost, bool skipOwned)
        {
            var now = session._clock;
            var resend = Math.Clamp(connection.RoundTrip * 1.25 + 0.02, 0.05, 0.5);
            var changes = new List<int>();
            for (var i = 0; i < obj.Slots.Count; i++)
            {
                if (skipOwned && obj.Slots[i].OwnerOwned)
                    continue;
                var value = obj.Current[i];
                if (NetCodec.Same(value, ghost.Acked[i]))
                    continue;
                // Already on its way: give it time to arrive before sending it again.
                if (NetCodec.Same(value, ghost.LastSent[i]) && now - ghost.LastSentAt[i] < resend)
                    continue;
                changes.Add(i);
            }

            if (changes.Count == 0)
                return;

            _entry.Clear();
            _entry.VarUInt(obj.Id);
            _entry.Byte(ghost.Incarnation);
            _entry.VarUInt((uint)changes.Count);
            var carried = new List<(Ghost, int, int, object?)>();
            foreach (var i in changes)
            {
                var slot = obj.Slots[i];
                var value = obj.Current[i];
                var version = ++ghost.SentVersion[i];
                _entry.VarUInt((uint)i);
                if (slot.IsLines)
                {
                    var bases = new List<IReadOnlyList<string>?> { ghost.Acked[i] as IReadOnlyList<string> };
                    if (ghost.InFlight[i] is { } flying)
                        bases.AddRange(flying.Select(f => f.Value as IReadOnlyList<string>));
                    NetCodec.WriteLines(_entry, value as IReadOnlyList<string>, bases);
                    (ghost.InFlight[i] ??= []).Add((version, value));
                }
                else
                {
                    NetCodec.Write(_entry, slot.Type, value);
                }

                ghost.LastSent[i] = value;
                ghost.LastSentAt[i] = now;
                carried.Add((ghost, i, version, value));
            }

            var header = 1 + 5 + 8;
            if (_entry.Length + header > NetConnection.MaxUnreliable)
            {
                // Too big for one packet (a whole new world's worth of rows): send it reliably on its own.
                var big = new NetWriter(_entry.Length + header);
                WriteHeader(big);
                big.Raw(_entry.Span);
                var list = carried;
                connection.SendReliable(big.Span, () => Acked(list));
                return;
            }

            if (_packet.Length > 0 && _packet.Length + _entry.Length > NetConnection.MaxUnreliable)
                Flush();
            if (_packet.Length == 0)
                WriteHeader(_packet);
            _packet.Raw(_entry.Span);
            _carried.AddRange(carried);
        }

        public void Flush()
        {
            if (_packet.Length == 0)
                return;
            var list = _carried;
            connection.SendUnreliable(_packet.Span, () => Acked(list));
            _packet.Clear();
            _carried = [];
        }

        private void WriteHeader(NetWriter w)
        {
            w.Byte((byte)NetMessage.State);
            w.VarInt(session._epoch);
            w.Double(session._clock);
        }

        private void Acked(List<(Ghost Ghost, int Slot, int Version, object? Value)> list)
        {
            foreach (var (ghost, slot, version, value) in list)
            {
                if (player.Ghosts.GetValueOrDefault(ghost.Object) != ghost || slot >= ghost.Acked.Length || version <= ghost.AckedVersion[slot])
                    continue;
                ghost.Acked[slot] = value;
                ghost.AckedVersion[slot] = version;
                ghost.InFlight[slot]?.RemoveAll(f => f.Version <= version);
            }
        }
    }

    // ----- What players send -----

    private void OnHostReceived(NetConnection connection, byte[] data)
    {
        if (connection.Tag is not NetPlayer player)
            return;
        try
        {
            var r = new NetReader(data);
            switch ((NetMessage)r.Byte())
            {
                case NetMessage.Input:
                    player.Input?.Receive(ref r);
                    break;
                case NetMessage.Ready:
                    if (r.VarInt() == _epoch)
                    {
                        player.ReadyEpoch = _epoch;
                        player.Ghosts.Clear();
                    }

                    break;
                case NetMessage.OwnerState:
                    ReceiveOwnerState(player, ref r);
                    break;
                case NetMessage.ClientMessage:
                    ReceiveClientMessage(player, ref r);
                    break;
                case NetMessage.Command:
                {
                    var name = r.String() ?? "";
                    var args = ReadData(ref r);
                    _game.RunNetCommand(player, name, args);
                    break;
                }
            }
        }
        catch (NetFormatException)
        {
            // A garbled message from that player: ignore it.
        }
    }

    // A player's own character, as their machine moved it.
    private void ReceiveOwnerState(NetPlayer player, ref NetReader r)
    {
        if (r.VarInt() != _epoch)
            return;
        var entries = r.VarUInt();
        for (var e = 0; e < entries; e++)
        {
            var id = r.VarUInt();
            var incarnation = r.Byte();
            var corrections = r.VarUInt();
            var count = r.VarUInt();
            var obj = _objectsById.GetValueOrDefault(id);
            var ghost = obj is not null ? player.Ghosts.GetValueOrDefault(obj) : null;
            var valid = obj is { OwnerMoves: true, FromOwner: not null } && obj.Owner == player && ghost?.Incarnation == incarnation;
            // Sent before the player heard the host overrule it (say, a respawn): out of date.
            var stale = valid && corrections < obj!.Corrections;
            for (var i = 0; i < count; i++)
            {
                var index = (int)r.VarUInt();
                if (!valid || index >= obj!.Slots.Count)
                    throw new NetFormatException("Out of step.");
                var slot = obj.Slots[index];
                var value = NetCodec.Read(ref r, slot.Type, slot.Get(obj.Entity));
                if (stale || !slot.OwnerOwned)
                    continue;
                slot.Set(obj.Entity, value);
                obj.FromOwner![index] = slot.Get(obj.Entity);
            }
        }
    }

    private void ReceiveClientMessage(NetPlayer player, ref NetReader r)
    {
        var target = r.Byte();
        var to = r.VarInt();
        var name = r.String() ?? "";
        var data = ReadData(ref r);
        if (string.IsNullOrWhiteSpace(name))
            return;
        switch (target)
        {
            case 0:
                Relay(name, data, player, except: player);
                Deliver(name, data, player);
                break;
            case 1:
                Deliver(name, data, player);
                break;
            default:
                if (PlayerById(to) is { } recipient)
                {
                    if (recipient.IsLocal)
                        Deliver(name, data, player);
                    else if (recipient.Connection is { } c)
                        c.SendReliable(MessageBytes(name, data, player.Id));
                }

                break;
        }
    }

    /// <summary>Called by <c>Game.Send</c> on the host: everyone else hears it too.</summary>
    internal void Relay(string message, object? data, NetPlayer? from = null, NetPlayer? except = null)
    {
        if (!IsHost || !NetCodec.CanSend(data))
            return;
        byte[]? bytes = null;
        foreach (var player in _players)
        {
            if (player == except || player.Connection is not { } connection || player.ReadyEpoch != _epoch)
                continue;
            bytes ??= MessageBytes(message, data, from?.Id ?? LocalPlayer?.Id ?? 0);
            connection.SendReliable(bytes);
        }
    }

    private byte[] MessageBytes(string message, object? data, int from)
    {
        var w = new NetWriter();
        w.Byte((byte)NetMessage.Message);
        w.VarInt(from);
        w.String(message);
        WriteData(w, data);
        return w.ToArray();
    }

    /// <summary>A sound the host played, for everyone to hear (from where it was, if it was somewhere).</summary>
    internal void RelaySound(string sound, float volume, float pitch, Vector2? at)
    {
        if (!IsHost)
            return;
        byte[]? bytes = null;
        foreach (var player in _players)
        {
            if (player.Connection is not { } connection || player.ReadyEpoch != _epoch)
                continue;
            if (bytes is null)
            {
                var w = new NetWriter(48);
                w.Byte((byte)NetMessage.Sound);
                w.String(sound);
                w.Float(volume);
                w.Float(pitch);
                w.Bool(at.HasValue);
                if (at is { } point)
                    w.Vector2(point);
                bytes = w.ToArray();
            }

            connection.SendUnreliable(bytes);
        }
    }

    private void SendMessage(byte target, int to, string message, object? data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!NetCodec.CanSend(data))
            throw new ArgumentException($"A {data!.GetType().Name} can't be sent to other players. Send a number, text, a vector, a color, an entity or a player.", nameof(data));
        if (!IsOnline)
        {
            _game.Send(message, data);
            return;
        }

        if (IsHost)
        {
            switch (target)
            {
                case 0:
                    // Game.Send passes it on to everyone.
                    Deliver(message, data, LocalPlayer, relay: true);
                    break;
                case 1:
                    Deliver(message, data, LocalPlayer);
                    break;
                default:
                    if (PlayerById(to) is { } recipient)
                    {
                        if (recipient.IsLocal)
                            Deliver(message, data, LocalPlayer);
                        else
                            recipient.Connection?.SendReliable(MessageBytes(message, data, LocalPlayer?.Id ?? 0));
                    }

                    break;
            }

            return;
        }

        var w = new NetWriter();
        w.Byte((byte)NetMessage.ClientMessage);
        w.Byte(target);
        w.VarInt(to);
        w.String(message);
        WriteData(w, data);
        _hostConnection?.SendReliable(w.Span);
        if (target == 0)
            Deliver(message, data, LocalPlayer);
        else if (target == 2 && to == LocalPlayer?.Id)
            Deliver(message, data, LocalPlayer);
    }

    // Delivers a message here as Game.Send would, saying who sent it.
    private void Deliver(string message, object? data, NetPlayer? from, bool relay = false)
    {
        var before = Sender;
        Sender = from;
        try
        {
            _game.SendFromNetwork(message, data, relay);
        }
        finally
        {
            Sender = before;
        }
    }

    /// <summary>Sends a command for the host to carry out for this player (a strategy order, crafting). Offline it runs here.</summary>
    internal void SendCommand(string name, object? args)
    {
        if (IsClient)
        {
            var w = new NetWriter();
            w.Byte((byte)NetMessage.Command);
            w.String(name);
            WriteData(w, args);
            _hostConnection?.SendReliable(w.Span);
            return;
        }

        _game.RunNetCommand(LocalPlayer, name, args);
    }

    private void WriteData(NetWriter w, object? data) =>
        NetCodec.WriteData(w, data, e => IsHost ? _known.GetValueOrDefault(e)?.Id ?? 0 : _mirrorOf.GetValueOrDefault(e)?.Id ?? 0);

    private object? ReadData(ref NetReader r) =>
        NetCodec.ReadData(ref r, id => IsHost ? _objectsById.GetValueOrDefault(id)?.Entity : _mirrors.GetValueOrDefault(id)?.Entity, PlayerById);
}
