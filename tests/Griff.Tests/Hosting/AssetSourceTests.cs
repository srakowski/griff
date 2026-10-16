using System.Collections.Concurrent;
using Griff.Hosting;
using Griff.Projects;

namespace Griff.Tests.Hosting;

/// <summary>
/// Where a game's files come from, as each host has them: an exported game's fixed, a browser's fetched before it starts,
/// and the editor's watched, a change said once it has settled.
/// </summary>
public sealed class AssetSourceTests : IDisposable
{
    private readonly string _assets = Path.Combine(Path.GetTempPath(), "griff-assets-" + Guid.NewGuid().ToString("N")[..8]);

    public AssetSourceTests() => Directory.CreateDirectory(Path.Combine(_assets, "Art"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_assets, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void A_folder_finds_its_files_by_any_spelling_of_their_names()
    {
        File.WriteAllBytes(Path.Combine(_assets, "Art", "hero.png"), [1]);
        var folder = new AssetFolder(_assets + Path.DirectorySeparatorChar);

        Assert.Equal(Path.Combine(_assets, "Art", "hero.png"), folder.PathOf("Art/hero.png"));
        Assert.Equal(folder.PathOf("Art/hero.png"), folder.PathOf("Art\\hero.png"));
        Assert.True(folder.Exists(folder.PathOf("Art/hero.png")));
        Assert.False(folder.Exists(folder.PathOf("Art/missing.png")));
        Assert.False(AssetFolder.None.Exists(AssetFolder.None.PathOf("Art/hero.png")));
    }

    [Fact]
    public void A_browsers_files_are_the_ones_fetched()
    {
        File.WriteAllBytes(Path.Combine(_assets, "Art", "hero.png"), [1]);
        File.WriteAllBytes(Path.Combine(_assets, "Art", "stray.png"), [1]);
        var fetched = new PreloadedAssets(_assets, [Path.Combine(_assets, "Art", "hero.png"), Path.Combine(_assets, "..", "project.griff")]);

        Assert.True(fetched.Exists(fetched.PathOf("Art/hero.png")));
        // On the disk, but not among what was fetched: a browser has nothing else.
        Assert.False(fetched.Exists(fetched.PathOf("Art/stray.png")));
    }

    [Fact]
    public void A_watched_folder_says_a_change_once_it_has_settled()
    {
        using var watched = new WatchedAssetFolder(_assets);
        var said = new ConcurrentQueue<string>();
        watched.Changed += said.Enqueue;
        var path = watched.PathOf("Art/hero.png");

        // Written in a burst, it's said once; held open by the program writing it, not until it's let go.
        using (var writing = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            for (var i = 0; i < 5; i++)
            {
                writing.Write([1, 2, 3]);
                writing.Flush();
                Thread.Sleep(30);
            }

            Thread.Sleep(600);
            Assert.DoesNotContain(path, said);
        }

        Wait(() => said.Contains(path));
        Thread.Sleep(400);
        Assert.Single(said, path);

        // A rename says both names; a delete, the one gone.
        var renamed = watched.PathOf("Art/villain.png");
        File.Move(path, renamed);
        Wait(() => said.Count(p => p == path) == 2 && said.Contains(renamed));
        File.Delete(renamed);
        Wait(() => said.Count(p => p == renamed) == 2);

        // Nothing is said once it's let go.
        watched.Dispose();
        File.WriteAllBytes(path, [4]);
        Thread.Sleep(500);
        Assert.Equal(2, said.Count(p => p == path));
    }

    private static void Wait(Func<bool> done)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(10), "The watched folder never said so.");
            Thread.Sleep(20);
        }
    }
}
