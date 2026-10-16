using System.Numerics;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

// Minigames: another scene played on top of this one, under an entity of its own (with a MinigameRoot) set beside the rest
// of the world, with the screen and the player's input while it's played, its result sent back as a message. Everything
// about it is kept in the scene (the root's component), so rewinds and saved games take it in.
public sealed partial class Game
{
    // How far apart a minigame and the rest of the world are kept, in world units, so they never touch or see each other.
    private const float MinigameGap = 64;

    // Minigames ending behind a fade: held still until it covers the screen, and not ended twice.
    private readonly List<Entity> _endingMinigames = [];

    // A minigame scene played on its own that said how it went, and in which step: said once, however many triggers do it.
    private (Entity? Entity, long Step) _endedAlone;

    /// <summary>
    /// The open minigame that has the screen and the player's input (the root entity its scene was loaded under), or null
    /// while none is being played. See <see cref="PlayMinigame"/>.
    /// </summary>
    public Entity? Minigame => MinigameRoot.Shown(Scene);

    /// <summary>
    /// Plays the scene called <paramref name="scene"/> on top of this one as a minigame: it's loaded under an entity of its
    /// own (with a <see cref="MinigameRoot"/>) beside the rest of the world, takes the screen (its own camera, HUD and menus)
    /// and the player's input, and pauses everything else, as <paramref name="from"/>'s <see cref="Scenes.Minigame"/> says (or
    /// with its defaults without one). It ends with <see cref="WinMinigame"/> or <see cref="LoseMinigame"/> (a trigger's Win
    /// Minigame or Lose Minigame in it), and the result comes back as <paramref name="from"/>'s Won or Lost message with
    /// the score. True if it's on its way; not during a network game, nor while the screen is fading to something else.
    /// </summary>
    /// <example><code>Game.PlayMinigame("Lockpick", from: Entity);</code></example>
    public bool PlayMinigame(string scene, Entity? from = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scene);
        Crowds.ThrowIfRunning("play minigames");
        return StartMinigame(scene.Trim(), from);
    }

    /// <summary>
    /// Ends the minigame <paramref name="inside"/> is in (or the one with the screen) as won, with <paramref name="score"/>
    /// (or, without one, the minigame's own score: its first Score output, or its first counter's count). It closes, the
    /// input goes back to what had it, and whatever opened it sends its Won Message with the score. Played on its own (not
    /// as a minigame), it just sends "Minigame Won". True if there was one to end.
    /// </summary>
    public bool WinMinigame(Entity? inside = null, int? score = null) => EndMinigame(inside, won: true, score);

    /// <summary>Like <see cref="WinMinigame"/>, as lost: whatever opened it sends its Lost Message.</summary>
    public bool LoseMinigame(Entity? inside = null, int? score = null) => EndMinigame(inside, won: false, score);

    // The minigame whose part of the world a top-level entity about to be added is in (where it was spawned: a bullet a
    // minigame's gun fired), with its position made the minigame's own; null outside them all.
    private Entity? MinigameAt(Entity entity)
    {
        var roots = Scene.ActiveNow<MinigameRoot>();
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

    /// <summary>
    /// The first of `components` not behind the minigame that has the screen: the one a HUD shows.
    /// </summary>
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

    void IGameContext.PlayMinigame(Entity from, string? scene)
    {
        var name = string.IsNullOrWhiteSpace(scene) ? from.GetComponent<Scenes.Minigame>()?.Scene : scene;
        if (string.IsNullOrWhiteSpace(name))
        {
            Warn($"\"{from.Name}\" has no minigame scene to play.");
            return;
        }

        StartMinigame(name.Trim(), from);
    }

    void IGameContext.EndMinigame(Entity inside, bool won, int? score) => EndMinigame(inside, won, score);

    void IGameContext.RestartMinigame(Entity inside) => RestartMinigame(inside);

    private bool StartMinigame(string scene, Entity? from)
    {
        if (SceneLoader is null)
            throw new InvalidOperationException("This game can't load scenes (no scene loader is set).");
        if (Net.IsOnline)
        {
            Warn("Minigames can't be played during a network game.");
            return false;
        }

        // Not twice from the same thing, and not over a scene change or another fade.
        if (from is not null && MinigameRoot.OpenedBy(from) is not null || _fadingOut is not null || IsChangingScene)
            return false;
        var options = from?.GetComponent<Scenes.Minigame>();
        var fade = options?.Fade ?? 0;
        var opener = from;
        FadeThrough(fade, () =>
        {
            if (opener is null || opener.Scene == Scene)
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
            loaded = SceneLoader!(scene);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or FormatException or System.Text.Json.JsonException)
        {
            Warn($"The minigame \"{scene}\" can't be played: {ex.Message}");
            return null;
        }

        var order = 0;
        foreach (var open in Scene.AllNow<MinigameRoot>())
            order = Math.Max(order, open.Order);
        var root = new Entity($"Minigame: {SceneNameOf(scene)}");
        var info = root.AddComponent<MinigameRoot>();
        (info.Scene, info.OpenedFrom, info.Order, info.Fade, info.MusicBefore) = (SceneNameOf(scene), from?.Id.ToString(), order + 1, fade, Audio.Music);
        if (options is not null)
            (info.PauseBelow, info.WonMessage, info.LostMessage) = (options.PauseBelow, options.WonMessage, options.LostMessage);

        // Beside the rest of the world: just past the furthest thing in it, so nothing of one reaches the other.
        var (_, right) = ExtentOf(Scene.EntitiesNow);
        var (left, _) = ExtentOf(loaded.EntitiesNow);
        var beside = float.IsFinite(right) && float.IsFinite(left);
        var offset = new Vector2(beside ? right - left + MinigameGap : 0, 0);
        root.Transform.Position = new Vector3(offset, 0);
        info.Left = beside ? right + MinigameGap / 2 : float.IsFinite(right) ? right : -1e9f;

        // Taken from the player's things outside it (what's inside keeps the focus its scene gives it).
        info.FocusBefore = focusBefore ?? TakeFocusFromAll();

        Scene.Add(root);
        var entities = loaded.Roots.ToList();
        var clash = loaded.EntitiesNow.Any(e => Scene.Find(e.Id) is not null);
        var serializer = clash ? new SceneSerializer(Components ?? ComponentRegistry.CreateDefault()) : null;
        foreach (var entity in entities)
        {
            loaded.Remove(entity);
            Scene.Add(serializer is null ? entity : serializer.Clone(entity), root);
        }

        foreach (var placed in root.SelfAndDescendants())
        {
            foreach (var component in placed.ComponentList)
            {
                if (component is IWorldPlaced moved)
                    moved.Shift(offset);
            }
        }

        ApplyTables(root);
        if (_focus is { Entity: { } picked } && MinigameRoot.IsBehind(picked, root))
            _focus = null;
        LoadCarried(root);
        SyncPanels();
        return root;
    }

    // Everything outside the minigames that took the player's input gives it up; returns which had it, to give it back.
    private List<string> TakeFocusFromAll()
    {
        var had = new List<string>();
        foreach (var entity in Scene.EntitiesNow)
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
                    Camera camera => new Box(new Vector2(x, 0), new Vector2(camera.Size * Math.Max(ViewAspect, 1), 0)),
                    _ => (Box?)null,
                };
                if (box is { } b && float.IsFinite(b.Min.X) && float.IsFinite(b.Max.X))
                    (from, to) = (Math.Min(from, b.Min.X), Math.Max(to, b.Max.X));
            }

            (left, right) = (Math.Min(left, from), Math.Max(right, to));
        }

        return (left, right);
    }

    private bool EndMinigame(Entity? inside, bool won, int? score)
    {
        Crowds.ThrowIfRunning("end minigames");
        var top = inside;
        while (top?.Parent is { } parent)
            top = parent;
        var root = inside is null ? Minigame : top;
        if (root?.GetComponent<MinigameRoot>() is not { } info)
        {
            // Played on its own, it says how it went all the same (once a step, however many triggers on it do it).
            if (inside is not null && Runs(inside) && _endedAlone != (inside, Scene.Step))
            {
                _endedAlone = (inside, Scene.Step);
                Send(won ? MinigameRoot.DefaultWonMessage : MinigameRoot.DefaultLostMessage, score ?? 0, from: inside);
            }

            return false;
        }

        if (info.Ended)
            return false;

        if (_endingMinigames.Contains(root) || !Runs(root))
            return false;
        var result = score ?? ScoreOf(root);
        _endingMinigames.Add(root);
        SyncPauses();
        var fade = _fadingOut is null ? info.Fade : 0;
        FadeThrough(fade, () => CloseMinigame(root, (won, result)));
        return true;
    }

    // Starts a minigame over: it closes with no result and opens again, as it was opened.
    private void RestartMinigame(Entity inside)
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
        if (root.Scene != Scene || root.GetComponent<MinigameRoot>() is not { Ended: false } info)
            return;

        // What it carried (a counter's or inventory's Save As) is saved one last time, then read again by what's outside it.
        var carried = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in root.SelfAndDescendants())
        {
            foreach (var component in entity.ComponentList)
            {
                if (component is ICarried { SaveAs: { } key } kept && !string.IsNullOrWhiteSpace(key) && kept.Loaded)
                {
                    Carry(kept, always: true);
                    carried.Add(key.Trim());
                }
            }
        }

        info.Ended = true;
        Destroy(root);
        if (giveFocusBack)
            GiveFocusBack(info.FocusBefore);
        foreach (var other in Scene.AllNow<ICarried>())
        {
            if (other is Component { Entity: { } owner } && !owner.IsSelfOrDescendantOf(root) && other.SaveAs is { } key && carried.Contains(key.Trim()))
                other.Loaded = false;
        }

        LoadCarried(null);
        if (!string.Equals(Audio.Music, info.MusicBefore, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(info.MusicBefore))
                Audio.StopMusic();
            else
                Audio.CrossfadeMusic(info.MusicBefore, 0.5f);
        }

        SyncPanels();
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
            Send(message.Trim(), score, from);
    }

    private Entity? OpenerOf(MinigameRoot info) =>
        Guid.TryParse(info.OpenedFrom, out var id) && Scene.Find(id) is { } found && found.Scene == Scene ? found : null;

    // Gives the player's input back to what had it before a minigame took it.
    private void GiveFocusBack(IReadOnlyList<string> ids)
    {
        foreach (var text in ids)
        {
            if (!Guid.TryParse(text, out var id) || Scene.Find(id) is not { } entity)
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
                return (int)MathF.Round(output.ReadNumber(component));
            counter ??= entity.GetComponent<Counter>();
        }

        return counter?.Value ?? 0;
    }

    // The minigames whose fade out is under way are held still meanwhile, as are those that pause what's below them: it
    // all, apart from the minigames opened after them.
    private void PauseForMinigames(List<Entity> groups)
    {
        var roots = Scene.ActiveNow<MinigameRoot>();
        if (roots.Count == 0)
            return;
        var lowest = int.MaxValue;
        foreach (var root in roots)
        {
            if (root.Ended)
                continue;
            if (_endingMinigames.Contains(root.Entity!))
                AddGroup(groups, root.Entity!);
            if (root.PauseBelow)
                lowest = Math.Min(lowest, root.Order);
        }

        if (lowest == int.MaxValue)
            return;

        // Each top-level entity once (a scene can have thousands), checked only against the few groups already listed.
        var listed = groups.Count;
        var tops = Scene.Roots;
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
}

