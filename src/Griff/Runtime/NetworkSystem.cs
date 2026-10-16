using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Runs the network session at either end of each step and the commands players' machines and the host send each other
/// (Griff's own and the modules'), and on the host runs each other player's tools for them, as them.
/// </summary>
internal sealed class NetworkSystem : GameSystem
{
    [NotState("what's registered as the game is set up")]
    private readonly Dictionary<string, Action<NetPlayer?, object?>> _netCommands = new(StringComparer.Ordinal);

    [NotState("what's registered as the game is set up")]
    private readonly Dictionary<string, Action<object?>> _hostCommands = new(StringComparer.Ordinal);

    public override string Name => "Network";

    internal void BeginNetwork() => Game.Net.BeginFrame(Game.Time.UnscaledDelta);

    internal void EndNetwork() => Game.Net.EndFrame(Game.Time.UnscaledDelta);

    // The host runs each other player's tools for them, as them: each system lays out its HUD for their screen, then runs
    // their controls.
    internal void RunOtherPlayersTools()
    {
        if (!Game.Net.IsHost)
            return;
        Game.ForEachRemotePlayer(player =>
        {
            Game.HudSystem.LayOutHuds();
            for (var i = 0; i < Game.Systems.Count; i++)
                Game.Systems[i].RunForOtherPlayer(player);
        });
    }

    // ----- Called by the session -----

    /// <summary>A scene started in a network game (the host's, or the copy of it here): settle who's who in it.</summary>
    internal void NetSceneStarted()
    {
        Game.Scene.PreferNamed = Game.Net.IsOnline ? e => Game.Net.IsMine(e) && NetSession.OwnerIdOf(e) != 0 : null;
        NetPlayersChanged();
    }

    /// <summary>The session ended: this machine carries on by itself.</summary>
    internal void NetEnded()
    {
        Game.Scene.PreferNamed = null;
        foreach (var entity in Game.Scene.EntitiesNow)
            entity.NetFlags = NetFlags.None;
    }

    /// <summary>The host went away: back to the menu, or this scene from the start.</summary>
    internal void LeftHost(string? exitScene)
    {
        if (Game.SceneLoader is null)
            return;
        if (!string.IsNullOrWhiteSpace(exitScene))
            Game.LoadScene(exitScene.Trim());
        else if (Game.SceneName is not null)
            Game.ReloadScene();
    }

    internal void RemoveForNetwork(Entity entity)
    {
        Game.LifetimeSystem.CancelDestroy(entity);
        if (entity.Scene == Game.Scene)
            Game.Scene.Remove(entity);
    }

    internal void PlayRelayedSound(string sound, float volume, float pitch, Vector2? at)
    {
        if (string.IsNullOrWhiteSpace(sound))
            return;
        if (at is { } point)
            Game.AtmosphereSystem.PlaySoundAt(sound, point, volume, pitch, relay: false);
        else
            Game.Audio.Play(sound, volume, 0, pitch);
    }

    /// <summary>Something a player's machine asked the host to do for them (an order for their units, crafting).</summary>
    internal void RunNetCommand(NetPlayer? player, string name, object? args)
    {
        if (_netCommands.TryGetValue(name, out var command))
            command(player, args);
    }

    internal void OnNetCommand(string name, Action<NetPlayer?, object?> command) => _netCommands[name] = command;

    /// <summary>Something the host told this player's machine (what the host's run of their tools wants shown here).</summary>
    internal void RunHostCommand(string name, object? args)
    {
        if (_hostCommands.TryGetValue(name, out var command))
            command(args);
    }

    internal void OnHostCommand(string name, Action<object?> command) => _hostCommands[name] = command;

    public ComponentList<T> Mine<T>(bool evenPaused = false) where T : Component =>
        Game.Net.IsOnline ? MineOnline<T>(evenPaused) : evenPaused ? Game.Present<T>() : Game.Active<T>();

    private ComponentList<T> MineOnline<T>(bool evenPaused) where T : Component
    {
        var id = (Game.ActingFor ?? Game.Net.LocalPlayer)?.Id ?? 0;
        return Game.Where(evenPaused ? Game.Scene.ActiveNow<T>() : Game.Scene.RunningNow<T>(), entity =>
        {
            var owner = NetSession.OwnerIdOf(entity);
            return (owner != 0 && owner == id) || (owner == 0 && Game.ActingFor is null && Game.Runs(entity));
        });
    }

    public void Claim(Entity spawned, Entity by)
    {
        // A shot is on its shooter's team, so it doesn't hurt the shooter's allies.
        if (Teams.Of(spawned).Length == 0 && Teams.Of(by) is { Length: > 0 } team)
            spawned.AddComponent(new Team { Name = team });

        var owner = Game.Net.IsOnline ? NetSession.OwnerIdOf(by) : 0;
        if (owner == 0)
            return;
        var networked = spawned.GetComponent<Networked>() ?? spawned.AddComponent<Networked>();
        networked.Owner = owner;

        // Shots in their player's color, a little lighter, so it's clear whose they are.
        if (Game.Net.Settings is { ColorPlayers: true } && Game.Net.PlayerById(owner) is { } player && spawned.GetComponent<Sprite>() is { } sprite)
        {
            var c = player.Color;
            sprite.Tint = new Color(c.R + (1 - c.R) * 0.45f, c.G + (1 - c.G) * 0.45f, c.B + (1 - c.B) * 0.45f, sprite.Tint.A);
        }
    }

    public bool CanHurt(Entity? source, Entity target)
    {
        if (source is null)
            return true;
        var friendlyFire = Game.Net.IsOnline && Game.Net.Settings is { FriendlyFire: true };
        if (!friendlyFire && Teams.Allies(Game.TeamOf(source), Game.TeamOf(target)))
            return false;
        if (!Game.Net.IsOnline)
            return true;
        var by = Game.Net.OwnerOf(source);
        var hurt = Game.Net.OwnerOf(target);
        return by is null || hurt is null || by != hurt;
    }

    public void PlayShared(string sound)
    {
        Game.Audio.Play(sound);
        Game.Net.RelaySound(sound, 1, 1, null);
    }

    public void ShakeFor(Entity entity, float strength, float duration)
    {
        if (Game.Net.IsMine(entity))
            Game.ShakeCamera(strength, duration);
        else if (Game.Net.OwnerOf(entity) is { } player)
            Game.Net.SendShake(player, strength, duration);
    }

    /// <summary>The list of players changed (someone joined or left, or changed team), or a network game's scene started.</summary>
    internal void NetPlayersChanged()
    {
        if (!Game.Net.IsOnline)
            return;
        foreach (var system in Game.Systems.ToList())
            system.RunPlayersChanged();
    }

    // What players' machines can ask the host to do for them.
    internal void ListenForCommands()
    {
        OnNetCommand(TurnSystem.EndTurnCommand, Game.TurnSystem.RunEndTurn);
        OnNetCommand(TurnSystem.GridStepCommand, Game.TurnSystem.RunGridStep);
        OnNetCommand(GridSystem.GridPushCommand, Game.GridSystem.RunGridPush);
        OnNetCommand(GridSystem.BumpCommand, Game.GridSystem.RunBump);
        OnNetCommand(MenuSystem.ButtonDoCommand, Game.MenuSystem.RunButtonDo);
    }
}
