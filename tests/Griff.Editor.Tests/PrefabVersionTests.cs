using System.IO;
using System.Numerics;
using System.Windows.Threading;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Projects;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Each placed copy remembers the version of its prefab it was made from, so a prefab changed outside the editor (a pull,
/// an agent's asset tools, a text editor) is noticed, while the project is open or as it opens, and its copies follow it,
/// keeping what each changed.
/// </summary>
public sealed class PrefabVersionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static T Wait<T>(Task<T> task) => EditorHost.Pump(task);

    private static void Wait(Task task) => Wait(task.ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }, TaskScheduler.Default));

    private static Entity Coin(string image)
    {
        var coin = new Entity("Coin");
        coin.AddComponent<Sprite>().Image = image;
        coin.AddComponent<Pickup>().Amount = 1;
        return coin;
    }

    // The editor going on with its work until `done`, as it does while a file settles and its change is taken in.
    private static bool Until(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
            Wait(Task.Delay(50));
        return done();
    }

    [Fact]
    public void APrefabChangedOutsideTheEditor_UpdatesItsCopies_KeepingWhatEachChanged() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;
        var path = project.DefaultPrefabPath("Coin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, workspace.Serializer.SavePrefabToString(Coin("Sprites/coin.png")));
        Assert.True(Until(() => workspace.PrefabLibrary!.VersionOf(path) is not null));

        var files = services.GetRequiredService<PrefabFiles>();
        var copy = files.Instantiate("Prefabs/Coin.prefab")!;
        copy.Transform.Position = new Vector3(2, 0, 0);
        copy.GetComponent<Pickup>()!.Amount = 5;
        workspace.Scene!.Execute(new Griff.Editing.AddEntityEdit(workspace.Scene.Scene, copy));
        var made = copy.PrefabVersion;
        Assert.Equal(made, workspace.PrefabLibrary!.VersionOf(path));

        // A text editor saves the prefab with a new picture.
        File.WriteAllText(path, workspace.Serializer.SavePrefabToString(Coin("Sprites/gold.png")));

        Assert.True(Until(() => copy.GetComponent<Sprite>()!.Image == "Sprites/gold.png"), "The copy wasn't updated.");
        Assert.Equal(5, copy.GetComponent<Pickup>()!.Amount);
        Assert.Equal(new Vector3(2, 0, 0), copy.Transform.Position);
        Assert.NotEqual(made, copy.PrefabVersion);
        Assert.Equal(workspace.PrefabLibrary.VersionOf(path), copy.PrefabVersion);
        Assert.Contains("Coin changed. Updated 1 copy in Main.", services.GetRequiredService<IStatusService>().Message, StringComparison.Ordinal);

        // One undo step, as saving it here would be.
        workspace.Scene.History.Undo();
        Assert.Equal(("Sprites/coin.png", 5), (copy.GetComponent<Sprite>()!.Image, copy.GetComponent<Pickup>()!.Amount));
        Assert.Equal(made, copy.PrefabVersion);
    });

    [Fact]
    public void CopiesOfAPrefabChangedWhileTheProjectWasClosed_CatchUpAsItOpens() => EditorHost.Run(services =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        var serializer = workspace.Serializer;

        // A project not open: a level holding a copy, given 5, of the coin as it was; then the coin changes (a pull).
        var project = Wait(Project.CreateAsync(Path.Combine(_root, "Closed"), "Closed"));
        var path = project.DefaultPrefabPath("Coin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var before = serializer.SavePrefabToString(Coin("Sprites/coin.png"));
        File.WriteAllText(path, before);
        var level = new Scene();
        var copy = serializer.LoadPrefabFromString(before);
        (copy.Prefab, copy.PrefabVersion) = ("Prefabs/Coin.prefab", PrefabVersion.Of(before));
        copy.GetComponent<Pickup>()!.Amount = 5;
        level.Add(copy);
        File.WriteAllText(project.CreateScene("Level 2"), serializer.SaveToString(level));
        File.WriteAllText(path, serializer.SavePrefabToString(Coin("Sprites/gold.png")));

        Assert.True(Wait(workspace.OpenProjectAsync(project.RootPath)));

        var caught = serializer.LoadFromString(File.ReadAllText(workspace.Project!.FindScene("Level 2")!)).Roots.Single();
        Assert.Equal(("Sprites/gold.png", 5), (caught.GetComponent<Sprite>()!.Image, caught.GetComponent<Pickup>()!.Amount));
        Assert.Equal(PrefabVersion.Of(File.ReadAllText(path)), caught.PrefabVersion);
        Assert.Contains("Updated 1 copy in Level 2.", services.GetRequiredService<IStatusService>().Message, StringComparison.Ordinal);
    });
}
