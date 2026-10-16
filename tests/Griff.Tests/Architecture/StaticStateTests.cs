using System.Reflection;
using System.Runtime.CompilerServices;
using Griff.Runtime;

namespace Griff.Tests.Architecture;

/// <summary>
/// Settings belong to a game or to what runs it, never to the process: two games in one process (Play with Players'
/// windows, a simulation's runs, tests side by side) never share one, and no test has to run apart because another
/// changed it. So the engine keeps no mutable static but caches (readonly, and holding types weakly: the editor's
/// WeakTypeCacheTests), what's per thread ([ThreadStatic]: what the thread running a game is doing) and the few counts
/// below that describe the process's own threads rather than any game.
/// </summary>
public sealed class StaticStateTests
{
    // Mutable statics that aren't settings, each with why it's the process's.
    private static readonly Dictionary<string, string> Allowed = new()
    {
        // The worker threads are the process's, shared by every game; these are the job they're on, one at a time.
        ["Griff.Runtime.Workers.s_state"] = "the job the shared threads are on",
        ["Griff.Runtime.Workers.s_jobAllocated"] = "what the shared threads allocated for that job",
        ["Griff.Runtime.Workers.s_finished"] = "how many of that job's chunks are done",
        ["Griff.Runtime.Workers.s_faults"] = "that job's faults, a slot a chunk",
        ["Griff.Runtime.Workers.s_running"] = "whether the shared threads are on a job",
        // Breakpoint probes are compiled into scripts, which have no game to ask: what they count is the process's.
        ["Griff.Debugging.ScriptProbes.s_breaks"] = "how many times a script was held at a breakpoint, in any game",
        ["Griff.Debugging.ScriptProbes.s_watchers"] = "how many debuggers watch exceptions thrown through scripts",
        // The module assemblies loaded into the process are the process's, like any assembly it has loaded.
        ["Griff.ModuleAssemblies.s_loaded"] = "the module assemblies the process has loaded",
        ["Griff.ModuleAssemblies.s_revision"] = "how many times a host loaded more of them",
        ["Griff.ModuleAssemblies.s_resolving"] = "whether the process finds them for scripts that name them",
    };

    [Fact]
    public void The_engine_keeps_no_mutable_static_but_caches()
    {
        // Griff's and its modules'.
        var found = ModuleAssemblies.All.Prepend(typeof(Game).Assembly).SelectMany(a => a.GetTypes())
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .SelectMany(t => t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                // (A static property's backing field counts; the compiler's own caches of lambdas, named <>..., don't.)
                .Where(f => !f.IsLiteral && !f.IsInitOnly && !f.IsDefined(typeof(ThreadStaticAttribute)) && !f.Name.StartsWith("<>", StringComparison.Ordinal))
                .Select(f => $"{t.FullName}.{f.Name}"))
            .Where(name => !Allowed.ContainsKey(name))
            .Order()
            .ToList();

        Assert.True(found.Count == 0,
            "A mutable static is shared by every game in the process; make it the game's (or its host's), readonly, or [ThreadStatic]:\n" +
            string.Join("\n", found));
    }

    [Fact]
    public void Each_allowed_static_is_still_there() =>
        Assert.All(Allowed.Keys, name =>
        {
            var at = name.LastIndexOf('.');
            var field = typeof(Game).Assembly.GetType(name[..at])?.GetField(name[(at + 1)..], BindingFlags.Static | BindingFlags.NonPublic);
            Assert.True(field is { IsInitOnly: false }, $"{name} is gone or readonly: take it off the list.");
        });
}
