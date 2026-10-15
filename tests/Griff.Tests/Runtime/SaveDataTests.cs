using Griff.Runtime;

namespace Griff.Tests.Runtime;

public sealed class SaveDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-save-" + Guid.NewGuid().ToString("N"));

    private string SavePath => Path.Combine(_root, "nested", "save.json");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Values_SurviveAReload()
    {
        var save = new SaveData(SavePath);
        save.Set("best", 42);
        save.Set("volume", 0.5f);
        save.Set("muted", true);
        save.Set("name", "Ada");

        var reloaded = new SaveData(SavePath);

        Assert.Equal(42, reloaded.GetInt("best"));
        Assert.Equal(0.5f, reloaded.GetFloat("volume"));
        Assert.True(reloaded.GetBool("muted"));
        Assert.Equal("Ada", reloaded.GetString("name"));
        Assert.Equal(["best", "volume", "muted", "name"], reloaded.Keys);
    }

    [Fact]
    public void MissingOrMismatchedValues_ReadAsTheFallback()
    {
        var save = new SaveData();
        save.Set("name", "Ada");

        Assert.Equal(7, save.GetInt("missing", 7));
        Assert.Equal(3, save.GetInt("name", 3));
        Assert.Null(save.GetString("missing"));
    }

    [Fact]
    public void IntsReadAsFloats_BeforeAndAfterReload()
    {
        var save = new SaveData(SavePath);
        save.Set("level", 3);

        Assert.Equal(3f, save.GetFloat("level"));
        Assert.Equal(3f, new SaveData(SavePath).GetFloat("level"));
    }

    [Fact]
    public void Delete_Clear_AndNullStrings_RemoveValues()
    {
        var save = new SaveData(SavePath);
        save.Set("a", 1);
        save.Set("b", 2);
        save.Set("c", "x");

        Assert.True(save.Delete("a"));
        Assert.False(save.Delete("a"));
        save.Set("c", null);
        Assert.Equal(["b"], new SaveData(SavePath).Keys);

        save.Clear();
        Assert.Empty(new SaveData(SavePath).Keys);
    }

    [Fact]
    public void ClearProgress_ForgetsTheGamesValues_ButKeepsThePlayersSettingsAndControls()
    {
        var save = new SaveData(SavePath);
        save.Set("TookKey", true);
        save.Set("best", 12);
        save.Set(Game.VolumeSaveKey, 0.5f);
        save.Set(InputState.BindingsSaveKey, "{}");

        save.ClearProgress();

        Assert.Equal([InputState.BindingsSaveKey, Game.VolumeSaveKey], new SaveData(SavePath).Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void DamagedFile_StartsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
        File.WriteAllText(SavePath, "{ not json");

        var save = new SaveData(SavePath);

        Assert.Empty(save.Keys);
        save.Set("ok", 1);
        Assert.Equal(1, new SaveData(SavePath).GetInt("ok"));
    }

    [Fact]
    public void InMemorySaveData_WritesNothing()
    {
        var save = new SaveData();
        save.Set("a", 1);

        Assert.Null(save.FilePath);
        Assert.Equal(1, save.GetInt("a"));
    }

    [Fact]
    public void StoredElsewhere_StartsFromWhatWasStored_AndHandsOverEveryChange()
    {
        // As the web player keeps a game's saves in the page's localStorage.
        string? stored = null;
        var save = new SaveData(null, json => stored = json);
        save.Set("best", 42);
        save.Set("name", "Ada");

        var reloaded = new SaveData(stored, json => stored = json);

        Assert.Null(reloaded.FilePath);
        Assert.Equal(42, reloaded.GetInt("best"));
        Assert.Equal("Ada", reloaded.GetString("name"));
        reloaded.Clear();
        Assert.Empty(new SaveData(stored, _ => { }).Keys);
        // Something else in the storage under its key starts over rather than stopping the game.
        Assert.Empty(new SaveData("{ not json", _ => { }).Keys);
    }

    [Fact]
    public void Paths_AreScopedToTheGameOrProject()
    {
        Assert.EndsWith(Path.Combine("Griff Games", "My_Game", "save.json"), SaveData.PlayerPath("My:Game"));
        Assert.Equal(Path.Combine("C:\\proj", ".griff", "save.json"), SaveData.EditorPath("C:\\proj"));
    }
}
