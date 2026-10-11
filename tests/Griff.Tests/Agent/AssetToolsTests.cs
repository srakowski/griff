using Griff.Agent;

namespace Griff.Tests.Agent;

public sealed class AssetToolsTests : IDisposable
{
    private sealed class FakeProject(string assets) : IAgentProject
    {
        public List<string> Changed { get; } = [];

        public List<string?> Previous { get; } = [];

        public string? AssetsPath => assets;

        public Task<AssetFeedback?> AssetChangedAsync(string assetPath, string? previousText = null)
        {
            Changed.Add(assetPath);
            Previous.Add(previousText);
            return Task.FromResult(assetPath.EndsWith(".cs") ? new AssetFeedback("Broken.cs(1,1): error", IsError: true) : null);
        }

        public List<(string From, string To)> Moved { get; } = [];

        public Task<AssetFeedback?> AssetMovedAsync(string fromPath, string toPath)
        {
            Moved.Add((fromPath, toPath));
            return Task.FromResult<AssetFeedback?>(new AssetFeedback("Relinked 2 copies in Level."));
        }
    }

    [Fact]
    public void Move_RenamesAPrefab_AndTellsTheEditorSoCopiesStayLinked()
    {
        Run("asset_write", """{"path": "Prefabs/Coin.prefab", "text": "{}"}""");
        Run("asset_write", """{"path": "Sprites/coin.png", "base64": "AAAA"}""");

        var moved = Run("asset_move", """{"from": "Prefabs/Coin.prefab", "to": "Prefabs/Money/Gold Coin.prefab"}""");

        Assert.False(moved.IsError, moved.Text);
        Assert.Contains("Relinked 2 copies", moved.Text);
        Assert.True(File.Exists(Path.Combine(_root, "Prefabs", "Money", "Gold Coin.prefab")));
        Assert.False(File.Exists(Path.Combine(_root, "Prefabs", "Coin.prefab")));
        Assert.Equal([("Prefabs/Coin.prefab", "Prefabs/Money/Gold Coin.prefab")], _project.Moved);

        // Only prefabs, and never over another file.
        Run("asset_write", """{"path": "Prefabs/Gem.prefab", "text": "{}"}""");
        Assert.Throws<AgentException>(() => Run("asset_move", """{"from": "Sprites/coin.png", "to": "Sprites/gold.png"}"""));
        Assert.Throws<AgentException>(() => Run("asset_move", """{"from": "Prefabs/Gem.prefab", "to": "Prefabs/Money/Gold Coin.prefab"}"""));
        Assert.Throws<AgentException>(() => Run("asset_move", """{"from": "Prefabs/Gem.prefab", "to": "../Gem.prefab"}"""));
        Assert.True(File.Exists(Path.Combine(_root, "Prefabs", "Gem.prefab")));
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-asset-tools-" + Guid.NewGuid().ToString("N"));
    private readonly FakeProject _project;
    private readonly Dictionary<string, AgentTool> _tools;

    public AssetToolsTests()
    {
        Directory.CreateDirectory(_root);
        _project = new FakeProject(_root);
        _tools = new AssetTools(_project).GetTools().ToDictionary(t => t.Name);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ToolResult Run(string tool, string json) => _tools[tool].Invoke(ToolArgs.Parse(json)).GetAwaiter().GetResult();

    [Fact]
    public void WriteReadListDelete()
    {
        var written = Run("asset_write", """{"path": "Notes/plan.txt", "text": "jump higher"}""");

        Assert.False(written.IsError, written.Text);
        Assert.Equal("jump higher", Run("asset_read", """{"path": "Notes/plan.txt"}""").Text);
        Assert.Contains("Notes/plan.txt", Run("assets_list", "{}").Text);
        Assert.Equal(["Notes/plan.txt"], _project.Changed);

        // Writing over a text file says what it held before (a prefab's copies keep what they changed from it).
        Run("asset_write", """{"path": "Notes/plan.txt", "text": "jump even higher"}""");
        Assert.Equal([null, "jump higher"], _project.Previous);

        Run("asset_delete", """{"path": "Notes/plan.txt"}""");
        Assert.False(File.Exists(Path.Combine(_root, "Notes", "plan.txt")));
    }

    [Fact]
    public void Base64_WritesBinaryFiles_AndPicturesComeBackAsImages()
    {
        var png = Convert.ToBase64String(Griff.Art.Png.Encode(new byte[16], 2, 2));

        Run("asset_write", $$"""{"path": "Sprites/dot.png", "base64": "{{png}}"}""");
        var read = Run("asset_read", """{"path": "Sprites/dot.png"}""");

        Assert.Contains(read.Content, c => (string?)c["type"] == "image");
    }

    [Fact]
    public void AScriptThatDoesntCompile_IsAnError_WithTheMessages()
    {
        var result = Run("asset_write", """{"path": "Scripts/Broken.cs", "text": "class {"}""");

        Assert.True(result.IsError);
        Assert.Contains("Broken.cs(1,1): error", result.Text);
    }

    [Fact]
    public void Paths_CantLeaveTheAssetsFolder()
    {
        Assert.Throws<AgentException>(() => _tools["asset_write"].Invoke(ToolArgs.Parse("""{"path": "../escape.txt", "text": "x"}""")).GetAwaiter().GetResult());
        Assert.Throws<AgentException>(() => _tools["asset_read"].Invoke(ToolArgs.Parse("""{"path": "C:/Windows/win.ini"}""")).GetAwaiter().GetResult());
    }
}
