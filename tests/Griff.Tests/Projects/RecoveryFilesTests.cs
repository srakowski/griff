using Griff.Projects;

namespace Griff.Tests.Projects;

public sealed class RecoveryFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-recovery-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task NewerCopy_IsFound_UntilDeleted()
    {
        var project = await Project.CreateAsync(_root, "Game");
        var scene = project.StartScenePath!;
        Assert.Null(RecoveryFiles.FindNewer(project, scene));

        File.SetLastWriteTimeUtc(scene, DateTime.UtcNow.AddMinutes(-5));
        RecoveryFiles.Write(project, scene, "{ \"entities\": [] }");

        var found = RecoveryFiles.FindNewer(project, scene);
        Assert.NotNull(found);
        Assert.Equal("{ \"entities\": [] }", File.ReadAllText(found));
        Assert.StartsWith(Path.Combine(project.RootPath, ".griff"), found);

        RecoveryFiles.Delete(project, scene);
        Assert.Null(RecoveryFiles.FindNewer(project, scene));
    }

    [Fact]
    public async Task OlderCopy_IsIgnored()
    {
        var project = await Project.CreateAsync(_root, "Game");
        var scene = project.StartScenePath!;
        RecoveryFiles.Write(project, scene, "{}");
        File.SetLastWriteTimeUtc(RecoveryFiles.PathFor(project, scene), DateTime.UtcNow.AddMinutes(-5));

        Assert.Null(RecoveryFiles.FindNewer(project, scene));
    }

    [Fact]
    public async Task NewProjects_IgnoreTheRecoveryFolder()
    {
        var project = await Project.CreateAsync(_root, "Game");

        Assert.Contains(".griff/", File.ReadAllText(Path.Combine(project.RootPath, ".gitignore")));
    }
}
