using Griff.Projects;

namespace Griff.Tests.Projects;

public sealed class SceneManagementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-scenes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task CreateScene_AndFindByNameOrPath()
    {
        var project = await Project.CreateAsync(_root, "Game");

        var path = project.CreateScene("Level2");

        Assert.Equal(path, project.FindScene("Level2"));
        Assert.Equal(path, project.FindScene("Scenes/Level2"));
        Assert.Equal(path, project.FindScene("Scenes/Level2.scene"));
        Assert.Null(project.FindScene("Nope"));
        Assert.Throws<IOException>(() => project.CreateScene("Level2"));
    }

    [Fact]
    public async Task SetStartScene_PersistsInManifest()
    {
        var project = await Project.CreateAsync(_root, "Game");
        var level = project.CreateScene("Level2");

        await project.SetStartSceneAsync(level);
        var reloaded = await Project.LoadAsync(project.RootPath);

        Assert.Equal("Assets/Scenes/Level2.scene", reloaded.Manifest.StartScene);
        Assert.Equal(level, reloaded.StartScenePath);
    }
}
