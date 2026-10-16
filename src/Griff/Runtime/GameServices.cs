using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// What a game's systems provide its components, asked for by type (see <see cref="IGameService"/>): each service is the
/// one system's that owns that part of the game, found among the systems running (Griff's, the modules', then those
/// scripts added). A lookup makes no garbage, and is safe while the scripts of a crowd run across the cores (systems are
/// added and taken away only between).
/// </summary>
internal sealed class GameServices(Game game) : IGameServices
{
    public T? Get<T>() where T : class, IGameService
    {
        var systems = game.Systems;
        for (var i = 0; i < systems.Count; i++)
        {
            if (systems[i] is T service && !systems[i].IsRemoved)
                return service;
        }

        return null;
    }
}
