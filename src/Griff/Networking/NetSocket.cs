using System.Net;
using System.Net.Sockets;

namespace Griff.Networking;

/// <summary>Sends and receives datagrams: a real UDP socket, or one on a <see cref="MemoryNetwork"/>.</summary>
public interface INetSocket : IDisposable
{
    /// <summary>The address others reach this socket at.</summary>
    EndPoint LocalEndPoint { get; }

    /// <summary>Sends one datagram. Nothing is promised: it may be lost, duplicated or arrive out of order.</summary>
    void Send(EndPoint to, ReadOnlySpan<byte> datagram);

    /// <summary>The next datagram that has arrived, if any. Never blocks.</summary>
    bool TryReceive(out EndPoint from, out byte[] datagram);

    /// <summary>Sends to every machine on the local network listening on <paramref name="port"/>, to find games there.</summary>
    void Broadcast(int port, ReadOnlySpan<byte> datagram);
}

/// <summary>A UDP socket for playing over a network (IPv4).</summary>
public sealed class UdpNetSocket : INetSocket
{
    // Windows reports a datagram that bounced off a closed port as an error on the next receive; turn that off.
    private const int SioUdpConnReset = -1744830452;

    private readonly Socket _socket;
    private readonly byte[] _receive = new byte[65536];

    /// <param name="port">The port to listen on; 0 picks any free one (for joining).</param>
    /// <param name="loopbackOnly">Only reachable from this machine: no firewall question for local play.</param>
    public UdpNetSocket(int port = 0, bool loopbackOnly = false)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false, EnableBroadcast = !loopbackOnly };
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
            }
            catch (SocketException)
            {
                // Not supported here; receive errors are ignored anyway.
            }
        }

        _socket.ReceiveBufferSize = 1 << 20;
        _socket.SendBufferSize = 1 << 20;
        _socket.Bind(new IPEndPoint(loopbackOnly ? IPAddress.Loopback : IPAddress.Any, port));
    }

    public EndPoint LocalEndPoint => _socket.LocalEndPoint!;

    public void Send(EndPoint to, ReadOnlySpan<byte> datagram)
    {
        try
        {
            _socket.SendTo(datagram, SocketFlags.None, to);
        }
        catch (SocketException)
        {
            // Unreachable or the buffer is full: the same as a lost packet.
        }
    }

    public bool TryReceive(out EndPoint from, out byte[] datagram)
    {
        while (true)
        {
            from = new IPEndPoint(IPAddress.Any, 0);
            datagram = [];
            try
            {
                if (_socket.Available <= 0)
                    return false;
                var count = _socket.ReceiveFrom(_receive, ref from);
                datagram = _receive.AsSpan(0, count).ToArray();
                return true;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize)
            {
                // A bounced or oversized datagram: skip it and look for the next.
            }
            catch (SocketException)
            {
                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }
    }

    public void Broadcast(int port, ReadOnlySpan<byte> datagram)
    {
        Send(new IPEndPoint(IPAddress.Broadcast, port), datagram);
        // Games on this same machine too, whatever the network says about broadcasts.
        Send(new IPEndPoint(IPAddress.Loopback, port), datagram);
    }

    public void Dispose() => _socket.Dispose();

    /// <summary>
    /// Reads "192.168.1.5", "localhost", "my-pc:7777" or "[::1]:7777" into an address, using <paramref name="defaultPort"/>
    /// when none is given. Null if it can't be found.
    /// </summary>
    public static IPEndPoint? Resolve(string? address, int defaultPort)
    {
        var text = (address ?? "").Trim();
        if (text.Length == 0)
            text = "127.0.0.1";
        if (IPEndPoint.TryParse(text, out var direct) && direct.AddressFamily == AddressFamily.InterNetwork)
            return direct.Port == 0 ? new IPEndPoint(direct.Address, defaultPort) : direct;

        var port = defaultPort;
        var colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out var parsed) && parsed is > 0 and < 65536)
        {
            port = parsed;
            text = text[..colon];
        }

        if (IPAddress.TryParse(text, out var ip))
            return new IPEndPoint(ip, port);
        try
        {
            var found = Dns.GetHostAddresses(text).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return found is null ? null : new IPEndPoint(found, port);
        }
        catch (SocketException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// A pretend network inside one process: sockets made from it talk to each other instantly and reliably, unless
/// <see cref="Filter"/> drops datagrams. For tests, and for extra players in the editor's own window.
/// </summary>
public sealed class MemoryNetwork
{
    private readonly Dictionary<EndPoint, MemorySocket> _sockets = [];
    private int _nextPort = 50000;

    /// <summary>Decides each datagram's fate: false drops it. For testing lost packets.</summary>
    public Func<EndPoint, EndPoint, byte[], bool>? Filter { get; set; }

    /// <summary>A socket at 127.0.0.1 on <paramref name="port"/>, or a free port if 0.</summary>
    public INetSocket Open(int port = 0)
    {
        lock (_sockets)
        {
            if (port == 0)
            {
                while (_sockets.ContainsKey(new IPEndPoint(IPAddress.Loopback, _nextPort)))
                    _nextPort++;
                port = _nextPort++;
            }

            var endPoint = new IPEndPoint(IPAddress.Loopback, port);
            if (_sockets.ContainsKey(endPoint))
                throw new InvalidOperationException($"Port {port} is already in use on this network.");
            var socket = new MemorySocket(this, endPoint);
            _sockets.Add(endPoint, socket);
            return socket;
        }
    }

    private void Deliver(EndPoint from, EndPoint to, ReadOnlySpan<byte> datagram)
    {
        var copy = datagram.ToArray();
        MemorySocket? target;
        lock (_sockets)
            _sockets.TryGetValue(Normalize(to), out target);
        if (target is null || Filter?.Invoke(from, to, copy) == false)
            return;
        lock (target.Inbox)
            target.Inbox.Enqueue((from, copy));
    }

    private static EndPoint Normalize(EndPoint to) =>
        to is IPEndPoint ip && !IPAddress.IsLoopback(ip.Address) ? new IPEndPoint(IPAddress.Loopback, ip.Port) : to;

    private sealed class MemorySocket(MemoryNetwork network, EndPoint endPoint) : INetSocket
    {
        public Queue<(EndPoint From, byte[] Data)> Inbox { get; } = new();

        public EndPoint LocalEndPoint { get; } = endPoint;

        public void Send(EndPoint to, ReadOnlySpan<byte> datagram) => network.Deliver(LocalEndPoint, to, datagram);

        public bool TryReceive(out EndPoint from, out byte[] datagram)
        {
            lock (Inbox)
            {
                if (Inbox.TryDequeue(out var next))
                {
                    (from, datagram) = next;
                    return true;
                }
            }

            from = LocalEndPoint;
            datagram = [];
            return false;
        }

        public void Broadcast(int port, ReadOnlySpan<byte> datagram) => Send(new IPEndPoint(IPAddress.Loopback, port), datagram);

        public void Dispose()
        {
            lock (network._sockets)
                network._sockets.Remove(LocalEndPoint);
        }
    }
}
