using System.Numerics;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>
/// Plays minigames: opens one over the scene (from a Minigame), gives it the input, pauses what's behind it, and closes
/// it with its result.
/// </summary>
internal sealed class MinigameSystem : GameSystem, IMinigames
{
    // A minigame's Play and Stop, and a trigger's or the minigame's end Win, Lose and Restart.
    void IMinigames.Play(Entity from, string? scene) => PlayMinigameFrom(from, scene);

    void IMinigames.End(Entity inside, bool won, int? score) => EndMinigame(inside, won, score);

    void IMinigames.Restart(Entity inside) => RestartMinigame(inside);

    // How far apart a minigame and the rest of the world are kept, in world units, so they never touch or see each other.
    private const float MinigameGap = 64;

    // Minigames ending behind a fade: held still until it covers the screen, and not ended twice.
    private readonly List<Entity> _endingMinigames = [];

    // A minigame scene played on its own that said how it went, and in which step: said once, however many triggers do it.
    private (Entity? Entity, long Step) _endedAlone;

    public override string Name => "Minigames";

    // What a Minigame component plays: the scene it's given, or the one its Minigame names.
    internal void PlayMinigameFrom(Entity from, string? scene)
    {
        var name = string.IsNullOrWhiteSpace(scene) ? from.GetComponent<Scenes.Minigame>()?.Scene : scene;
        if (string.IsNullOrWhiteSpace(name))
        {
            Game.Warn($"\"{from.Name}\" has no minigame scene to play.");
            return;
        }

        StartMinigame(name.Trim(), from);
    }

    public Entity? Minigame => MinigameRoot.Shown(Game.Scene);

    public bool PlayMinigame(string scene, Entity? from = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scene);
        Crowds.ThrowIfRunning("play minigames");
        return StartMinigame(scene.Trim(), from);
    }

    public bool WinMinigame(Entity? inside = null, int? score = null) => EndMinigame(inside, won: true, score);

    public bool LoseMinigame(Entity? inside = null, int? score = null) => EndMinigame(inside, won: false, score);

    // The minigame whose part of the world a top-level entity about to be added is in (where it was spawned: a bullet a
    // minigame's gun fired), with its position made the minigame's own; null outside them all.
    internal Entity? MinigameAt(Entity entity)
    {
        var roots = Game.Scene.ActiveNow<MinigameRoot>();
        if (roots.Count == 0)
            return null;
        var x = entity.Transform.Position.X;
        MinigameRoot? found = null;
        foreach (var root in roots)
        {
            if (!root.Ended && x >= root.Left && (found is null || root.Left > found.Left))
                found = root;
        }

        if (found?.Entity is not { } inside)
            return null;
        var at = inside.Transform.Position;
        entity.Transform.Position -= new Vector3(at.X, at.Y, 0);
        return inside;
    }

    /// <summary>True if <paramref name="entity"/> is out of sight behind the minigame that has the screen (see <see cref="Minigame"/>).</summary>
    internal bool IsBehindMinigame(Entity entity) => MinigameRoot.IsBehind(entity, Minigame);

    public T? FirstInSight<T>(ComponentList<T> components) where T : Component
    {
        var shown = Minigame;
        foreach (var component in components)
        {
            if (!MinigameRoot.IsBehind(component.Entity!, shown))
                return component;
        }

        return null;
    }

    internal bool StartMinigame(string scene, Entity? from)
    {
        if (Game.SceneLoader is null)
            throw new InvalidOperationException("This game can't load scenes (no scene loader is set).");
        if (Game.Network.IsOnline)
        {
            Game.Warn("Minigames can't be played during a network game.");
            return false;
        }

        // Not twice from the same thing, and not over a scene change or another fade.
        if (from is not null && MinigameRoot.OpenedBy(from) is not null || Game.SceneSystem.IsFadingOut || Game.IsChangingScene)
            return false;
        var options = from?.GetComponent<Scenes.Minigame>();
        var fade = options?.Fade ?? 0;
        var opener = from;
        Game.FadeThrough(fade, () =>
        {
            if (opener is null || opener.Scene == Game.Scene)
                OpenMinigame(scene, opener, options, fade);
        });
        return true;
    }

    // Loads the minigame's scene under a root of its own, beside everything else, and hands it the screen and the input.
    private Entity? OpenMinigame(string scene, Entity? from, Scenes.Minigame? options, float fade, IReadOnlyList<string>? focusBefore = null)
    {
        Scene loaded;
        try
        {
            loaded = Game.SceneLoader!(scene);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or FormatException or System.Text.Json.JsonException)
        {
            Game.Warn($"The minigame \"{scene}\" can't be played: {ex.Message}");
            return null;
        }

        var order = 0;
        foreach (var open in Game.Scene.AllNow<MinigameRoot>())
            order = Math.Max(order, open.Order);
        var root = new Entity($"Minigame: {SceneSystem.SceneNameOf(scene)}");
        var info = root.AddComponent<MinigameRoot>();
        (info.Scene, info.OpenedFrom, info.Order, info.Fade, info.MusicBefore) = (SceneSystem.SceneNameOf(scene), from?.Id.ToString(), order + 1, fade, Game.Audio.Music);
        if (options is not null)
            (info.PauseBelow, info.WonMessage, info.LostMessage) = (options.PauseBelow, options.WonMessage, options.LostMessage);

        // Beside the rest of the world: just past the furthest thing in it, so nothing of one reaches the other.
        var (_, right) = ExtentOf(Game.Scene.EntitiesNow);
        var (left, _) = ExtentOf(loaded.EntitiesNow);
        var beside = float.IsFinite(right) && float.IsFinite(left);
        var offset = new Vector2(beside ? right - left + MinigameGap : 0, 0);
        root.Transform.Position = new Vector3(offset, 0);
        info.Left = beside ? right + MinigameGap / 2 : float.IsFinite(right) ? right : -1e9f;

        // Taken from the player's things outside it (what's inside keeps the focus its scene gives it).
        info.FocusBefore = focusBefore ?? TakeFocusFromAll();

        Game.Scene.Add(root);
        var entities = loaded.Roots.ToList();
        var clash = loaded.EntitiesNow.Any(e => Game.Scene.Find(e.Id) is not null);
        var serializer = clash ? new SceneSerializer(Game.Components ?? ComponentRegistry.CreateDefault()) : null;
        foreach (var entity in entities)
        {
            loaded.Remove(entity);
            Game.Scene.Add(serializer is null ? entity : serializer.Clone(entity), root);
        }

        foreach (var placed in root.SelfAndDescendants())
        {
            foreach (var component in placed.ComponentList)
            {
                if (component is IWorldPlaced moved)
                    moved.Shift(offset);
            }
        }

        Game.TableSystem.ApplyTables(root);
        if (Game.Focused is { Entity: { } picked } && MinigameRoot.IsBehind(picked, root))
            Game.MenuSystem.SetFocus(null);
        Game.SceneSystem.LoadCarried(root);
        Game.PanelSystem.SyncPanels();
        return root;
    }

    // Everything outside the minigames that took the player's input gives it up; returns which had it, to give it back.
    private List<string> TakeFocusFromAll()
    {
        var had = new List<string>();
        foreach (var entity in Game.Scene.EntitiesNow)
        {
            var took = false;
            foreach (var component in entity.ComponentList)
            {
                if (!ComponentFocus.Has(component.GetType()) || !ComponentFocus.Takes(component))
                    continue;
                ComponentFocus.Set(component, false);
                took = true;
            }

            if (took)
                had.Add(entity.Id.ToString());
        }

        return had;
    }

    // The leftmost and rightmost reach of what's in a scene in the world, roughly: where things are, what tilemaps and
    // colliders cover, and what cameras see.
    private (float Left, float Right) ExtentOf(IReadOnlyList<Entity> entities)
    {
        var (left, right) = (float.PositiveInfinity, float.NegativeInfinity);
        foreach (var entity in entities)
        {
            var x = entity.Transform.WorldPosition.X;
            var (from, to) = (x, x);
            foreach (var component in entity.ComponentList)
            {
                var box = component switch
                {
                    Tilemap map => map.Area,
                    Collider collider => collider.WorldBounds,
                    Camera camera => new Box(new Vector2(x, 0), new Vector2(camera.Size * Math.Max(Game.ViewAspect, 1), 0)),
                    _ => (Box?)null,
                };
                if (box is { } b && float.IsFinite(b.Min.X) && float.IsFinite(b.Max.X))
                    (from, to) = (Math.Min(from, b.Min.X), Math.Max(to, b.Max.X));
            }

            (left, right) = (Math.Min(left, from), Math.Max(right, to));
        }

        return (left, right);
    }

    internal bool EndMinigame(Entity? inside, bool won, int? score)
    {
        Crowds.ThrowIfRunning("end minigames");
        var top = inside;
        while (top?.Parent is { } parent)
            top = parent;
        var root = inside is null ? Minigame : top;
        if (root?.GetComponent<MinigameRoot>() is not { } info)
        {
            // Played on its own, it says how it went all the same (once a step, however many triggers on it do it).
            if (inside is not null && Game.Runs(inside) && _endedAlone != (inside, Game.Scene.Step))
            {
                _endedAlone = (inside, Game.Scene.Step);
                Game.Send(won ? MinigameRoot.DefaultWonMessage : MinigameRoot.DefaultLostMessage, score ?? 0, from: inside);
            }

            return false;
        }

        if (info.Ended)
            return false;

        if (_endingMinigames.Contains(root) || !Game.Runs(root))
            return false;
        var result = score ?? ScoreOf(root);
        _endingMinigames.Add(root);
        Game.PauseSystem.SyncPauses();
        var fade = Game.SceneSystem.IsFadingOut ? 0 : info.Fade;
        Game.FadeThrough(fade, () => CloseMinigame(root, (won, result)));
        return true;
    }

    // Starts a minigame over: it closes with no result and opens again, as it was opened.
    internal void RestartMinigame(Entity inside)
    {
        var root = inside.GetComponent<MinigameRoot>() is { Ended: false } ? inside : MinigameRoot.Of(inside);
        if (root?.GetComponent<MinigameRoot>() is not { Scene: { Length: > 0 } scene } info || _endingMinigames.Contains(root))
            return;
        var from = OpenerOf(info);
        var before = info.FocusBefore;
        CloseMinigame(root, null, giveFocusBack: false);
        OpenMinigame(scene, from, from?.GetComponent<Scenes.Minigame>(), info.Fade, before);
    }

    // Closes a minigame: it goes (at the end of the step), the input goes back to what had it, what it carried is read again
    // outside it, and the result (if it has one) goes to what opened it.
    private void CloseMinigame(Entity root, (bool Won, int Score)? result, bool giveFocusBack = true)
    {
        _endingMinigames.Remove(root);
        if (root.Scene != Game.Scene || root.GetComponent<MinigameRoot>() is not { Ended: false } info)
            return;

        // What it carried (a counter's or inventory's Save As) is saved one last time, then read again by what's outside it.
        var carried = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in root.SelfAndDescendants())
        {
            foreach (var component in entity.ComponentList)
            {
                if (component is ICarried { SaveAs: { } key } kept && !string.IsNullOrWhiteSpace(key) && kept.Loaded)
                {
                    Game.SceneSystem.Carry(kept, always: true);
                    carried.Add(key.Trim());
                }
            }
        }

        info.Ended = true;
        Game.Destroy(root);
        if (giveFocusBack)
            GiveFocusBack(info.FocusBefore);
        foreach (var other in Game.Scene.AllNow<ICarried>())
        {
            if (other is Component { Entity: { } owner } && !owner.IsSelfOrDescendantOf(root) && other.SaveAs is { } key && carried.Contains(key.Trim()))
                other.Loaded = false;
        }

        Game.SceneSystem.LoadCarried(null);
        if (!string.Equals(Game.Audio.Music, info.MusicBefore, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(info.MusicBefore))
                Game.Audio.StopMusic();
            else
                Game.Audio.CrossfadeMusic(info.MusicBefore, 0.5f);
        }

        Game.PanelSystem.SyncPanels();
        if (result is not { } ended)
            return;
        var (won, score) = ended;
        var from = OpenerOf(info);
        if (from?.GetComponent<Scenes.Minigame>() is { } minigame)
        {
            (minigame.Won, minigame.Score, minigame.Plays) = (won, score, minigame.Plays + 1);
            if (won)
                (minigame.Wins, minigame.Best) = (minigame.Wins + 1, minigame.Wins == 0 ? score : Math.Max(minigame.Best, score));
            minigame.RaiseEnded(won, score);
        }

        var message = won ? info.WonMessage : info.LostMessage;
        if (!string.IsNullOrWhiteSpace(message))
            Game.Send(message.Trim(), score, from);
    }

    private Entity? OpenerOf(MinigameRoot info) =>
        Guid.TryParse(info.OpenedFrom, out var id) && Game.Scene.Find(id) is { } found && found.Scene == Game.Scene ? found : null;

    // Gives the player's input back to what had it before a minigame took it.
    private void GiveFocusBack(IReadOnlyList<string> ids)
    {
        foreach (var text in ids)
        {
            if (!Guid.TryParse(text, out var id) || Game.Scene.Find(id) is not { } entity)
                continue;
            foreach (var component in entity.ComponentList)
            {
                if (ComponentFocus.Has(component.GetType()))
                    ComponentFocus.Set(component, true);
            }
        }
    }

    // A minigame's own score: its first Score output (a board's), or its first counter's count, or 0.
    private static int ScoreOf(Entity root)
    {
        Counter? counter = null;
        foreach (var entity in root.SelfAndDescendants())
        {
            if (ComponentOutputs.Find(entity, "Score") is var (component, output) && output.IsNumber)
                return ComponentOutputs.CanRead(component) ? (int)MathF.Round(output.ReadNumber(component)) : 0;
            counter ??= entity.GetComponent<Counter>();
        }

        return counter?.Value ?? 0;
    }

    // The minigames whose fade out is under way are held still meanwhile, as are those that pause what's below them: it
    // all, apart from the minigames opened after them.
    internal void PauseForMinigames(List<Entity> groups)
    {
        var roots = Game.Scene.ActiveNow<MinigameRoot>();
        if (roots.Count == 0)
            return;
        var lowest = int.MaxValue;
        foreach (var root in roots)
        {
            if (root.Ended)
                continue;
            if (_endingMinigames.Contains(root.Entity!))
                PauseSystem.AddGroup(groups, root.Entity!);
            if (root.PauseBelow)
                lowest = Math.Min(lowest, root.Order);
        }

        if (lowest == int.MaxValue)
            return;

        // Each top-level entity once (a scene can have thousands), checked only against the few groups already listed.
        var listed = groups.Count;
        var tops = Game.Scene.Roots;
        for (var t = 0; t < tops.Count; t++)
        {
            var top = tops[t];
            if (top.GetComponent<MinigameRoot>() is { Ended: false } other && other.Order >= lowest)
                continue;
            var already = false;
            for (var i = 0; i < listed && !already; i++)
                already = groups[i] == top;
            if (!already)
                groups.Add(top);
        }
    }

    // Minigames being played end with the scene, giving the input back first.
    internal void EndMinigamesWithScene()
    {
        foreach (var root in Game.Scene.AllNow<MinigameRoot>())
        {
            if (!root.Ended)
            {
                GiveFocusBack(root.FocusBefore);
                root.Ended = true;
            }
        }

        _endingMinigames.Clear();
    }
}
