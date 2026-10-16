using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Griff.Input;
using Griff.Networking;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Tests.Architecture;

namespace Griff.Tests.Networking;

/// <summary>Turns <see cref="UdpNetSocket.ForceLoopback"/> on before any test runs.</summary>
internal static class TestSockets
{
    [ModuleInitializer]
    internal static void KeepOnThisMachine() => UdpNetSocket.ForceLoopback = true;
}

/// <summary>
/// Tests keep their sockets on this machine. One bound to the network (a template's Host Game pressed by a test, say)
/// makes Windows ask whether testhost may use it, again for each new path it runs from, and the question takes the
/// screen. These fail if the switch that prevents it is off, or if code binds a socket where the switch can't reach it.
/// </summary>
public sealed partial class TestSocketTests
{
    [Fact]
    public void Hosting_and_joining_in_a_test_only_bind_loopback()
    {
        Assert.True(UdpNetSocket.ForceLoopback, "The module initializer turns it on for every test.");
        using (var socket = new UdpNetSocket())
            Assert.True(IPAddress.IsLoopback(((IPEndPoint)socket.LocalEndPoint).Address), $"Bound to {socket.LocalEndPoint}.");

        int port;
        using (var free = new UdpNetSocket())
            port = ((IPEndPoint)free.LocalEndPoint).Port;
        var host = new Game(new Scene(), new InputState());
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
    }

    // Everything that binds a socket goes through UdpNetSocket, which the switch keeps on loopback; anything else must
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
