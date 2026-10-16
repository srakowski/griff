using System.IO;
using System.Net;
using System.Windows.Threading;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Play;
using Griff.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The editor's tests make no socket the network can reach, with nothing set for the whole run: a game played in an editor
/// with no window (as in tests) stays on this machine (<see cref="Griff.Runtime.INetwork.OverNetwork"/> is the game's,
/// and off), so a test that hosts never makes Windows ask about the firewall.
/// </summary>
public sealed class TestSockets : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-sockets-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void A_game_played_in_the_tests_hosts_on_this_machine() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        var play = services.GetRequiredService<PlayMode>();
        play.Play();
        try
        {
            var network = play.Game!.Network;
            Assert.False(network.OverNetwork);
            int port;
            using (var free = new UdpNetSocket(loopbackOnly: true))
                port = ((IPEndPoint)free.LocalEndPoint).Port;
            Assert.True(network.Host(port), network.Status);
            Assert.Equal($"Hosting on 127.0.0.1:{port}", network.Status);
            network.Leave();
        }
        finally
        {
            play.Stop();
        }
    });
}
