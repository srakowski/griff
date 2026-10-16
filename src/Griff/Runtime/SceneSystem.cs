using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>
/// Changes scenes: loads the one asked for after its fade out (and fades in), carries values and kept entities across,
/// remembers the scenes left that say so and puts arrivals at their doors; a network game's players load the host's.
/// </summary>
internal sealed class SceneSystem : GameSystem
{
    // How close to fully covered (or clear) a fade has to get to count as there.
    private const float FadeSnap = 1e-4f;

    private string? _pendingScene;

    // A fade out on its way to a scene, or to a call (FadeThrough) with no scene change.
    private (string? Scene, Action? Then, float Seconds)? _fadingOut;

    private float _fadeInSeconds;

    // Kept as it was when the step began, too, to draw the screen between steps.
    private Smoothed<float> _fade;

    private Color _fadeColor = Color.Black;

    // The scene being gone to starts as its file has it, even if it's remembered (a restart).
    private bool _freshScene;

    private bool _netLoading;

    // The scenes remembered as they were left, by name: each the scene's file, squeezed (see RememberScene).
    private readonly Dictionary<string, byte[]> _remembered = new(StringComparer.OrdinalIgnoreCase);

    // Where the players arrive in the scene being gone to, and the name of whoever went (a door's user), until it's there.
    private (string Spot, string? Traveller)? _arriving;

    // New Game is under way: what the scene being left carries isn't saved again on the way out.
    private bool _startingOver;

    private Entity? _arrivedAt;

    public override string Name => "Scenes";

    public float FadeAmount
    {
        get => _fade.Value;
        private set => _fade.Set(Game.Scene, value);
    }

    // How much of the screen the fade covers in a frame drawn as `blend` says.
    internal float FadeDrawn(StepBlend blend) => blend.Of(_fade);

    public Color FadeColor
    {
        get => _fadeColor;
        private set => _fadeColor = value;
    }

    public bool IsUncovering => _fadingOut is null && FadeAmount > 0;

    public bool IsChangingScene => _fadingOut is { Scene: not null } || _pendingScene is not null;

    // A fade or a scene change under way: a door waits for it rather than starting another trip.
    internal bool IsFadingOrChangingScene => _fadingOut is not null || _pendingScene is not null;

    // A fade out to another scene, or to a minigame, is under way.
    internal bool IsFadingOut => _fadingOut is not null;

    // A saved game being loaded takes the place of a scene change asked for.
    internal void CancelSceneChange() => _pendingScene = null;

    public void LoadScene(string nameOrPath) => LoadScene(nameOrPath, 0);

    public void LoadScene(string nameOrPath, float fade, Color? color = null) => LoadScene(nameOrPath, fade, color, arriveAt: null);

    public void LoadScene(string nameOrPath, float fade, Color? color, string? arriveAt) =>
        LoadScene(nameOrPath, fade, color, arriveAt, traveller: null, fresh: false);

    public void LoadScene(string nameOrPath, float fade, Color? color, string? arriveAt, Entity? traveller, bool fresh)
    {
        Crowds.ThrowIfRunning("change scenes");
        if (Game.SceneLoader is null)
            throw new InvalidOperationException("This game can't load scenes (no scene loader is set).");

        // On a player's machine, going to another scene (a menu's Quit to Title) leaves the host's game.
        if (Game.Net.IsClient || Game.Net.IsConnecting)
            Game.Net.Leave();

        _arriving = string.IsNullOrWhiteSpace(arriveAt) ? null : (arriveAt.Trim(), traveller?.Name);
        (_freshScene, _startingOver) = (fresh, false);

        if (fade > 0)
        {
            _fadingOut = (nameOrPath, null, fade);
            _fadeInSeconds = 0;
            FadeColor = color ?? Color.Black;
            return;
        }

        // Cut straight there; a fade on its way out clears over the time it would have taken.
        if (_fadingOut is { } fading)
            _fadeInSeconds = fading.Seconds;
        _fadingOut = null;
        _pendingScene = nameOrPath;
    }

    public void ReloadScene() => ReloadScene(0);

    public void ReloadScene(float fade, Color? color = null) =>
        LoadScene(Game.SceneName ?? throw new InvalidOperationException("This game doesn't know its scene's name, so it can't reload it."),
            fade, color, arriveAt: null, traveller: null, fresh: true);

    public static void KeepBetweenScenes(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Crowds.ThrowIfRunning("keep entities between scenes");
        if (entity.GetComponent<Scenes.KeepBetweenScenes>() is null)
            entity.AddComponent<Scenes.KeepBetweenScenes>();
    }

    // A fade out ends by requesting the scene, which switches at the end of this frame; the fade in starts on the next.
    internal void AdvanceFade()
    {
        var seconds = Game.Time.UnscaledDelta;
        if (_fadingOut is { } fading)
        {
            FadeAmount = Snap(FadeAmount + seconds / fading.Seconds);
            if (FadeAmount < 1)
                return;
            _fadingOut = null;
            _pendingScene = fading.Scene;
            _fadeInSeconds = fading.Seconds;
            fading.Then?.Invoke();
        }
        else if (_fadeInSeconds > 0 && _pendingScene is null)
        {
            FadeAmount = Snap(FadeAmount - seconds / _fadeInSeconds);
            if (FadeAmount <= 0)
                _fadeInSeconds = 0;
        }

        // Rounds the last sliver off, so 0.1 + 0.1 + 0.1 of a 0.3 second fade can't cost an extra frame.
        static float Snap(float amount) => amount >= 1 - FadeSnap ? 1 : amount <= FadeSnap ? 0 : amount;
    }

    internal void SwitchSceneIfRequested()
    {
        if (Game.SaveSystem.SwitchToLoadedGame() || _pendingScene is not { } name)
            return;
        _pendingScene = null;
        SwitchScene(name);
    }

    // The lambdas here capture what's staying, so they're kept out of the step every frame takes. A saved game's scene
    // comes `loaded`; otherwise it's loaded by name.
    internal void SwitchScene(string name, Scene? loaded = null)
    {
        // Minigames being played end with the scene, giving the input back first (so a remembered scene has it); what the
        // scene carries is saved as it is, and the scene remembered as it's left if it says so (not when it's starting over,
        // nor for a saved game being loaded, which brings its own).
        Game.MinigameSystem.EndMinigamesWithScene();
        var target = SceneNameOf(name);
        var restarting = loaded is null && _freshScene;
        if (!_startingOver || loaded is not null)
            FlushCarried();
        _startingOver = false;
        if (restarting)
            _remembered.Remove(target);
        if (loaded is null && !restarting)
            RememberLeaving();
        _freshScene = false;

        Game.SystemsSceneEnding();

        var next = loaded ?? (restarting ? null : Recalled(name)) ?? Game.SceneLoader!(name);

        // Nothing glides into the new scene: after this step it's drawn as it is.
        next.Step = next.FirstStep = Game.Scene.Step;

        // Kept entities (see KeepBetweenScenes) move across still running; a kept child goes along with a kept ancestor.
        var moving = KeptEntities();
        var staying = moving.SelectMany(e => e.SelfAndDescendants()).ToHashSet();

        foreach (var behavior in Game.Scene.Entities.Where(e => !staying.Contains(e)).SelectMany(e => e.GetComponents<Behavior>()))
            behavior.RunDestroy();
        Game.LifetimeSystem.ForgetDestroys();

        foreach (var entity in moving)
        {
            Game.Scene.Remove(entity);
            Arrive(entity, next);
        }

        Game.Scene = next;
        if (name.Length > 0 || loaded is null)
            Game.SceneName = SceneNameOf(name);
        Game.MessageSystem.PruneListeners(next);
        Game.MenuSystem.ForgetControlsOutside(next);
        if (loaded is not null)
            _arriving = null;
        var arrived = PlaceArrivals();
        Game.SystemsSceneChanged();
        if (arrived is not null)
            Game.LevelSystem.ArrivedThroughDoors(arrived);
        LoadCarried(null);
        Game.AtmosphereSystem.StopSoundSources();
        Game.MenuSystem.SetFocus(null);
        Game.PanelSystem.SyncPanels();
        if (Game.Net.IsHost)
            Game.Net.StartHostScene();
        else if (_netLoading)
        {
            _netLoading = false;
            Game.Net.HostSceneLoaded();
        }

        Game.RaiseSceneChanged(next);
    }

    // "Scenes/Level2.scene" and "Level2" are both "Level2".
    internal static string SceneNameOf(string nameOrPath)
    {
        var name = nameOrPath[(nameOrPath.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) ? name[..^".scene".Length] : name;
    }

    public void FadeThrough(float seconds, Action then, Color? color = null)
    {
        ArgumentNullException.ThrowIfNull(then);
        if (!(seconds > 0))
        {
            then();
            return;
        }

        _fadingOut = (null, then, seconds);
        _fadeInSeconds = 0;
        FadeColor = color ?? Color.Black;
    }

    /// <summary>Loads the scene the host is in, for this machine to show its copy of.</summary>
    internal void LoadSceneForNetwork(string name)
    {
        if (Game.SceneLoader is null || string.IsNullOrEmpty(name))
        {
            // Nothing to load it with (tests): use the scene already showing.
            Game.Net.HostSceneLoaded();
            return;
        }

        _netLoading = true;
        _fadingOut = null;
        _pendingScene = name;
    }

    public IReadOnlyCollection<string> RememberedScenes => [.. _remembered.Keys];

    public bool Remembers(string scene) => _remembered.ContainsKey(SceneNameOf(scene));

    public bool ForgetScene(string scene)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scene);
        return _remembered.Remove(SceneNameOf(scene.Trim()));
    }

    public void ForgetScenes() => _remembered.Clear();

    public void NewGame(string scene, float fade = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scene);
        Game.SaveData.ClearProgress();
        ForgetScenes();
        LoadScene(scene.Trim(), fade, null, arriveAt: null, traveller: null, fresh: true);
        _startingOver = true;
    }

    public Entity? ArrivedAt
    {
        get => _arrivedAt;
        private set => _arrivedAt = value;
    }

    // ----- Carried values -----

    // Each value with a Save As takes what's saved (once, as it's first seen), and saves what it has as that changes,
    // where the game is run. Read before anything else in the step, so a HUD shows it from the first frame.
    internal void RunCarried()
    {
        foreach (var carried in Game.Scene.ActiveNow<ICarried>())
        {
            if (carried is Component { Entity: { } entity } && Game.Runs(entity))
                Carry(carried, always: false);
        }
    }

    // Reads (or saves) one value; returns quietly when it keeps nothing.
    internal void Carry(ICarried carried, bool always)
    {
        var key = carried.SaveAs?.Trim();
        if (!carried.Loaded)
        {
            carried.Loaded = true;
            if (!string.IsNullOrEmpty(key))
                carried.Load(Game.SaveData, key);
            return;
        }

        if (!string.IsNullOrEmpty(key) && carried.Save(Game.SaveData, key, always))
            NoteCarriedName(key);
    }

    // Values not read yet (in `inside`, or the whole scene) read what's saved now.
    internal void LoadCarried(Entity? inside)
    {
        foreach (var carried in Game.Scene.AllNow<ICarried>())
        {
            if (!carried.Loaded && carried is Component { Entity: { } entity } && Game.Runs(entity) && (inside is null || entity.IsSelfOrDescendantOf(inside)))
                Carry(carried, always: false);
        }
    }

    // Leaving a scene: every value it carries is saved as it is (a rewind may have put one back since it last changed).
    private void FlushCarried()
    {
        foreach (var carried in Game.Scene.AllNow<ICarried>())
        {
            if (carried.Loaded && carried is Component { Entity: { } entity } && Game.Runs(entity))
                Carry(carried, always: true);
        }
    }

    // The names of the values carried between scenes.
    private string[] CarriedNames() =>
        (Game.SaveData.GetText(Game.CarriedNamesKey) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void NoteCarriedName(string name)
    {
        var known = Game.SaveData.GetText(Game.CarriedNamesKey);
        if (known is not null && known.Split('\n').Contains(name, StringComparer.Ordinal))
            return;
        Game.SaveData.Set(Game.CarriedNamesKey, string.IsNullOrEmpty(known) ? name : known + "\n" + name);
    }

    // ----- Remembered scenes -----

    // Remembers the scene being left, if it says to, as a scene file: without what it forgets, what's on its way out and the
    // minigames being played in it.
    private void RememberLeaving()
    {
        if (Game.Net.IsClient || Game.SceneName is not { Length: > 0 } name)
            return;
        RememberScene? memory = null;
        foreach (var found in Game.Scene.ActiveNow<RememberScene>())
        {
            memory = found;
            break;
        }

        if (memory is null)
            return;

        var serializer = new SceneSerializer(Game.Components ?? ComponentRegistry.CreateDefault());
        var json = serializer.SaveToString(Game.Scene);
        var forget = new HashSet<Guid>();
        var named = new List<Entity>();
        foreach (var remember in Game.Scene.ActiveNow<RememberScene>())
        {
            named.Clear();
            if (!string.IsNullOrWhiteSpace(remember.Forget))
                Game.TargetsOf(remember.Entity!, remember.Forget, named);
            foreach (var entity in named)
                forget.Add(entity.Id);
        }

        foreach (var entity in Game.Scene.EntitiesNow)
        {
            if (entity.GetComponent<Lifetime>() is not null || entity.GetComponent<MinigameRoot>() is not null || Game.IsDestroying(entity))
                forget.Add(entity.Id);
        }

        if (forget.Count > 0 || Game.PanelSystem.OpenCount > 0)
        {
            var copy = serializer.LoadFromString(Game.SaveSystem.ClosingPanels(serializer, json));
            foreach (var entity in copy.Entities.Where(e => forget.Contains(e.Id)).ToList())
            {
                if (entity.Scene == copy)
                    copy.Remove(entity);
            }

            json = serializer.SaveToString(copy);
        }

        _remembered[name] = Squeeze(json);
    }

    // The scene called `name` as it was left, or null if it isn't remembered. What it carries is read again as it comes back,
    // as the other scenes left it.
    private Scene? Recalled(string name)
    {
        if (Game.Net.IsClient || !_remembered.TryGetValue(SceneNameOf(name), out var squeezed))
            return null;
        Scene scene;
        try
        {
            scene = new SceneSerializer(Game.Components ?? ComponentRegistry.CreateDefault()).LoadFromString(Unsqueeze(squeezed));
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Game.Warn($"The scene \"{name}\" couldn't be put back as it was left, so it starts afresh: {ex.Message}");
            _remembered.Remove(SceneNameOf(name));
            return null;
        }

        foreach (var carried in scene.AllNow<ICarried>())
            carried.Loaded = false;
        return scene;
    }

    private static byte[] Squeeze(string json)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest))
            deflate.Write(Encoding.UTF8.GetBytes(json));
        return output.ToArray();
    }

    private static string Unsqueeze(byte[] squeezed)
    {
        using var input = new MemoryStream(squeezed);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(deflate, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // What a saved game keeps besides its scene: the remembered scenes, and the values carried between scenes as they are.
    internal void SaveFlow(string slot, JsonObject header)
    {
        FlushCarried();
        var carried = new JsonObject();
        foreach (var name in CarriedNames())
        {
            // A health keeps its Max beside it.
            foreach (var key in (ReadOnlySpan<string>)[name, name + ".Max"])
            {
                if (Game.SaveData.GetRaw(key) is { } value)
                    carried[key] = value;
            }
        }

        header["carried"] = carried;
        if (_remembered.Count == 0)
        {
            Game.SaveData.Delete(Game.SavedScenesKey + slot);
            return;
        }

        var scenes = new JsonObject();
        foreach (var (name, squeezed) in _remembered)
            scenes[name] = Convert.ToBase64String(squeezed);
        Game.SaveData.Set(Game.SavedScenesKey + slot, scenes.ToJsonString());
    }

    // Puts back what a saved game kept besides its scene, as it's loaded.
    internal void LoadFlow(string slot, JsonObject? header)
    {
        if (header?["carried"] is JsonObject carried)
        {
            // Values carried since it was saved weren't carried then.
            foreach (var name in CarriedNames())
            {
                if (!carried.ContainsKey(name))
                    Game.SaveData.Delete(name);
            }

            foreach (var (name, value) in carried)
                Game.SaveData.SetRaw(name, value);
        }

        _remembered.Clear();
        if (Game.SaveData.GetText(Game.SavedScenesKey + slot) is not { } text2)
            return;
        try
        {
            if (JsonNode.Parse(text2) is not JsonObject scenes)
                return;
            foreach (var (name, value) in scenes)
            {
                if (value is JsonValue v && v.TryGetValue<string>(out var packed))
                    _remembered[name] = Convert.FromBase64String(packed);
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException)
        {
            Game.Warn($"The scenes remembered in the game saved in \"{slot}\" couldn't be read: {ex.Message}");
        }
    }

    // ----- Keeping entities, and arriving -----

    // Entities with a Keep Between Scenes (and not on their way out), less those inside another of them, which go along.
    private List<Entity> KeptEntities()
    {
        var kept = new List<Entity>();
        foreach (var keep in Game.Scene.AllNow<KeepBetweenScenes>())
        {
            if (keep.Entity is { } entity && entity.Scene == Game.Scene && !Game.IsDestroying(entity))
                kept.Add(entity);
        }

        kept.RemoveAll(e => kept.Any(other => other != e && e.IsSelfOrDescendantOf(other)));
        return kept;
    }

    // A kept entity goes into the next scene top-level, in place of the very same entity there and, if it Replaces, of one of
    // the same name (taking its place in the world).
    private static void Arrive(Entity entity, Scene next)
    {
        Vector2? stood = null;
        foreach (var e in entity.SelfAndDescendants())
        {
            if (next.Find(e.Id) is { } twin && twin.Scene == next)
            {
                if (e == entity)
                    stood = Game.Flat(twin.Transform.WorldPosition);
                next.Remove(twin);
            }
        }

        if (entity.GetComponent<KeepBetweenScenes>() is { Replaces: true } && next.FindNamed(entity.Name) is { } same)
        {
            stood ??= Game.Flat(same.Transform.WorldPosition);
            next.Remove(same);
        }

        next.Add(entity);
        if (stood is { } at)
        {
            entity.Transform.Teleport(new Vector3(at, entity.Transform.Position.Z));
            if (entity.GetComponent<Body>() is { } body)
                (body.Velocity, body.Spin) = (Vector2.Zero, 0);
        }
    }

    // In the scene just gone to: the players (and whoever went, by name) stand at the spot they arrive at, and don't go
    // straight back through a door there.
    private List<Entity>? PlaceArrivals()
    {
        ArrivedAt = null;
        if (_arriving is not { } arriving)
            return null;
        _arriving = null;
        if (Game.Net.IsClient || Game.Scene.FindNamed(arriving.Spot) is not { } spot)
            return null;
        ArrivedAt = spot;
        var at = spot.ComponentList.OfType<IArrivalSpot>().FirstOrDefault()?.ArrivalPoint ?? Game.CenterOf(spot);
        var arrivers = new List<Entity>();
        foreach (var entity in Game.Scene.EntitiesNow)
        {
            if (entity == spot || !entity.IsActive)
                continue;
            var traveller = EntityNames.Is(entity, arriving.Traveller);
            if (traveller || PlayerMovementAttribute.IsOn(entity) && Game.HasFocus(entity) && !arrivers.Any(a => entity.IsSelfOrDescendantOf(a)))
                arrivers.Add(entity);
        }

        foreach (var entity in arrivers)
            Game.LevelSystem.PlaceAt(entity, at);
        return arrivers;
    }
}
