using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Griff.Art;
using Griff.Rendering;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>
/// A game saved in a slot (see <see cref="Game.SaveGame"/>): when it was saved, the scene it was in and the line being
/// said then, and its screenshot.
/// </summary>
public sealed record SavedGame(string Slot, DateTimeOffset Saved, string? SceneName, string Line)
{
    /// <summary>
    /// The name its screenshot is drawn by, as an image's path ("@save/1"; see <see cref="GamePictures"/>), or null
    /// before it has one.
    /// </summary>
    public string? Picture { get; init; }

    /// <summary>Its screenshot's width divided by its height (the screen's shape when it was saved), or 0 with none.</summary>
    public float PictureAspect { get; init; }

    /// <summary>
    /// <paramref name="format"/> filled in: "{slot}" is the slot's name, "{date}" the day it was saved ("15 Oct 2026"),
    /// "{time}" the time ("14:03"), "{scene}" the scene's name and "{line}" the line being said (or the scene's name when
    /// nobody was talking).
    /// </summary>
    public string Describe(string format) => SaveSlot.Fill(format, Slot, Saved, SceneName, Line);
}

// Saved games: the scene as it is (the stage, where a talk has got to, everything), the flags and a screenshot, kept in a
// slot of the save data, to load again later exactly as it was.
public sealed partial class Game
{
    /// <summary>The slot <see cref="QuickSave"/> and <see cref="QuickLoad"/> use.</summary>
    public const string QuickSlot = "Quick";

    /// <summary>
    /// Where each saved game is kept in <see cref="SaveData"/>: this, then the slot's name ("griff.save.1"), holds when it was
    /// saved, the line, the flags and the screenshot; <see cref="SavedSceneKey"/> holds the scene.
    /// </summary>
    public const string SavedGameKey = "griff.save.";

    /// <summary>Where each saved game's scene is kept in <see cref="SaveData"/>: this, then the slot's name.</summary>
    public const string SavedSceneKey = "griff.scene.";

    /// <summary>
    /// Where the names of the flags the game has set are kept in <see cref="SaveData"/> (one a line), so a saved game knows
    /// which to put back.
    /// </summary>
    public const string FlagNamesKey = "griff.flags";

    // How wide a saved game's screenshot is, in pixels.
    internal const int PictureWidth = 240;

    // The slot waiting for its screenshot, and the scene it was saved in (a picture of another scene is no good).
    private string? _pictureFor;
    private Scene? _pictureOf;

    // A saved game waiting to be switched to at the end of the frame (after a fade, if any).
    private (Scene Scene, string? Name, IReadOnlyList<string> Flags, string Slot, JsonObject? Header)? _pendingLoad;

    // The saved games as last read, by slot, with their screenshots' text (to notice one change), and the save data and
    // version they were read from.
    private readonly Dictionary<string, (SavedGame Game, string? Picture)> _slots = new(StringComparer.Ordinal);
    private SaveData? _slotsOf;
    private long _slotsVersion = -1;

    /// <summary>The pictures the game makes for itself: its saved games' screenshots (see <see cref="GamePictures"/>).</summary>
    public GamePictures Pictures { get; } = new();

    /// <summary>
    /// True while a game just saved waits for its screenshot: the host that draws the game then draws it once more without
    /// the panels open over it (<see cref="SceneRenderer.BuildPicture"/>) and hands the pixels to <see cref="TakePicture"/>.
    /// </summary>
    public bool PictureWanted => _pictureFor is not null;

    /// <summary>The time a game is saved at: now, unless a test says otherwise.</summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.Now;

    /// <summary>Raised when a game has been saved, with its slot.</summary>
    public event Action<string>? GameSaved;

    /// <summary>Raised when a saved game has been loaded (its scene is the one running now), with its slot.</summary>
    public event Action<string>? GameLoaded;

    /// <summary>Every saved game, by slot name (numbers in order: 2 before 10).</summary>
    public IReadOnlyList<SavedGame> SavedGames
    {
        get
        {
            RefreshSlots();
            return [.. _slots.Values.Select(s => s.Game).OrderBy(g => g.Slot, SlotOrder.Instance)];
        }
    }

    /// <summary>The game saved in <paramref name="slot"/>, or null when there's none.</summary>
    public SavedGame? SavedGameIn(string slot)
    {
        RefreshSlots();
        return _slots.TryGetValue(slot.Trim(), out var saved) ? saved.Game : null;
    }

    /// <summary>
    /// The game saved most recently, in any slot (quick saves too), or null when none is: what a title screen's Continue
    /// loads (see <see cref="ContinueGame"/>).
    /// </summary>
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

    /// <summary>
    /// Loads the game saved most recently (<see cref="LatestSave"/>), as <see cref="LoadGame"/> does: a title screen's
    /// Continue. True if there was one, and it's on its way.
    /// </summary>
    public bool ContinueGame(float fade = 0) => LatestSave is { } latest && LoadGame(latest.Slot, fade);

    /// <summary>True if a game is saved in <paramref name="slot"/>.</summary>
    public bool HasSave(string slot) => !string.IsNullOrWhiteSpace(slot) && SaveData.Has(SavedGameKey + slot.Trim());

    /// <summary>
    /// Saves the game in <paramref name="slot"/> (a name of your own: "1", "Quick", "Chapter 2"), replacing what was
    /// there: the scene as it is now (the stage, where a talk has got to, everything a rewind keeps), with the panels open
    /// over it closed, and the flags set now. Loading it (<see cref="LoadGame"/>) puts all of it back. Kept in
    /// <see cref="SaveData"/>, so it's there next time (in a browser, in its storage), with a screenshot taken as the game
    /// is next drawn. Not in a network game, which isn't this machine's alone. True if it was saved.
    /// </summary>
    public bool SaveGame(string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        Crowds.ThrowIfRunning("save games");
        slot = slot.Trim();
        if (!CanSave())
            return false;

        var serializer = new SceneSerializer(Components ?? ComponentRegistry.CreateDefault());
        var scene = ClosingPanels(serializer, serializer.SaveToString(Scene));
        var header = new JsonObject
        {
            ["scene"] = SceneName,
            ["saved"] = Clock().ToString("O", CultureInfo.InvariantCulture),
            ["line"] = TalkingLine(),
            ["flags"] = new JsonArray([.. FlagNames().Where(HasFlag).Select(f => (JsonNode)f)]),
        };
        SaveFlow(slot, header);
        SaveData.Set(SavedSceneKey + slot, Pack(scene));
        SaveData.Set(SavedGameKey + slot, header.ToJsonString());
        (_pictureFor, _pictureOf) = (slot, Scene);
        GameSaved?.Invoke(slot);
        return true;
    }

    /// <summary>
    /// Loads the game saved in <paramref name="slot"/> at the end of this frame (after fading out over
    /// <paramref name="fade"/> seconds, if more than 0, and back in as long): its scene as it was saved, the stage, the talk
    /// going on from the line it was on, and the flags as they were. Nothing happens if the slot is empty. Not in a network
    /// game. True if it's on its way.
    /// </summary>
    public bool LoadGame(string slot, float fade = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        Crowds.ThrowIfRunning("load games");
        slot = slot.Trim();
        if (!CanSave() || SaveData.GetText(SavedGameKey + slot) is not { } text || SaveData.GetText(SavedSceneKey + slot) is not { } packed)
            return false;

        Scene scene;
        var warnings = new List<string>();
        try
        {
            scene = new SceneSerializer(Components ?? ComponentRegistry.CreateDefault()).LoadFromString(Unpack(packed), warnings);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Warn($"The game saved in \"{slot}\" can't be loaded: {ex.Message}");
            return false;
        }

        foreach (var warning in warnings)
            Warn($"Loading the game saved in \"{slot}\": {warning}");
        var header = ParseHeader(text);
        var load = (scene, header?["scene"]?.GetValue<string>() ?? SceneName, (IReadOnlyList<string>)FlagsIn(header), slot, header);
        if (fade > 0)
        {
            FadeThrough(fade, () => _pendingLoad = load);
        }
        else
        {
            _pendingLoad = load;
            _pendingScene = null;
        }

        return true;
    }

    /// <summary>Forgets the game saved in <paramref name="slot"/>. True if there was one.</summary>
    public bool DeleteSave(string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        slot = slot.Trim();
        var had = SaveData.Delete(SavedGameKey + slot);
        SaveData.Delete(SavedSceneKey + slot);
        SaveData.Delete(SavedScenesKey + slot);
        return had;
    }

    /// <summary>Saves the game in the <see cref="QuickSlot"/>, as <see cref="SaveGame"/> does.</summary>
    public bool QuickSave() => SaveGame(QuickSlot);

    /// <summary>Loads the game saved in the <see cref="QuickSlot"/>, as <see cref="LoadGame"/> does.</summary>
    public bool QuickLoad(float fade = 0) => LoadGame(QuickSlot, fade);

    /// <summary>
    /// Hands over the pixels of the picture <see cref="PictureWanted"/> asked for (top-down RGBA rows,
    /// <paramref name="width"/> by <paramref name="height"/>): they're shrunk and kept as the saved game's screenshot.
    /// </summary>
    public void TakePicture(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        var (slot, scene) = (_pictureFor, _pictureOf);
        (_pictureFor, _pictureOf) = (null, null);
        if (slot is null || scene != Scene || width <= 0 || height <= 0 || rgba.Length < width * height * 4 ||
            SaveData.GetText(SavedGameKey + slot) is not { } text || ParseHeader(text) is not { } header)
            return;
        var (small, w, h) = Shrink(rgba, width, height, PictureWidth);
        header["picture"] = Convert.ToBase64String(Png.Encode(small, w, h));
        SaveData.Set(SavedGameKey + slot, header.ToJsonString());
    }

    // Saving and loading are this machine's own, so not in a network game.
    private bool CanSave()
    {
        if (!Net.IsOnline)
            return true;
        Warn("Games can't be saved or loaded during a network game.");
        return false;
    }

    // The saved games' slots in the save data, read again (and their screenshots made pictures) when it changes.
    private void RefreshSlots()
    {
        var data = SaveData;
        if (data == _slotsOf && data.Version == _slotsVersion)
            return;
        (_slotsOf, _slotsVersion) = (data, data.Version);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in data.Keys)
        {
            if (!key.StartsWith(SavedGameKey, StringComparison.Ordinal) || key.Length == SavedGameKey.Length ||
                data.GetText(key) is not { } text || ParseHeader(text) is not { } header)
                continue;
            var slot = key[SavedGameKey.Length..];
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
    private string ClosingPanels(SceneSerializer serializer, string json)
    {
        if (_panels.Open.Count == 0)
            return json;
        var open = _panels.Open.Select(p => p.Panel.Entity?.Id).OfType<Guid>().ToHashSet();
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
        if (Talking is not { Said: { Length: > 0 } said } talking)
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
            SetFlag(name, set.Contains(name));
    }

    // The names of the flags the game has set (or cleared) through SetFlag.
    private string[] FlagNames() =>
        (SaveData.GetText(FlagNamesKey) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Remembers a flag's name, so a saved game knows to put it back.
    private void NoteFlagName(string name)
    {
        var known = FlagNames().ToList();
        if (known.Contains(name, StringComparer.OrdinalIgnoreCase))
            return;
        known.Add(name);
        SaveData.Set(FlagNamesKey, string.Join('\n', known));
    }

    // Switches to a saved game's scene, its flags put back first. True if there was one waiting.
    private bool SwitchToLoadedGame()
    {
        if (_pendingLoad is not { } load)
            return false;
        _pendingLoad = null;
        _pendingScene = null;
        PutFlagsBack(load.Flags);
        SwitchScene(load.Name ?? SceneName ?? "", load.Scene);
        LoadFlow(load.Slot, load.Header);
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
    internal static (byte[] Rgba, int Width, int Height) Shrink(byte[] rgba, int width, int height, int most)
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
