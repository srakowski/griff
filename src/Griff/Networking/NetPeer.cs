using System.Net;

namespace Griff.Networking;

public enum NetConnectionState
{
    /// <summary>Asking the host to let it in.</summary>
    Connecting,

    Connected,

    /// <summary>Left, refused, timed out or kicked. A disconnected connection is never used again.</summary>
    Disconnected,
}

/// <summary>Pretend network trouble, to try a game on a bad connection without one: applied to everything a peer sends.</summary>
public sealed record NetConditions
{
    /// <summary>Extra delay on every packet, in seconds, e.g. 0.05 for 100 ms there and back.</summary>
    public double Latency { get; init; }

    /// <summary>Up to this much more delay at random, in seconds, which can put packets out of order.</summary>
    public double Jitter { get; init; }

    /// <summary>The share of packets lost, from 0 to 1.</summary>
    public double Loss { get; init; }

    public bool IsPerfect => Latency <= 0 && Jitter <= 0 && Loss <= 0;
}

/// <summary>What a joining player sent with their request, for the host to accept or refuse.</summary>
public sealed record NetConnectRequest(EndPoint From, byte[] Payload);

/// <summary>The host's answer to a <see cref="NetConnectRequest"/>: in (with data for the new player), or out (with why).</summary>
public sealed record NetConnectReply(bool Accepted, byte[] Payload, string? Reason = null)
{
    public static NetConnectReply Accept(byte[]? payload = null) => new(true, payload ?? []);

    public static NetConnectReply Refuse(string reason) => new(false, [], reason);
}

/// <summary>
/// One end of the link to another peer. Messages are sent either reliably (they arrive once, in the order sent, however
/// big; lost packets are sent again) or unreliably (the latest news, which may be lost: the caller hears when one arrived
/// so it can stop sending what the other side already has).
/// </summary>
public sealed class NetConnection
{
    // Packets are kept under a typical internet MTU; bigger reliable messages are cut into fragments.
    internal const int MaxDatagram = 1200;
    internal const int MaxFragment = 1000;
    private const int HeaderSize = 2 + 2 + 2 + 4;
    private const int SentHistory = 1024;

    private readonly NetPeer _peer;
    private readonly List<ReliableOut> _reliableOut = [];
    private readonly List<(byte[] Data, Action? Acked)> _unreliableOut = [];
    private readonly SentPacket?[] _sent = new SentPacket?[SentHistory];
    private readonly Dictionary<ushort, (byte Kind, byte[] Data)> _reliableAhead = [];
    private readonly Queue<(ushort LastId, Action Callback)> _deliveredCallbacks = new();
    private ushort _nextSeq;
    private ushort _nextReliableId;
    private ushort _remoteSeq;
    private uint _receivedBits;
    private bool _anyReceived;
    private bool _ackPending;
    private int _sinceAck;
    private ushort _expectedReliable;
    private byte[]? _assembling;
    private int _assembled;
    private double _lastSent = double.NegativeInfinity;
    private double _lastConnectSent = double.NegativeInfinity;

    internal NetConnection(NetPeer peer, int id, EndPoint remote, NetConnectionState state, double now)
    {
        _peer = peer;
        Id = id;
        RemoteEndPoint = remote;
        State = state;
        LastReceived = now;
        Started = now;
    }

    /// <summary>A number for this connection, unique on its peer.</summary>
    public int Id { get; }

    public EndPoint RemoteEndPoint { get; }

    public NetConnectionState State { get; internal set; }

    /// <summary>Why it disconnected, once it has.</summary>
    public string? DisconnectReason { get; internal set; }

    /// <summary>Round trip time, smoothed, in seconds.</summary>
    public double RoundTrip { get; private set; } = 0.1;

    /// <summary><see cref="RoundTrip"/> in whole milliseconds, for a scoreboard.</summary>
    public int Ping => (int)Math.Round(RoundTrip * 1000);

    /// <summary>What the host sent back when it accepted this connection (on the joining side).</summary>
    public byte[] AcceptPayload { get; internal set; } = [];

    /// <summary>What the joining side sent with its request (on the host's side).</summary>
    public byte[] ConnectPayload { get; internal set; } = [];

    /// <summary>Anything the game wants to keep with the connection, e.g. its player.</summary>
    public object? Tag { get; set; }

    public long BytesSent { get; private set; }

    public long BytesReceived { get; private set; }

    /// <summary>Reliable messages (and fragments) not yet confirmed by the other side.</summary>
    public int PendingReliable => _reliableOut.Count;

    internal double LastReceived { get; private set; }

    internal double Started { get; }

    /// <summary>
    /// Sends <paramref name="message"/> so it arrives exactly once, after everything sent reliably before it.
    /// <paramref name="delivered"/> is called once the other side has it and everything sent before it.
    /// </summary>
    public void SendReliable(ReadOnlySpan<byte> message, Action? delivered = null)
    {
        if (State == NetConnectionState.Disconnected)
            return;
        Fragment(message);
        if (delivered is not null)
            _deliveredCallbacks.Enqueue(((ushort)(_nextReliableId - 1), delivered));
    }

    private void Fragment(ReadOnlySpan<byte> message)
    {
        if (message.Length <= MaxFragment)
        {
            QueueReliable(0, message.ToArray());
            return;
        }

        // First fragment carries the total length; the rest follow in order.
        var first = new NetWriter(MaxFragment + 8);
        first.VarUInt((uint)message.Length);
        var take = MaxFragment - first.Length;
        first.Raw(message[..take]);
        QueueReliable(1, first.ToArray());
        for (var at = take; at < message.Length; at += MaxFragment)
            QueueReliable(2, message.Slice(at, Math.Min(MaxFragment, message.Length - at)).ToArray());
    }

    /// <summary>
    /// Sends <paramref name="message"/> in the next packet. It may be lost; <paramref name="acked"/> is called if the other
    /// side confirms it arrived. At most <see cref="MaxUnreliable"/> bytes.
    /// </summary>
    public void SendUnreliable(ReadOnlySpan<byte> message, Action? acked = null)
    {
        if (State == NetConnectionState.Disconnected)
            return;
        if (message.Length > MaxUnreliable)
            throw new ArgumentException($"An unreliable message can be at most {MaxUnreliable} bytes; this one is {message.Length}.", nameof(message));
        _unreliableOut.Add((message.ToArray(), acked));
    }

    /// <summary>The most an unreliable message can hold.</summary>
    public const int MaxUnreliable = MaxDatagram - HeaderSize - 8;

    /// <summary>Closes the connection, telling the other side why.</summary>
    public void Disconnect(string reason = "Left the game.") => _peer.Close(this, reason, tell: true);

    private void QueueReliable(byte kind, byte[] data) => _reliableOut.Add(new ReliableOut(_nextReliableId++, kind, data));

    internal void Heard(double now) => LastReceived = now;

    internal void Receive(ReadOnlySpan<byte> packet, double now)
    {
        LastReceived = now;
        BytesReceived += packet.Length + 28;
        var reader = new NetReader(packet);
        var seq = reader.UShort();
        var ack = reader.UShort();
        var ackBits = reader.UInt();
        ProcessAcks(ack, ackBits, now);

        if (!TrackReceived(seq))
            return;
        _ackPending = true;

        // A burst of packets is acknowledged as it arrives: one packet's acks only reach 33 back.
        if (++_sinceAck >= 16)
            SendAcks(now);

        while (!reader.AtEnd)
        {
            var flags = reader.Byte();
            if ((flags & 1) != 0)
            {
                var id = reader.UShort();
                var data = reader.Bytes();
                ReceiveReliable(id, (byte)(flags >> 1), data);
            }
            else
            {
                _peer.Deliver(this, reader.Bytes().ToArray());
            }

            if (State == NetConnectionState.Disconnected)
                return;
        }
    }

    // False for a packet already seen (or too old to tell), whose messages must not be delivered twice.
    private bool TrackReceived(ushort seq)
    {
        if (!_anyReceived)
        {
            _anyReceived = true;
            _remoteSeq = seq;
            _receivedBits = 0;
            return true;
        }

        if (Newer(seq, _remoteSeq))
        {
            var shift = (ushort)(seq - _remoteSeq);
            _receivedBits = shift >= 32 ? (shift == 32 ? 1u << 31 : 0) : (_receivedBits << shift) | (1u << (shift - 1));
            _remoteSeq = seq;
            return true;
        }

        var back = (ushort)(_remoteSeq - seq);
        if (back == 0 || back > 32)
            return false;
        var bit = 1u << (back - 1);
        if ((_receivedBits & bit) != 0)
            return false;
        _receivedBits |= bit;
        return true;
    }

    private void ProcessAcks(ushort ack, uint bits, double now)
    {
        Acked(ack, now);
        for (var i = 0; i < 32; i++)
        {
            if ((bits & (1u << i)) != 0)
                Acked((ushort)(ack - 1 - i), now);
        }

        // Everything up to the oldest message still unconfirmed has arrived.
        var lowest = _reliableOut.Count > 0 ? _reliableOut[0].Id : _nextReliableId;
        while (_deliveredCallbacks.Count > 0 && Newer(lowest, _deliveredCallbacks.Peek().LastId))
            _deliveredCallbacks.Dequeue().Callback();
    }

    private void Acked(ushort seq, double now)
    {
        var slot = seq % SentHistory;
        if (_sent[slot] is not { } packet || packet.Seq != seq)
            return;
        _sent[slot] = null;

        var sample = Math.Max(0, now - packet.Time);
        RoundTrip += (sample - RoundTrip) * 0.1;
        foreach (var reliable in packet.Reliable)
            reliable.Acked = true;
        if (packet.Reliable.Count > 0)
            _reliableOut.RemoveAll(r => r.Acked);
        foreach (var callback in packet.Callbacks)
            callback();
    }

    private void ReceiveReliable(ushort id, byte kind, ReadOnlySpan<byte> data)
    {
        if (id != _expectedReliable)
        {
            // Ahead: hold it until the gap is filled. Behind: already delivered.
            if (Newer(id, _expectedReliable))
                _reliableAhead.TryAdd(id, (kind, data.ToArray()));
            return;
        }

        DeliverReliable(kind, data);
        _expectedReliable++;
        while (State != NetConnectionState.Disconnected && _reliableAhead.Remove(_expectedReliable, out var next))
        {
            DeliverReliable(next.Kind, next.Data);
            _expectedReliable++;
        }
    }

    private void DeliverReliable(byte kind, ReadOnlySpan<byte> data)
    {
        switch (kind)
        {
            case 0:
                _peer.Deliver(this, data.ToArray());
                break;
            case 1:
            {
                var reader = new NetReader(data);
                var total = checked((int)reader.VarUInt());
                if (total > 64 << 20)
                    throw new NetFormatException("A message is too big.");
                _assembling = new byte[total];
                var part = reader.Rest();
                part.CopyTo(_assembling);
                _assembled = part.Length;
                break;
            }
            default:
                if (_assembling is null)
                    return;
                data[..Math.Min(data.Length, _assembling.Length - _assembled)].CopyTo(_assembling.AsSpan(_assembled));
                _assembled += data.Length;
                if (_assembled >= _assembling.Length)
                {
                    var whole = _assembling;
                    _assembling = null;
                    _peer.Deliver(this, whole);
                }

                break;
        }
    }

    internal void Flush(double now, INetSocketSender socket)
    {
        if (State == NetConnectionState.Connecting)
        {
            if (now - _lastConnectSent >= 0.25)
            {
                _lastConnectSent = now;
                _peer.SendConnect(this);
            }

            return;
        }

        if (State != NetConnectionState.Connected)
            return;

        var resend = Math.Clamp(RoundTrip * 1.5 + 0.03, 0.06, 1.0);
        var due = _reliableOut.Where(r => now - r.LastSent >= resend).ToList();
        var packets = 0;
        var reliableAt = 0;
        var unreliableAt = 0;
        var writer = new NetWriter(MaxDatagram);
        while (packets < NetPeer.MaxPacketsPerFlush)
        {
            var anyLeft = reliableAt < due.Count || unreliableAt < _unreliableOut.Count;
            var keepAlive = now - _lastSent >= 0.1;
            if (!anyLeft && !_ackPending && !keepAlive)
                break;

            writer.Clear();
            var seq = _nextSeq++;
            writer.Byte(NetPeer.Magic);
            writer.Byte((byte)PacketType.Data);
            writer.UShort(seq);
            writer.UShort(_remoteSeq);
            writer.UInt(_receivedBits);
            var record = new SentPacket(seq, now);

            while (reliableAt < due.Count && writer.Length + due[reliableAt].Data.Length + 8 <= MaxDatagram)
            {
                var reliable = due[reliableAt++];
                writer.Byte((byte)(1 | (reliable.Kind << 1)));
                writer.UShort(reliable.Id);
                writer.Bytes(reliable.Data);
                reliable.LastSent = now;
                record.Reliable.Add(reliable);
            }

            while (unreliableAt < _unreliableOut.Count && writer.Length + _unreliableOut[unreliableAt].Data.Length + 6 <= MaxDatagram)
            {
                var (data, acked) = _unreliableOut[unreliableAt++];
                writer.Byte(0);
                writer.Bytes(data);
                if (acked is not null)
                    record.Callbacks.Add(acked);
            }

            _sent[seq % SentHistory] = record;
            socket.Send(this, writer.Span);
            BytesSent += writer.Length + 28;
            _lastSent = now;
            _ackPending = false;
            _sinceAck = 0;
            packets++;
        }

        // What didn't fit this time waits for the next flush; stale unreliable news is dropped (newer will follow).
        _unreliableOut.Clear();
    }

    private void SendAcks(double now)
    {
        var writer = new NetWriter(16);
        var seq = _nextSeq++;
        writer.Byte(NetPeer.Magic);
        writer.Byte((byte)PacketType.Data);
        writer.UShort(seq);
        writer.UShort(_remoteSeq);
        writer.UInt(_receivedBits);
        _sent[seq % SentHistory] = new SentPacket(seq, now);
        ((INetSocketSender)_peer).Send(this, writer.Span);
        BytesSent += writer.Length + 28;
        _sinceAck = 0;
    }

    private static bool Newer(ushort a, ushort b) => (short)(a - b) > 0;

    private sealed class ReliableOut(ushort id, byte kind, byte[] data)
    {
        public ushort Id { get; } = id;

        public byte Kind { get; } = kind;

        public byte[] Data { get; } = data;

        public double LastSent { get; set; } = double.NegativeInfinity;

        public bool Acked { get; set; }
    }

    private sealed class SentPacket(ushort seq, double time)
    {
        public ushort Seq { get; } = seq;

        public double Time { get; } = time;

        public List<ReliableOut> Reliable { get; } = [];

        public List<Action> Callbacks { get; } = [];
    }
}

internal enum PacketType : byte
{
    Connect = 1,
    Accept = 2,
    Refuse = 3,
    Data = 4,
    Disconnect = 5,
    Discover = 6,
    DiscoverReply = 7,
}

internal interface INetSocketSender
{
    void Send(NetConnection connection, ReadOnlySpan<byte> datagram);
}

/// <summary>
/// One machine's side of a networked game: the host listening for players, or a player joining a host. Call
/// <see cref="Update"/> every frame; messages, joins and leaves are reported from inside it.
/// </summary>
public sealed class NetPeer : IDisposable, INetSocketSender
{
    internal const byte Magic = 0x47;
    internal const int MaxPacketsPerFlush = 64;
    private const ushort ProtocolVersion = 1;

    private readonly INetSocket _socket;
    private readonly string _appKey;
    private readonly Dictionary<EndPoint, NetConnection> _connections = [];
    private readonly List<(double At, EndPoint To, byte[] Data)> _delayed = [];
    private readonly Random _random = new(1234);
    private Func<NetConnectRequest, NetConnectReply>? _accept;
    private int _nextId = 1;
    private double _now;

    /// <param name="appKey">Which game (and version of it) this is: only peers with the same key can connect.</param>
    public NetPeer(INetSocket socket, string appKey)
    {
        _socket = socket;
        _appKey = appKey;
    }

    public INetSocket Socket => _socket;

    /// <summary>True once <see cref="Listen"/> has been called: this peer is a host.</summary>
    public bool IsListening => _accept is not null;

    /// <summary>The most connections a host accepts.</summary>
    public int MaxConnections { get; set; } = 64;

    /// <summary>Seconds without hearing anything before a connection counts as lost.</summary>
    public double Timeout { get; set; } = 10;

    /// <summary>Pretend network trouble on everything this peer sends.</summary>
    public NetConditions Conditions { get; set; } = new();

    /// <summary>What a host tells peers looking for games on the local network (see <see cref="Discover"/>).</summary>
    public Func<byte[]>? DiscoveryInfo { get; set; }

    public IReadOnlyCollection<NetConnection> Connections => _connections.Values;

    public event Action<NetConnection>? Connected;

    public event Action<NetConnection, string>? Disconnected;

    public event Action<NetConnection, byte[]>? Received;

    /// <summary>A host on the local network answered <see cref="Discover"/>, with its <see cref="DiscoveryInfo"/>.</summary>
    public event Action<EndPoint, byte[]>? Discovered;

    /// <summary>Starts accepting players. <paramref name="accept"/> decides about each one.</summary>
    public void Listen(Func<NetConnectRequest, NetConnectReply> accept) => _accept = accept;

    /// <summary>Asks the host at <paramref name="host"/> to let this peer in, sending <paramref name="payload"/> along.</summary>
    public NetConnection Connect(EndPoint host, byte[]? payload = null)
    {
        var connection = new NetConnection(this, _nextId++, host, NetConnectionState.Connecting, _now) { ConnectPayload = payload ?? [] };
        _connections[host] = connection;
        return connection;
    }

    /// <summary>Asks every host on the local network listening on <paramref name="port"/> to say what it's playing.</summary>
    public void Discover(int port)
    {
        var writer = new NetWriter();
        writer.Byte(Magic);
        writer.Byte((byte)PacketType.Discover);
        writer.String(_appKey);
        _socket.Broadcast(port, writer.Span);
    }

    /// <summary>Receives what has arrived, drops connections gone quiet, and sends what's waiting. <paramref name="now"/> is in seconds.</summary>
    public void Update(double now)
    {
        Poll(now);
        Flush(now);
    }

    /// <summary>Receives what has arrived (reporting it) and drops connections gone quiet.</summary>
    public void Poll(double now)
    {
        _now = now;
        while (_socket.TryReceive(out var from, out var datagram))
        {
            try
            {
                Handle(from, datagram);
            }
            catch (NetFormatException)
            {
                // Garbled, or from something that isn't this game: ignore it.
            }
        }

        foreach (var connection in _connections.Values.ToList())
        {
            var limit = connection.State == NetConnectionState.Connecting ? Math.Min(Timeout, 6) : Timeout;
            if (now - connection.LastReceived > limit)
                Close(connection, connection.State == NetConnectionState.Connecting ? "The host didn't answer." : "The connection was lost.", tell: false);
        }
    }

    /// <summary>Sends everything waiting to go.</summary>
    public void Flush(double now)
    {
        _now = now;
        foreach (var connection in _connections.Values.ToList())
            connection.Flush(now, this);
        SendDelayed();
    }

    /// <summary>Tells everyone it's leaving and closes every connection.</summary>
    public void DisconnectAll(string reason)
    {
        foreach (var connection in _connections.Values.ToList())
            Close(connection, reason, tell: true);
        SendDelayed(all: true);
    }

    public void Dispose()
    {
        DisconnectAll("The game closed.");
        _socket.Dispose();
    }

    void INetSocketSender.Send(NetConnection connection, ReadOnlySpan<byte> datagram) => Send(connection.RemoteEndPoint, datagram);

    internal void Deliver(NetConnection connection, byte[] message) => Received?.Invoke(connection, message);

    internal void SendConnect(NetConnection connection)
    {
        var writer = new NetWriter();
        writer.Byte(Magic);
        writer.Byte((byte)PacketType.Connect);
        writer.UShort(ProtocolVersion);
        writer.String(_appKey);
        writer.Bytes(connection.ConnectPayload);
        Send(connection.RemoteEndPoint, writer.Span);
    }

    internal void Close(NetConnection connection, string reason, bool tell)
    {
        if (connection.State == NetConnectionState.Disconnected)
            return;
        if (tell)
        {
            var writer = new NetWriter();
            writer.Byte(Magic);
            writer.Byte((byte)PacketType.Disconnect);
            writer.String(reason);
            // A few times, since it isn't acknowledged.
            for (var i = 0; i < 3; i++)
                Send(connection.RemoteEndPoint, writer.Span);
        }

        var wasConnected = connection.State == NetConnectionState.Connected;
        connection.State = NetConnectionState.Disconnected;
        connection.DisconnectReason = reason;
        _connections.Remove(connection.RemoteEndPoint);
        if (wasConnected || !IsListening)
            Disconnected?.Invoke(connection, reason);
    }

    private void Handle(EndPoint from, byte[] datagram)
    {
        var reader = new NetReader(datagram);
        if (reader.Byte() != Magic)
            return;
        var type = (PacketType)reader.Byte();
        _connections.TryGetValue(from, out var connection);
        switch (type)
        {
            case PacketType.Data when connection is { State: NetConnectionState.Connected }:
                connection.Receive(datagram.AsSpan(2), _now);
                break;
            case PacketType.Connect:
                HandleConnect(from, ref reader, connection);
                break;
            case PacketType.Accept when connection is { State: NetConnectionState.Connecting }:
                connection.AcceptPayload = reader.Bytes().ToArray();
                connection.State = NetConnectionState.Connected;
                connection.Heard(_now);
                Connected?.Invoke(connection);
                break;
            case PacketType.Refuse when connection is { State: NetConnectionState.Connecting }:
                Close(connection, reader.String() ?? "The host said no.", tell: false);
                break;
            case PacketType.Disconnect when connection is not null:
                Close(connection, reader.String() ?? "Disconnected.", tell: false);
                break;
            case PacketType.Discover when IsListening && DiscoveryInfo is not null && reader.String() == _appKey:
            {
                var writer = new NetWriter();
                writer.Byte(Magic);
                writer.Byte((byte)PacketType.DiscoverReply);
                writer.Bytes(DiscoveryInfo());
                Send(from, writer.Span);
                break;
            }
            case PacketType.DiscoverReply:
                Discovered?.Invoke(from, reader.Bytes().ToArray());
                break;
        }
    }

    private void HandleConnect(EndPoint from, ref NetReader reader, NetConnection? existing)
    {
        if (_accept is null)
            return;
        var version = reader.UShort();
        var key = reader.String();
        var payload = reader.Bytes().ToArray();
        if (existing is { State: NetConnectionState.Connected })
        {
            // Our Accept was lost; say it again.
            SendAccept(from, existing.AcceptPayload);
            return;
        }

        string? refusal = null;
        if (version != ProtocolVersion || key != _appKey)
            refusal = "That's a different game, or a different version of it.";
        else if (_connections.Count >= MaxConnections)
            refusal = "The game is full.";

        NetConnectReply reply = refusal is null ? _accept(new NetConnectRequest(from, payload)) : NetConnectReply.Refuse(refusal);
        if (!reply.Accepted)
        {
            var writer = new NetWriter();
            writer.Byte(Magic);
            writer.Byte((byte)PacketType.Refuse);
            writer.String(reply.Reason ?? "The host said no.");
            Send(from, writer.Span);
            return;
        }

        var connection = new NetConnection(this, _nextId++, from, NetConnectionState.Connected, _now)
        {
            ConnectPayload = payload,
            AcceptPayload = reply.Payload,
        };
        _connections[from] = connection;
        SendAccept(from, reply.Payload);
        Connected?.Invoke(connection);
    }

    private void SendAccept(EndPoint to, byte[] payload)
    {
        var writer = new NetWriter();
        writer.Byte(Magic);
        writer.Byte((byte)PacketType.Accept);
        writer.Bytes(payload);
        Send(to, writer.Span);
    }

    private void Send(EndPoint to, ReadOnlySpan<byte> datagram)
    {
        var conditions = Conditions;
        if (conditions.IsPerfect)
        {
            _socket.Send(to, datagram);
            return;
        }

        if (conditions.Loss > 0 && _random.NextDouble() < conditions.Loss)
            return;
        var delay = Math.Max(0, conditions.Latency) + _random.NextDouble() * Math.Max(0, conditions.Jitter);
        _delayed.Add((_now + delay, to, datagram.ToArray()));
    }

    private void SendDelayed(bool all = false)
    {
        if (_delayed.Count == 0)
            return;
        var due = _delayed.Where(d => all || d.At <= _now).OrderBy(d => d.At).ToList();
        foreach (var item in due)
            _socket.Send(item.To, item.Data);
        _delayed.RemoveAll(d => all || d.At <= _now);
    }
}
