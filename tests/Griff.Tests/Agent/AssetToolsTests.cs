using Griff.Agent;

namespace Griff.Tests.Agent;

public sealed class AssetToolsTests : IDisposable
{
    private sealed class FakeProject(string assets) : IAgentProject
    {
        public List<string> Changed { get; } = [];

        public string? AssetsPath => assets;

        public Task<AssetFeedback?> AssetChangedAsync(string assetPath)
        {
            Changed.Add(assetPath);
            return Task.FromResult(assetPath.EndsWith(".cs") ? new AssetFeedback("Broken.cs(1,1): error", IsError: true) : null);
        }
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
