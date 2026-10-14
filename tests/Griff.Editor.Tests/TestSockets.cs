using System.Runtime.CompilerServices;
using Griff.Networking;

namespace Griff.Editor.Tests;

/// <summary>
/// Keeps every socket the editor's tests make on this machine (see <see cref="UdpNetSocket.ForceLoopback"/>), so no test
/// makes Windows ask about the firewall.
/// </summary>
public sealed class TestSockets
{
    [ModuleInitializer]
    internal static void KeepOnThisMachine() => UdpNetSocket.ForceLoopback = true;

    [Fact]
    public void Sockets_stay_on_this_machine() => Assert.True(UdpNetSocket.ForceLoopback);
}
