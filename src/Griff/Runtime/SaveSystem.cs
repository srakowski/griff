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

    // A saved game read back, waiting to be switched to. Never changed once made, so a snapshot keeps the same one.
    [NotState("read back from a save, and never changed")]
    private sealed record PendingLoad(Scene Scene, string? Name, IReadOnlyList<string> Flags, string Slot, JsonObject? Header);

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

        var serializer = new SceneSerializer(Game.Components ?? ComponentRegistry.CreateDefault());
        var scene = ClosingPanels(serializer, serializer.SaveToString(Game.Scene));
        var header = new JsonObject
        {
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
        if (!CanSave() || Game.SaveData.GetText(Game.SavedGameKey + slot) is not { } text || Game.SaveData.GetText(Game.SavedSceneKey + slot) is not { } packed)
            return false;

        Scene scene;
        var warnings = new List<string>();
        try
        {
            scene = new SceneSerializer(Game.Components ?? ComponentRegistry.CreateDefault()).LoadFromString(Unpack(packed), warnings);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Game.Warn($"The game saved in \"{slot}\" can't be loaded: {ex.Message}");
            return false;
        }

        foreach (var warning in warnings)
            Game.Warn($"Loading the game saved in \"{slot}\": {warning}");
        var header = ParseHeader(text);
        var load = new PendingLoad(scene, header?["scene"]?.GetValue<string>() ?? Game.SceneName, FlagsIn(header), slot, header);
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
        if (!Game.Net.IsOnline)
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
            var picture = header["picture"]?.GetValue<string>();
            var name = $"{GamePictures.Mark}save/{slot}";
            var saved = DateTimeOffset.TryParse(header["saved"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)
                ? when
                : DateTimeOffset.MinValue;
            var game = new SavedGame(slot, saved, header["scene"]?.GetValue<string>(), header["line"]?.GetValue<string>() ?? "")
            {
                Picture = picture is null ? null : name,
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

    private static List<string> FlagsIn(JsonObject? header) =>
        header?["flags"] is JsonArray flags ? [.. flags.Select(f => f?.GetValue<string>()).OfType<string>()] : [];

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
        Game.SceneSystem.LoadFlow(load.Slot, load.Header);
        GameLoaded?.Invoke(load.Slot);
        return true;
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
