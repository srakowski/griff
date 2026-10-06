using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Projects;

public sealed class ProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Create_MakesFolderManifestAndAssets()
    {
        var project = await Project.CreateAsync(_root, "  My Game  ");

        Assert.Equal(Path.Combine(_root, "My Game"), project.RootPath);
        Assert.True(File.Exists(project.ManifestPath));
        Assert.True(Directory.Exists(project.AssetsPath));
        Assert.Equal("My Game", project.Name);
        Assert.Contains("Build/", File.ReadAllText(Path.Combine(project.RootPath, ".gitignore")));
    }

    [Fact]
    public async Task Create_WritesStartSceneWithCamera()
    {
        var project = await Project.CreateAsync(_root, "My Game");

        Assert.Equal("Assets/Main.scene", project.Manifest.StartScene);
        Assert.Equal([project.StartScenePath], project.FindScenes());

        await using var stream = File.OpenRead(project.StartScenePath!);
        var scene = new SceneSerializer(ComponentRegistry.CreateDefault()).Load(stream);
        var camera = Assert.Single(scene.Roots);
        Assert.Equal("Main Camera", camera.Name);
        Assert.NotNull(camera.GetComponent<Camera>());
    }

    [Fact]
    public async Task Paths_ConvertBetweenRelativeAndAbsolute()
    {
        var project = await Project.CreateAsync(_root, "My Game");
        var absolute = project.ResolvePath("Assets/Sprites/hero.png");

        Assert.Equal(Path.Combine(project.RootPath, "Assets", "Sprites", "hero.png"), absolute);
        Assert.Equal("Assets/Sprites/hero.png", project.GetRelativePath(absolute));
    }

    [Fact]
    public async Task Load_RoundTripsManifest()
    {
        var created = await Project.CreateAsync(_root, "My Game");

        var loaded = await Project.LoadAsync(created.RootPath);

        Assert.Equal("My Game", loaded.Name);
        Assert.Equal(ProjectManifest.CurrentFormatVersion, loaded.Manifest.FormatVersion);
        Assert.Equal(created.RootPath, loaded.RootPath);
    }

    [Fact]
    public async Task Create_IntoNonEmptyFolder_Throws()
    {
        await Project.CreateAsync(_root, "My Game");

        await Assert.ThrowsAsync<IOException>(() => Project.CreateAsync(_root, "My Game"));
    }

    [Fact]
    public async Task Create_IntoEmptyFolder_ReusesIt()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Empty"));

        var project = await Project.CreateAsync(_root, "Empty");

        Assert.True(File.Exists(project.ManifestPath));
    }

    [Theory]
    [InlineData("Space Shooter 2", true)]
    [InlineData("my-game_v1", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a/b", false)]
    [InlineData("CON", false)]
    [InlineData("con.txt", false)]
    [InlineData("Game.", false)]
    public void ProjectNames_Validate(string name, bool valid)
    {
        Assert.Equal(valid, ProjectNames.Validate(name) is null);
    }

    [Fact]
    public void ProjectNames_RejectsOverlongNames()
    {
        Assert.NotNull(ProjectNames.Validate(new string('x', ProjectNames.MaxLength + 1)));
    }
}
