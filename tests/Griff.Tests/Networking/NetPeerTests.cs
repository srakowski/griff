using System.Text;
using Griff.Networking;

namespace Griff.Tests.Networking;

public sealed class NetPeerTests
{
    private const double Frame = 1 / 60.0;

    private sealed class Pair
    {
        public MemoryNetwork Network { get; } = new();
        public NetPeer Host { get; }
        public NetPeer Client { get; }
        public List<string> HostGot { get; } = [];
        public List<string> ClientGot { get; } = [];
        public List<NetConnection> HostConnections { get; } = [];
        public double Now { get; set; }

        public Pair(string hostKey = "game", string clientKey = "game", Func<NetConnectRequest, NetConnectReply>? accept = null)
        {
            Host = new NetPeer(Network.Open(7777), hostKey);
            Client = new NetPeer(Network.Open(), clientKey);
            Host.Listen(accept ?? (_ => NetConnectReply.Accept(Encoding.UTF8.GetBytes("welcome"))));
            Host.Connected += c => HostConnections.Add(c);
            Host.Received += (_, m) => HostGot.Add(Encoding.UTF8.GetString(m));
            Client.Received += (_, m) => ClientGot.Add(Encoding.UTF8.GetString(m));
        }

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += Frame)
            {
                Now += Frame;
                Client.Update(Now);
                Host.Update(Now);
            }
        }
    }

    private static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void A_client_connects_and_both_sides_exchange_reliable_messages()
    {
        var pair = new Pair();
        var connection = pair.Client.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 7777), Text("hi"));
        pair.Run(0.2);

        Assert.Equal(NetConnectionState.Connected, connection.State);
        Assert.Equal("welcome", Encoding.UTF8.GetString(connection.AcceptPayload));
        Assert.Single(pair.HostConnections);
        Assert.Equal("hi", Encoding.UTF8.GetString(pair.HostConnections[0].ConnectPayload));

        connection.SendReliable(Text("ping"));
        pair.HostConnections[0].SendReliable(Text("pong"));
        pair.Run(0.2);
        Assert.Equal(["ping"], pair.HostGot);
        Assert.Equal(["pong"], pair.ClientGot);
    }

    [Fact]
    public void Reliable_messages_arrive_once_and_in_order_through_heavy_loss()
    {
        var pair = new Pair();
        var connection = pair.Client.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 7777));
        pair.Run(0.2);
        var random = new Random(7);
        pair.Network.Filter = (_, _, _) => random.NextDouble() > 0.35;

        for (var i = 0; i < 300; i++)
        {
            connection.SendReliable(Text($"m{i}"));
            if (i % 10 == 0)
                pair.Run(Frame);
        }

        pair.Run(8);
        Assert.Equal(Enumerable.Range(0, 300).Select(i => $"m{i}"), pair.HostGot);
        Assert.Equal(0, connection.PendingReliable);
    }

    [Fact]
    public void A_big_reliable_message_is_cut_up_and_put_back_together()
    {
        var pair = new Pair();
        var connection = pair.Client.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 7777));
        pair.Run(0.2);
        var random = new Random(3);
        pair.Network.Filter = (_, _, _) => random.NextDouble() > 0.2;
        var big = string.Concat(Enumerable.Range(0, 30000).Select(i => (char)('a' + i % 26)));

        pair.HostConnections[0].SendReliable(Text(big));
        pair.HostConnections[0].SendReliable(Text("after"));
        pair.Run(6);
        Assert.Equal([big, "after"], pair.ClientGot);
    }

    [Fact]
    public void Unreliable_messages_report_when_they_arrive()
    {
        var pair = new Pair();
        var connection = pair.Client.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 7777));
        pair.Run(0.2);
        var acked = 0;
        connection.SendUnreliable(Text("state"), () => acked++);
        pair.Run(0.3);
        Assert.Equal(["state"], pair.HostGot);
        Assert.Equal(1, acked);

        // Lost ones never do.
        pair.Network.Filter = (_, _, _) => false;
        connection.SendUnreliable(Text("lost"), () => acked++);
        pair.Run(0.3);
        Assert.Equal(1, acked);
    }

    [Fact]
    public void A_different_game_is_refused()
    {
        var pair = new Pair(clientKey: "other game");
        string? reason = null;
        pair.Client.Disconnected += (_, why) => reason = why;
        var connection = pair.Client.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 7777));
        pair.Run(0.3);
        Assert.Equal(NetConnectionState.Disconnected, connection.State);
        Assert.Contains("different", reason);
        Assert.Empty(pair.HostConnections);
    }

    [Fact]
    public void The_host_can_refuse_a_player()
    {
        var pair = new Pair(accept: _ => NetConnectReply.Refuse("No room at the inn."));
        var connection = pair.Client.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 7777));
        pair.Run(0.3);
        Assert.Equal(NetConnectionState.Disconnected, connection.State);
        Assert.Equal("No room at the inn.", connection.DisconnectReason);
    }

    [Fact]
    public void A_silent_connection_times_out_and_leaving_tells_the_other_side()
    {
        var pair = new Pair();
        pair.Host.Timeout = 2;
        var hostLost = new List<string>();
        pair.Host.Disconnected += (_, why) => hostLost.Add(why);
        var connection = pair.Client.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 7777));
        pair.Run(0.2);

        connection.Disconnect("Bye.");
        pair.Run(0.2);
        Assert.Equal(["Bye."], hostLost);

        var again = pair.Client.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 7777));
        pair.Run(0.2);
        Assert.Equal(NetConnectionState.Connected, again.State);
        pair.Network.Filter = (_, _, _) => false;
        pair.Run(3);
        Assert.Equal(2, hostLost.Count);
        Assert.Contains("lost", hostLost[1]);
    }

    [Fact]
    public void Pretend_lag_delays_packets()
    {
        var pair = new Pair();
        var connection = pair.Client.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 7777));
        pair.Run(0.2);
        pair.Client.Conditions = new NetConditions { Latency = 0.25 };
        connection.SendReliable(Text("slow"));
        pair.Run(0.15);
        Assert.Empty(pair.HostGot);
        pair.Run(0.2);
        Assert.Equal(["slow"], pair.HostGot);
    }

    [Fact]
    public void Hosts_on_the_local_network_answer_discovery()
    {
        var pair = new Pair();
        pair.Host.DiscoveryInfo = () => Text("Arena: 2/8");
        var found = new List<string>();
        pair.Client.Discovered += (_, info) => found.Add(Encoding.UTF8.GetString(info));
        pair.Client.Discover(7777);
        pair.Run(0.1);
        Assert.Equal(["Arena: 2/8"], found);
    }

    [Fact]
    public void Writer_and_reader_round_trip()
    {
        var writer = new NetWriter(4);
        writer.VarUInt(300);
        writer.VarInt(-5);
        writer.String("héllo");
        writer.String(null);
        writer.Float(1.5f);
        writer.Guid(Guid.Parse("12345678-1234-1234-1234-123456789abc"));
        var reader = new NetReader(writer.Span);
        Assert.Equal(300u, reader.VarUInt());
        Assert.Equal(-5, reader.VarInt());
        Assert.Equal("héllo", reader.String());
        Assert.Null(reader.String());
        Assert.Equal(1.5f, reader.Float());
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789abc"), reader.Guid());
        Assert.True(reader.AtEnd);
        Assert.Throws<NetFormatException>(() => new NetReader([0x80]).VarUInt());
    }
}
