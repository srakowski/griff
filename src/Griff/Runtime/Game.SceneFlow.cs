using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

// Going between scenes: what's carried (kept entities, and the values kept under a Save As), scenes that remember how they
// were left, and where the players arrive in the next scene.
public sealed partial class Game
{
    /// <summary>
    /// Where the names of the values carried between scenes are kept in <see cref="SaveData"/> (one a line): what an
    /// inventory's, a health's or a counter's Save As keeps, so a saved game knows to put them back as they were.
    /// </summary>
    public const string CarriedNamesKey = "griff.carried";

    /// <summary>Where each saved game's remembered scenes are kept in <see cref="SaveData"/>: this, then the slot's name.</summary>
    public const string SavedScenesKey = "griff.remembered.";

    // The scenes remembered as they were left, by name: each the scene's file, squeezed (see RememberScene).
    private readonly Dictionary<string, byte[]> _remembered = new(StringComparer.OrdinalIgnoreCase);

    // Where the players arrive in the scene being gone to, and the name of whoever went (a door's user), until it's there.
    private (string Spot, string? Traveller)? _arriving;

    /// <summary>
    /// The scenes the game remembers as they were left (see <see cref="RememberScene"/>): going back to one finds it so,
    /// until it's forgotten.
    /// </summary>
    public IReadOnlyCollection<string> RememberedScenes => [.. _remembered.Keys];

    /// <summary>True if the game remembers how <paramref name="scene"/> was left (see <see cref="RememberScene"/>).</summary>
    public bool Remembers(string scene) => _remembered.ContainsKey(SceneNameOf(scene));

    /// <summary>
    /// Forgets how <paramref name="scene"/> was left, so going there next starts it as its file has it (the dungeon reset
    /// once the boss is beaten). True if it was remembered.
    /// </summary>
    public bool ForgetScene(string scene)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scene);
        return _remembered.Remove(SceneNameOf(scene.Trim()));
    }

    void IGameContext.LoadScene(string scene, float fade) => LoadScene(scene, fade);

    void IGameContext.ForgetScene(string scene) => ForgetScene(scene);

    /// <summary>Forgets every scene remembered, as New Game does.</summary>
    public void ForgetScenes() => _remembered.Clear();

    /// <summary>
    /// Starts the game over, as a New Game button does: forgets the progress in the save data (flags, carried inventories,
    /// health and counts, best scores; not the player's settings and controls, saved games or lines read) and every scene
    /// remembered, then goes to <paramref name="scene"/> as its file has it.
    /// </summary>
    public void NewGame(string scene, float fade = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scene);
        SaveData.ClearProgress();
        ForgetScenes();
        LoadScene(scene.Trim(), fade, null, arriveAt: null, traveller: null, fresh: true);
        _startingOver = true;
    }

    // New Game is under way: what the scene being left carries isn't saved again on the way out.
    private bool _startingOver;

    /// <summary>
    /// The entity the players arrived at in this scene (a door's or hotspot's Arrive At, or <see cref="LoadScene(string, float, Color?, string?)"/>'s
    /// <c>arriveAt</c>), or null if they didn't arrive anywhere in particular.
    /// </summary>
    public Entity? ArrivedAt { get; private set; }

    // ----- Carried values -----

    // Each value with a Save As takes what's saved (once, as it's first seen), and saves what it has as that changes,
    // where the game is run. Read before anything else in the step, so a HUD shows it from the first frame.
    private void RunCarried()
    {
        foreach (var carried in Scene.ActiveNow<ICarried>())
        {
            if (carried is Component { Entity: { } entity } && Runs(entity))
                Carry(carried, always: false);
        }
    }

    // Reads (or saves) one value; returns quietly when it keeps nothing.
    private void Carry(ICarried carried, bool always)
    {
        var key = carried.SaveAs?.Trim();
        if (!carried.Loaded)
        {
            carried.Loaded = true;
            if (!string.IsNullOrEmpty(key))
                carried.Load(SaveData, key);
            return;
        }

        if (!string.IsNullOrEmpty(key) && carried.Save(SaveData, key, always))
            NoteCarriedName(key);
    }

    // Values not read yet (in `inside`, or the whole scene) read what's saved now.
    private void LoadCarried(Entity? inside)
    {
        foreach (var carried in Scene.AllNow<ICarried>())
        {
            if (!carried.Loaded && carried is Component { Entity: { } entity } && Runs(entity) && (inside is null || entity.IsSelfOrDescendantOf(inside)))
                Carry(carried, always: false);
        }
    }

    // Leaving a scene: every value it carries is saved as it is (a rewind may have put one back since it last changed).
    private void FlushCarried()
    {
        foreach (var carried in Scene.AllNow<ICarried>())
        {
            if (carried.Loaded && carried is Component { Entity: { } entity } && Runs(entity))
                Carry(carried, always: true);
        }
    }

    // The names of the values carried between scenes.
    private string[] CarriedNames() =>
        (SaveData.GetText(CarriedNamesKey) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void NoteCarriedName(string name)
    {
        var known = SaveData.GetText(CarriedNamesKey);
        if (known is not null && known.Split('\n').Contains(name, StringComparer.Ordinal))
            return;
        SaveData.Set(CarriedNamesKey, string.IsNullOrEmpty(known) ? name : known + "\n" + name);
    }

    // ----- Remembered scenes -----

    // Remembers the scene being left, if it says to, as a scene file: without what it forgets, what's on its way out and the
    // minigames being played in it.
    private void RememberLeaving()
    {
        if (Net.IsClient || SceneName is not { Length: > 0 } name)
            return;
        RememberScene? memory = null;
        foreach (var found in Scene.ActiveNow<RememberScene>())
        {
            memory = found;
            break;
        }

        if (memory is null)
            return;

        var serializer = new SceneSerializer(Components ?? ComponentRegistry.CreateDefault());
        var json = serializer.SaveToString(Scene);
        var forget = new HashSet<Guid>();
        var named = new List<Entity>();
        foreach (var remember in Scene.ActiveNow<RememberScene>())
        {
            named.Clear();
            if (!string.IsNullOrWhiteSpace(remember.Forget))
                TargetsOf(remember.Entity!, remember.Forget, named);
            foreach (var entity in named)
                forget.Add(entity.Id);
        }

        foreach (var entity in Scene.EntitiesNow)
        {
            if (entity.GetComponent<Lifetime>() is not null || entity.GetComponent<MinigameRoot>() is not null || _pendingDestroy.Contains(entity))
                forget.Add(entity.Id);
        }

        if (forget.Count > 0 || _panels.Open.Count > 0)
        {
            var copy = serializer.LoadFromString(ClosingPanels(serializer, json));
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
        if (Net.IsClient || !_remembered.TryGetValue(SceneNameOf(name), out var squeezed))
            return null;
        Scene scene;
        try
        {
            scene = new SceneSerializer(Components ?? ComponentRegistry.CreateDefault()).LoadFromString(Unsqueeze(squeezed));
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Warn($"The scene \"{name}\" couldn't be put back as it was left, so it starts afresh: {ex.Message}");
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
    private void SaveFlow(string slot, JsonObject header)
    {
        FlushCarried();
        var carried = new JsonObject();
        foreach (var name in CarriedNames())
        {
            // A health keeps its Max beside it.
            foreach (var key in (ReadOnlySpan<string>)[name, name + ".Max"])
            {
                if (SaveData.GetRaw(key) is { } value)
                    carried[key] = value;
            }
        }

        header["carried"] = carried;
        if (_remembered.Count == 0)
        {
            SaveData.Delete(SavedScenesKey + slot);
            return;
        }

        var scenes = new JsonObject();
        foreach (var (name, squeezed) in _remembered)
            scenes[name] = Convert.ToBase64String(squeezed);
        SaveData.Set(SavedScenesKey + slot, scenes.ToJsonString());
    }

    // Puts back what a saved game kept besides its scene, as it's loaded.
    private void LoadFlow(string slot, JsonObject? header)
    {
        if (header?["carried"] is JsonObject carried)
        {
            // Values carried since it was saved weren't carried then.
            foreach (var name in CarriedNames())
            {
                if (!carried.ContainsKey(name))
                    SaveData.Delete(name);
            }

            foreach (var (name, value) in carried)
                SaveData.SetRaw(name, value);
        }

        _remembered.Clear();
        if (SaveData.GetText(SavedScenesKey + slot) is not { } text2)
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
            Warn($"The scenes remembered in the game saved in \"{slot}\" couldn't be read: {ex.Message}");
        }
    }

    // ----- Keeping entities, and arriving -----

    // Entities with a Keep Between Scenes (and not on their way out), less those inside another of them, which go along.
    private List<Entity> KeptEntities()
    {
        var kept = new List<Entity>();
        foreach (var keep in Scene.AllNow<KeepBetweenScenes>())
        {
            if (keep.Entity is { } entity && entity.Scene == Scene && !_pendingDestroy.Contains(entity))
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
                    stood = Flat(twin.Transform.WorldPosition);
                next.Remove(twin);
            }
        }

        if (entity.GetComponent<KeepBetweenScenes>() is { Replaces: true } && next.FindNamed(entity.Name) is { } same)
        {
            stood ??= Flat(same.Transform.WorldPosition);
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
        if (Net.IsClient || Scene.FindNamed(arriving.Spot) is not { } spot)
            return null;
        ArrivedAt = spot;
        var at = spot.ComponentList.OfType<IArrivalSpot>().FirstOrDefault()?.ArrivalPoint ?? CenterOf(spot);
        var arrivers = new List<Entity>();
        foreach (var entity in Scene.EntitiesNow)
        {
            if (entity == spot || !entity.IsActive)
                continue;
            var traveller = arriving.Traveller is { } name && entity.Name == name;
            if (traveller || PlayerMovementAttribute.IsOn(entity) && HasFocus(entity) && !arrivers.Any(a => entity.IsSelfOrDescendantOf(a)))
                arrivers.Add(entity);
        }

        foreach (var entity in arrivers)
            LevelSystem.PlaceAt(entity, at);
        return arrivers;
    }
}

