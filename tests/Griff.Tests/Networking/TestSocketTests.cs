using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Griff.Input;
using Griff.Networking;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Tests.Architecture;

namespace Griff.Tests.Networking;

/// <summary>
/// Tests keep their sockets on this machine. One bound to the network (a template's Host Game pressed by a test, say)
/// makes Windows ask whether testhost may use it, again for each new path it runs from, and the question takes the
/// screen. Nothing is set for the whole run: a game plays over the network only when its host says so
/// (<see cref="INetwork.OverNetwork"/>, the game's own), and only the player and the editor's Play do. These fail if a
/// game hosts or joins where the network can reach it, or if code binds a socket where the game's choice can't reach it.
/// </summary>
public sealed partial class TestSocketTests
{
    [Fact]
    public void A_game_hosts_and_joins_on_this_machine_unless_its_host_says_otherwise()
    {
        // Another game in the process that may play over the network (it doesn't host here) changes nothing for this one.
        var other = new Game(new Scene(), new InputState());
        other.Network.OverNetwork = true;

        int port;
        using (var free = new UdpNetSocket(loopbackOnly: true))
            port = ((IPEndPoint)free.LocalEndPoint).Port;
        var host = new Game(new Scene(), new InputState());
        Assert.False(host.Network.OverNetwork);
        try
        {
            Assert.True(host.Network.Host(port), host.Network.Status);
            Assert.Equal($"Hosting on 127.0.0.1:{port}", host.Network.Status);
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Where(e => e.Port == port).ToList();
            Assert.NotEmpty(listeners);
            Assert.All(listeners, e => Assert.True(IPAddress.IsLoopback(e.Address), $"Hosting bound {e}."));
        }
        finally
        {
            host.Network.Leave();
        }

        var guest = new Game(new Scene(), new InputState());
        guest.Network.Join("lan");
        guest.Network.Leave();
        Assert.True(other.Network.OverNetwork);
    }

    // Only the hosts that run a game for its players let it play over the network: the player (a dedicated server too)
    // and the editor's Play. Nothing else turns it on, tests least of all (this file's game above doesn't host).
    [Fact]
    public void Only_the_players_hosts_let_a_game_play_over_the_network()
    {
        var root = LayerTests.RepoRoot();
        var allowed = new[]
        {
            Path.Combine("src", "Griff.Player", "App.xaml.cs"), Path.Combine("src", "Griff.Editor", "Features", "Play", "PlayMode.cs"),
            Path.Combine("tests", "Griff.Tests", "Networking", "TestSocketTests.cs"),
        };
        var found = new List<string>();
        foreach (var folder in new[] { "src", "tests", "tools" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                if (allowed.Contains(relative) || relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                    continue;
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (TurnsOnNetwork().IsMatch(lines[i]))
                        found.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(found.Count == 0,
            "Only the player and the editor's Play let a game play over the network; a test, simulation or tool keeps it on " +
            "this machine (a socket the network can reach makes Windows ask about the firewall):\n" + string.Join("\n", found));
    }

    [GeneratedRegex(@"OverNetwork\s*=(?!=)")]
    private static partial Regex TurnsOnNetwork();

    // Everything that binds a socket goes through UdpNetSocket, which a game keeps on loopback; anything else must
    // say Loopback on the line that binds it (as the agent server's listener does).
    [Fact]
    public void Only_the_network_socket_binds_to_the_network()
    {
        var allowed = new[] { Path.Combine("Griff", "Networking", "NetSocket.cs"), Path.Combine("Griff", "Runtime", "Net", "NetSession.cs") };
        var source = Path.Combine(LayerTests.RepoRoot(), "src");
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (allowed.Contains(relative) || relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (Binds().IsMatch(lines[i]) && !lines[i].Contains("Loopback", StringComparison.Ordinal))
                    found.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(found.Count == 0,
            "A socket bound outside UdpNetSocket can reach the network from a test and make Windows ask about the firewall; " +
            "use UdpNetSocket, or bind to IPAddress.Loopback:\n" + string.Join("\n", found));
    }

    [GeneratedRegex(@"new\s+(Socket|UdpClient|TcpListener|TcpClient|HttpListener)\b|\.Bind\(\s*new\s+IPEndPoint|IPAddress\.(Any|IPv6Any)\b")]
    private static partial Regex Binds();
}
