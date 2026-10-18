using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Griff.Art;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>
/// Saves and loads games in slots (with a picture of the screen as it was), with the flags they hold, and lists the
/// slots.
/// </summary>
internal sealed class SaveSystem : GameSystem
{
    // How wide a saved game's screenshot is, in pixels.
    internal const int PictureWidth = 240;

    // The slot waiting for its screenshot, and the scene it was saved in (a picture of another scene is no good).
    private string? _pictureFor;

    private Scene? _pictureOf;

    // A saved game waiting to be switched to at the end of the frame (after a fade, if any).
    private PendingLoad? _pendingLoad;

    // A saved game read back, waiting to be switched to, with the scenes it remembers (as the save data keeps them). Never
    // changed once made, so a snapshot keeps the same one.
    [NotState("read back from a save, and never changed")]
    private sealed record PendingLoad(Scene Scene, string? Name, IReadOnlyList<string> Flags, string Slot, JsonObject? Header, string? Scenes, JsonObject? Model);

    // A saved game just switched to, whose scripts' fields go back once its scripts have started: the objects they hold,
    // the scripts' systems', and each script component's.
    [NotState("a loaded game's, put back once as its scripts start")]
    private sealed record LoadedFields(string Slot, JsonObject? Model, List<(Component Script, JsonObject Fields)> Scripts);

    [NotState("a loaded game's, put back once as its scripts start")]
    private LoadedFields? _loadedFields;

    // The game's own version of its saves (see Game.SaveVersion).
    [NotState("the game's, set as it starts")]
    private int _version;

    // The saved games as last read, by slot, with their screenshots' text (to notice one change), and the save data and
    // version they were read from.
    [NotState("worked out again from the save data")]
    private readonly Dictionary<string, (SavedGame Game, string? Picture)> _slots = new(StringComparer.Ordinal);

    [NotState("worked out again from the save data")]
    private SaveData? _slotsOf;

    [NotState("worked out again from the save data")]
    private long _slotsVersion = -1;

    [NotState("takes the pictures saved with games")]
    private readonly GamePictures _pictures = new();

    /// <summary>The time a game is saved at: now, unless a test says otherwise.</summary>
    // When a game is saved, which tests set.
    [NotState("the host's clock")]
    private Func<DateTimeOffset> _clock = () => DateTimeOffset.Now;

    public override string Name => "Saves";

    public GamePictures Pictures => _pictures;

    public bool PictureWanted => _pictureFor is not null;

    internal Func<DateTimeOffset> Clock
    {
        get => _clock;
        set => _clock = value;
    }

    [field: NotState("the host's")]
    internal event Action<string>? GameSaved;

    [field: NotState("the host's")]
    internal event Action<string>? GameLoaded;

    public int Version
    {
        get => _version;
        set => _version = Math.Max(0, value);
    }

    public IReadOnlyList<SavedGame> SavedGames
    {
        get
        {
            RefreshSlots();
            return [.. _slots.Values.Select(s => s.Game).OrderBy(g => g.Slot, SlotOrder.Instance)];
        }
    }

    public SavedGame? SavedGameIn(string slot)
    {
        RefreshSlots();
        return _slots.TryGetValue(slot.Trim(), out var saved) ? saved.Game : null;
    }

    public SavedGame? LatestSave
    {
        get
        {
            RefreshSlots();
            SavedGame? latest = null;
            foreach (var (game, _) in _slots.Values)
            {
                if (latest is null || game.Saved > latest.Saved)
                    latest = game;
            }

            return latest;
        }
    }

    public bool ContinueGame(float fade = 0) => LatestSave is { } latest && LoadGame(latest.Slot, fade);

    public bool HasSave(string slot) => !string.IsNullOrWhiteSpace(slot) && Game.SaveData.Has(Game.SavedGameKey + slot.Trim());

    public bool SaveGame(string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        Crowds.ThrowIfRunning("save games");
        slot = slot.Trim();
        if (!CanSave())
            return false;

        // The scene with each script's own fields, and the objects they hold and the scripts' systems beside it.
        var registry = Game.Components ?? ComponentRegistry.CreateDefault();
        var model = new ModelWriter(Game);
        var scene = model.Finish(ClosingPanels(new SceneSerializer(registry), new SceneSerializer(registry) { Fields = model.FieldsOf }.SaveToString(Game.Scene)));
        var header = new JsonObject
        {
            ["format"] = Game.SaveFormat,
            ["version"] = _version,
            ["scene"] = Game.SceneName,
            ["saved"] = Clock().ToString("O", CultureInfo.InvariantCulture),
            ["line"] = TalkingLine(),
            ["flags"] = new JsonArray([.. FlagNames().Where(Game.HasFlag).Select(f => (JsonNode)f)]),
        };
        Game.SceneSystem.SaveFlow(slot, header);
        Game.SaveData.Set(Game.SavedSceneKey + slot, Pack(scene));
        Game.SaveData.Set(Game.SavedGameKey + slot, header.ToJsonString());
        (_pictureFor, _pictureOf) = (slot, Game.Scene);
        GameSaved?.Invoke(slot);
        return true;
    }

    public bool LoadGame(string slot, float fade = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        Crowds.ThrowIfRunning("load games");
        slot = slot.Trim();
        if (!CanSave() || Game.SaveData.GetText(Game.SavedGameKey + slot) is not { } text || Game.SaveData.GetText(Game.SavedSceneKey + slot) is not { } packed ||
            Read(slot, text, packed) is not { } load)
            return false;

        if (fade > 0)
        {
            Game.FadeThrough(fade, () => _pendingLoad = load);
        }
        else
        {
            _pendingLoad = load;
            Game.SceneSystem.CancelSceneChange();
        }

        return true;
    }

    // The game saved in `slot` read back by name (entities, components, properties), brought up to this version of the game,
    // or null, said why, when it can't be: damaged, or saved by a newer Griff. A save of another version of the game loads.
    private PendingLoad? Read(string slot, string text, string packed)
    {
        if (ParseHeader(text) is not { } header)
        {
            Game.Warn($"The game saved in \"{slot}\" can't be loaded: it's damaged (what it says about itself can't be read).");
            return null;
        }

        if (NumberIn(header, "format") is > Game.SaveFormat and var format)
        {
            Game.Warn($"The game saved in \"{slot}\" can't be loaded: it was saved by a newer version of Griff (save format {format}; this one reads up to {Game.SaveFormat}).");
            return null;
        }

        var version = NumberIn(header, "version") ?? 0;
        if (version > _version)
            Game.Warn($"The game saved in \"{slot}\" was saved by a later version of the game (save version {version}; this is {_version}): it's loaded as it is.");

        string json;
        try
        {
            json = Unpack(packed);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            Game.Warn($"The game saved in \"{slot}\" can't be loaded: it's damaged (its scene can't be read).");
            return null;
        }

        var flags = FlagsIn(header);
        var scenes = Game.SaveData.GetText(Game.SavedScenesKey + slot);
        if (version < _version)
            (json, scenes) = Upgrade(slot, version, json, scenes, flags);

        Scene scene;
        var warnings = new ProblemLog();
        try
        {
            scene = new SceneSerializer(Game.Components ?? ComponentRegistry.CreateDefault()).LoadFromString(json, warnings);
        }
        catch (Exception ex)
        {
            Game.Warn($"The game saved in \"{slot}\" can't be loaded: {ex.Message}");
            return null;
        }

        foreach (var problem in warnings.Problems)
            Game.Problems.Report(problem with { Message = $"Loading the game saved in \"{slot}\": {problem.Message}" });
        return new PendingLoad(scene, TextIn(header, "scene") ?? Game.SceneName, flags, slot, header, scenes, ModelIn(json));
    }

    // The objects scripts' fields hold and the scripts' systems, beside a saved game's scene, if it has any.
    private static JsonObject? ModelIn(string json)
    {
        if (!json.Contains($"\"{ModelReader.ObjectsKey}\"", StringComparison.Ordinal) && !json.Contains($"\"{ModelReader.SystemsKey}\"", StringComparison.Ordinal))
            return null;
        var root = JsonNode.Parse(json)!.AsObject();
        var model = new JsonObject();
        foreach (var key in (ReadOnlySpan<string>)[ModelReader.ObjectsKey, ModelReader.SystemsKey])
        {
            if (root.Remove(key, out var value))
                model[key] = value;
        }

        return model;
    }

    // The scene and the scenes it remembers, as each system that upgrades saves (ISaveUpgrader) changes them. If one fails,
    // they're read as saved.
    private (string Json, string? Scenes) Upgrade(string slot, int version, string json, string? scenes, List<string> flags)
    {
        var upgraders = Game.Systems.OfType<ISaveUpgrader>().ToList();
        if (upgraders.Count == 0)
            return (json, scenes);
        var kept = flags.ToList();
        try
        {
            var scene = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Its scene isn't a scene.");
            var remembered = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            if (scenes is not null && JsonNode.Parse(scenes) is JsonObject all)
            {
                foreach (var (name, value) in all)
                {
                    if (value is JsonValue v && v.TryGetValue<string>(out var squeezed) && JsonNode.Parse(Unpack(squeezed)) is JsonObject one)
                        remembered[name] = one;
                }
            }

            var upgrade = new SaveUpgrade(slot, version, _version, scene, remembered, flags);
            foreach (var upgrader in upgraders)
                upgrader.UpgradeSave(upgrade);
            if (remembered.Count == 0)
                return (scene.ToJsonString(), scenes);
            var packed = new JsonObject();
            foreach (var (name, one) in remembered)
                packed[name] = Pack(one.ToJsonString());
            return (scene.ToJsonString(), packed.ToJsonString());
        }
        catch (Exception ex)
        {
            Game.Warn($"The game saved in \"{slot}\" couldn't be brought up from save version {version}, so it's loaded as it was saved: {ex.Message}");
            flags.Clear();
            flags.AddRange(kept);
            return (json, scenes);
        }
    }

    // Each Autosave in the scene saves its slot as the scene starts and every so many minutes of play after (game time, so
    // not while paused), unless its setting is off. Not in a network game, nor while the scene is on its way out.
    internal void RunAutosaves()
    {
        if (Game.Network.IsOnline || _pendingLoad is not null || Game.IsChangingScene)
            return;
        foreach (var autosave in Game.Scene.ActiveNow<Autosave>())
        {
            var due = autosave.Tick(Time.Delta);
            if (due && IsOn(autosave) && !string.IsNullOrWhiteSpace(autosave.Slot))
                SaveGame(autosave.Slot);
        }
    }

    // As the game closes, each Autosave set to saves its slot.
    internal void AutosaveOnQuit()
    {
        if (Game.Network.IsOnline)
            return;
        foreach (var autosave in Game.Scene.ActiveNow<Autosave>())
        {
            if (autosave.OnQuit && IsOn(autosave) && !string.IsNullOrWhiteSpace(autosave.Slot))
                SaveGame(autosave.Slot);
        }
    }

    // Its setting (the player's switch) isn't false.
    private bool IsOn(Autosave autosave) =>
        string.IsNullOrWhiteSpace(autosave.Setting) || Game.SaveData.GetBool(autosave.Setting.Trim(), true);

    public bool DeleteSave(string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        slot = slot.Trim();
        var had = Game.SaveData.Delete(Game.SavedGameKey + slot);
        Game.SaveData.Delete(Game.SavedSceneKey + slot);
        Game.SaveData.Delete(Game.SavedScenesKey + slot);
        return had;
    }

    public bool QuickSave() => SaveGame(Game.QuickSlot);

    public bool QuickLoad(float fade = 0) => LoadGame(Game.QuickSlot, fade);

    public void TakePicture(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        var (slot, scene) = (_pictureFor, _pictureOf);
        (_pictureFor, _pictureOf) = (null, null);
        if (slot is null || scene != Game.Scene || width <= 0 || height <= 0 || rgba.Length < width * height * 4 ||
            Game.SaveData.GetText(Game.SavedGameKey + slot) is not { } text || ParseHeader(text) is not { } header)
            return;
        var (small, w, h) = Shrink(rgba, width, height, PictureWidth);
        header["picture"] = Convert.ToBase64String(Png.Encode(small, w, h));
        Game.SaveData.Set(Game.SavedGameKey + slot, header.ToJsonString());
    }

    // Saving and loading are this machine's own, so not in a network game.
    private bool CanSave()
    {
        if (!Game.Network.IsOnline)
            return true;
        Game.Warn("Games can't be saved or loaded during a network game.");
        return false;
    }

    // The saved games' slots in the save data, read again (and their screenshots made pictures) when it changes.
    private void RefreshSlots()
    {
        var data = Game.SaveData;
        if (data == _slotsOf && data.Version == _slotsVersion)
            return;
        (_slotsOf, _slotsVersion) = (data, data.Version);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in data.Keys)
        {
            if (!key.StartsWith(Game.SavedGameKey, StringComparison.Ordinal) || key.Length == Game.SavedGameKey.Length ||
                data.GetText(key) is not { } text || ParseHeader(text) is not { } header)
                continue;
            var slot = key[Game.SavedGameKey.Length..];
            seen.Add(slot);
            var picture = TextIn(header, "picture");
            var name = $"{GamePictures.Mark}save/{slot}";
            var saved = DateTimeOffset.TryParse(TextIn(header, "saved"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)
                ? when
                : DateTimeOffset.MinValue;
            var game = new SavedGame(slot, saved, TextIn(header, "scene"), TextIn(header, "line") ?? "")
            {
                Picture = picture is null ? null : name,
                Version = NumberIn(header, "version") ?? 0,
            };
            var bytes = _slots.TryGetValue(slot, out var before) && before.Picture == picture ? Pictures.Get(name).Png : Picture(picture);
            if (before.Picture != picture)
                Pictures.Set(name, bytes);
            _slots[slot] = (game with { PictureAspect = AspectOf(bytes) }, picture);
        }

        foreach (var gone in _slots.Keys.Where(s => !seen.Contains(s)).ToList())
        {
            _slots.Remove(gone);
            Pictures.Set($"{GamePictures.Mark}save/{gone}", null);
        }

        // A PNG's width over its height, from its header.
        static float AspectOf(byte[]? png) =>
            png is { Length: >= 24 } && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)) is > 0 and var height
                ? System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)) / (float)height
                : 0;

        static byte[]? Picture(string? text)
        {
            try
            {
                return text is null ? null : Convert.FromBase64String(text);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }

    // The scene as saved, with the panels open now (the save screen it's saved from) closed in it.
    internal string ClosingPanels(SceneSerializer serializer, string json)
    {
        if (Game.PanelSystem.OpenCount == 0)
            return json;
        var open = Game.OpenPanels.Select(p => p.Entity?.Id).OfType<Guid>().ToHashSet();
        var copy = serializer.LoadFromString(json);
        foreach (var entity in copy.Entities.Where(e => open.Contains(e.Id)))
        {
            foreach (var panel in entity.GetComponents<Panel>())
                panel.Open = false;
            entity.IsEnabled = false;
        }

        return serializer.SaveToString(copy);
    }

    // "Ava: What a lovely day!" for the line showing, or nothing.
    private string TalkingLine()
    {
        if (Game.Talking is not { Said: { Length: > 0 } said } talking)
            return "";
        return talking.Speaker is { } speaker && speaker.ShownName.Length > 0 ? $"{speaker.ShownName}: {said}" : said;
    }

    private static JsonObject? ParseHeader(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static List<string> FlagsIn(JsonObject header) =>
        header["flags"] is JsonArray flags ? [.. flags.Select(f => f is JsonValue v && v.TryGetValue<string>(out var flag) ? flag : null).OfType<string>()] : [];

    // A header's text or whole number, or null when it has none (or something else there, in a damaged one).
    private static string? TextIn(JsonObject header, string key) =>
        header[key] is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;

    private static int? NumberIn(JsonObject header, string key) =>
        header[key] is JsonValue v && v.TryGetValue<int>(out var number) ? number : null;

    // The flags a saved game had set, set again, and every other flag the game knows of cleared.
    private void PutFlagsBack(IReadOnlyList<string> flags)
    {
        var set = new HashSet<string>(flags, StringComparer.OrdinalIgnoreCase);
        foreach (var name in FlagNames().Concat(flags).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            Game.SetFlag(name, set.Contains(name));
    }

    // The names of the flags the game has set (or cleared) through SetFlag.
    private string[] FlagNames() =>
        (Game.SaveData.GetText(Game.FlagNamesKey) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Remembers a flag's name, so a saved game knows to put it back.
    internal void NoteFlagName(string name)
    {
        var known = FlagNames().ToList();
        if (known.Contains(name, StringComparer.OrdinalIgnoreCase))
            return;
        known.Add(name);
        Game.SaveData.Set(Game.FlagNamesKey, string.Join('\n', known));
    }

    // Switches to a saved game's scene, its flags put back first. True if there was one waiting.
    internal bool SwitchToLoadedGame()
    {
        if (_pendingLoad is not { } load)
            return false;
        _pendingLoad = null;
        Game.SceneSystem.CancelSceneChange();
        PutFlagsBack(load.Flags);
        Game.SceneSystem.SwitchScene(load.Name ?? Game.SceneName ?? "", load.Scene);
        Game.SceneSystem.LoadFlow(load.Slot, load.Header, load.Scenes);
        _loadedFields = new LoadedFields(load.Slot, load.Model, ScriptFieldsIn(load.Scene));
        // A game just loaded is as saved: its autosaves wait for their time rather than saving it again as it starts.
        foreach (var autosave in Game.Scene.AllNow<Autosave>())
            autosave.Arrive();
        GameLoaded?.Invoke(load.Slot);
        return true;
    }

    // Each component's saved fields in a loaded scene, taken from what it kept of its saved data (so they're not saved again
    // as they were).
    private static List<(Component, JsonObject)> ScriptFieldsIn(Scene scene)
    {
        var scripts = new List<(Component, JsonObject)>();
        foreach (var entity in scene.Entities)
        {
            foreach (var component in entity.Components)
            {
                if (component is MissingComponent || component.Kept is not { } kept || !kept.Any(k => k.Key == SavedData.FieldsKey))
                    continue;
                var fields = kept.First(k => k.Key == SavedData.FieldsKey).Value;
                var rest = kept.Where(k => k.Key != SavedData.FieldsKey).ToList();
                component.Kept = rest.Count > 0 ? rest : null;
                if (fields.ValueKind == System.Text.Json.JsonValueKind.Object)
                    scripts.Add((component, JsonObject.Create(fields)!));
            }
        }

        return scripts;
    }

    // Once a loaded game's scripts have started (so what Start sets up is there), every script's saved fields go back over
    // what Start made: the scripts' systems', then each script's (one that hasn't started yet, on a disabled entity, as it
    // starts). Then each script and system hears OnLoaded, to start again what couldn't be saved.
    internal void PutBackLoadedGame()
    {
        if (_loadedFields is not { } loaded)
            return;
        _loadedFields = null;
        var reader = new ModelReader(Game, loaded.Model?[ModelReader.ObjectsKey] as JsonArray, loaded.Slot);
        var systems = loaded.Model?[ModelReader.SystemsKey] as JsonArray;
        var running = Game.Systems.Where(s => !s.IsRemoved).ToList();
        foreach (var system in running)
        {
            var type = system.GetType();
            if (SavePlan.IsScript(type) && systems?.OfType<JsonObject>().FirstOrDefault(s => SavePlan.Matches(type, s["type"]?.GetValue<string>())) is { } saved &&
                saved["fields"] is JsonObject fields)
                reader.Read(system, SavePlan.OfScript(type, null), fields, type.Name)();
        }

        foreach (var (script, fields) in loaded.Scripts)
        {
            if (script.Entity?.Scene != Game.Scene || !SavePlan.IsScript(script.GetType()))
                continue;
            var putBack = reader.Read(script, SavePlan.OfScript(script.GetType(), Game.Components), fields, script.GetType().Name);
            if (script is Behavior { IsStarted: false } waiting)
                waiting.FieldsToPutBack = putBack;
            else
                putBack();
        }

        foreach (var system in running.Where(s => !s.IsRemoved))
            system.RunLoaded();
        foreach (var script in Game.Scene.AllNow<Behavior>())
        {
            if (script.IsStarted)
                script.RunLoaded();
            else
                script.FieldsToPutBack ??= static () => { };
        }
    }

    // A scene's JSON, squeezed (it's mostly the same words over and over) and kept as text.
    private static string Pack(string json)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal))
            deflate.Write(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(output.ToArray());
    }

    private static string Unpack(string packed)
    {
        using var input = new MemoryStream(Convert.FromBase64String(packed));
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(deflate, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // The picture shrunk to `width` pixels wide (its shape kept), each pixel the average of those it covers.
    private static (byte[] Rgba, int Width, int Height) Shrink(byte[] rgba, int width, int height, int most)
    {
        if (width <= most)
            return (rgba, width, height);
        var w = most;
        var h = Math.Max(1, (int)MathF.Round(height * (float)most / width));
        var small = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var (top, bottom) = (y * height / h, Math.Max(y * height / h + 1, (y + 1) * height / h));
            for (var x = 0; x < w; x++)
            {
                var (left, right) = (x * width / w, Math.Max(x * width / w + 1, (x + 1) * width / w));
                int r = 0, g = 0, b = 0, a = 0, count = 0;
                for (var sy = top; sy < bottom; sy++)
                {
                    for (var sx = left; sx < right; sx++)
                    {
                        var i = (sy * width + sx) * 4;
                        (r, g, b, a) = (r + rgba[i], g + rgba[i + 1], b + rgba[i + 2], a + rgba[i + 3]);
                        count++;
                    }
                }

                var o = (y * w + x) * 4;
                (small[o], small[o + 1], small[o + 2], small[o + 3]) = ((byte)(r / count), (byte)(g / count), (byte)(b / count), (byte)255);
            }
        }

        return (small, w, h);
    }

    // Slot names with numbers in them in the order of the numbers: "2" before "10".
    private sealed class SlotOrder : IComparer<string>
    {
        public static readonly SlotOrder Instance = new();

        public int Compare(string? a, string? b)
        {
            var (na, nb) = (int.TryParse(a, out var x), int.TryParse(b, out var y));
            if (na && nb)
                return x.CompareTo(y);
            if (na != nb)
                return na ? -1 : 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
