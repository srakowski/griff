using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Undo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Tests;

/// <summary>The editor's real services, built from every module, for a test to run against on a WPF thread.</summary>
internal static class EditorHost
{
    /// <summary>
    /// How long a test may run on its WPF thread (and, as <see cref="WaitTimeout"/>, a task it waits for). Longer, and the test fails
    /// saying where it was, instead of leaving the run hanging (a test host that never finishes) behind a dialog nobody
    /// can answer or a task that never completes.
    /// </summary>
    public static readonly TimeSpan TestTimeout = TimeSpan.FromMinutes(5);

    /// <summary>How long a task a test waits for (see <see cref="Pump{T}"/>) may take.</summary>
    public static readonly TimeSpan WaitTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Runs <paramref name="task"/> to the end on the test's WPF thread, pumping its dispatcher as the editor's would, and
    /// returns what it gave. Fails with a message naming the test (and its line) if the task takes longer than
    /// <see cref="TestTimeout"/>.
    /// </summary>
    public static T Pump<T>(Task<T> task, TimeSpan? timeout = null)
    {
        PumpUntilDone(task, timeout ?? WaitTimeout);
        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Undoes the last change as Edit &gt; Undo (Ctrl+Z) does: in the document being worked on, or in <paramref name="in"/>
    /// when given (a panel's undo button gives its own). False when there was nothing to undo.
    /// </summary>
    public static bool Undo(IServiceProvider services, IDocumentEditor? @in = null) => Step(services, "Edit.Undo", @in);

    /// <summary>Redoes the last change undone, as Edit &gt; Redo (Ctrl+Y) does; see <see cref="Undo"/>.</summary>
    public static bool Redo(IServiceProvider services, IDocumentEditor? @in = null) => Step(services, "Edit.Redo", @in);

    /// <summary>Undoes as an agent does, with Edit &gt; Undo's tool, in the document being worked on; gives what it said.</summary>
    public static string AgentUndo(IServiceProvider services) => AgentCommand(services, "Edit.Undo");

    /// <summary>Redoes as an agent does, with Edit &gt; Redo's tool; gives what it said.</summary>
    public static string AgentRedo(IServiceProvider services) => AgentCommand(services, "Edit.Redo");

    private static string AgentCommand(IServiceProvider services, string id)
    {
        var tool = services.GetServices<Griff.Agent.IAgentToolSource>().SelectMany(s => s.GetTools()).Single(t => t.Name == Features.Agent.CommandTools.ToolName(id));
        var result = Pump(tool.Invoke(Griff.Agent.ToolArgs.Empty));
        Assert.False(result.IsError, result.Text);
        return result.Text;
    }

    /// <summary>Runs the command <paramref name="id"/> on <paramref name="target"/>, as its menu item does (a component's menu gives it the component).</summary>
    public static void Command(IServiceProvider services, string id, object target) =>
        Pump(services.GetRequiredService<CommandRegistry>().Get(id).ExecuteAsync(target));

    private static bool Step(IServiceProvider services, string id, IDocumentEditor? @in)
    {
        var command = services.GetRequiredService<CommandRegistry>().Get(id);
        if (!command.CanExecute(@in))
            return false;
        Pump(command.ExecuteAsync(@in));
        return true;
    }

    /// <summary>As <see cref="Pump{T}"/>, for a task that gives nothing.</summary>
    public static void Pump(Task task, TimeSpan? timeout = null)
    {
        PumpUntilDone(task, timeout ?? WaitTimeout);
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntilDone(Task task, TimeSpan timeout)
    {
        var caller = new StackTrace(2, fNeedFileInfo: true);
        var frame = new DispatcherFrame();
        var timedOut = false;
        var timer = new DispatcherTimer(timeout, DispatcherPriority.Normal, (_, _) => (timedOut, frame.Continue) = (true, false), Dispatcher.CurrentDispatcher);
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (timedOut && !task.IsCompleted)
            throw new TimeoutException($"A task the test waits for didn't finish in {timeout.TotalSeconds:0} seconds. It is probably waiting for an answer to a dialog nobody can give, or for something that never happens. The test was at:\n{caller}");
    }

    // WPF objects (the scene view, the game surface, the clipboard) need a single-threaded apartment.
    public static void Run(Action<IServiceProvider> test) => Run(_ => { }, test);

    /// <summary>As <see cref="Run(Action{IServiceProvider})"/>, with services swapped first (a fake for one that reaches outside the editor).</summary>
    public static void Run(Action<IServiceCollection> configure, Action<IServiceProvider> test, TimeSpan? timeout = null)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var services = new ServiceCollection();
                services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
                App.ConfigureCoreServices(services);
                foreach (var module in App.Modules)
                    module.ConfigureServices(services);

                // Tests never touch the user's own settings (recent projects and the like), or the clipboard.
                services.AddSingleton<ISettingsStore, TestSettings>();
                services.AddSingleton<Griff.Editor.Core.Workspace.IClipboard, TestClipboard>();
                configure(services);
                using var provider = services.BuildServiceProvider();
                // As the editor's window does at start (its Edit > Undo needs it): from now on, what's worked in is followed.
                provider.GetRequiredService<ActiveDocument>();
                test(provider);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        // A thread stuck for good mustn't keep the test host from ending once the test has failed.
        thread.IsBackground = true;
        var caller = new StackTrace(1, fNeedFileInfo: true);
        thread.Start();
        if (!thread.Join(timeout ?? TestTimeout))
            throw new TimeoutException($"The test didn't finish in {(timeout ?? TestTimeout).TotalSeconds:0} seconds on its WPF thread. It is probably blocked on a dialog that opened a window nobody can close, or on a task or lock that never completes. It was started from:\n{caller}");
        if (failure is not null)
            ExceptionDispatchInfo.Throw(failure);
    }
}

/// <summary>Settings kept in memory for one test.</summary>
internal sealed class TestSettings : ISettingsStore
{
    public EditorSettings Settings { get; } = new();

    public void Save()
    {
    }
}

/// <summary>
/// A clipboard of the test's own. Windows' clipboard is shared by every program, so tests running side by side (in this
/// run, another, or another checkout's) would copy over each other's entities between a cut and its paste.
/// </summary>
internal sealed class TestClipboard : Griff.Editor.Core.Workspace.IClipboard
{
    private readonly Dictionary<string, string> _data = [];

    public string GetText() => _data.GetValueOrDefault("Text", "");

    public bool SetText(string text)
    {
        _data.Clear();
        _data["Text"] = text;
        return true;
    }

    public string? Get(string format) => _data.GetValueOrDefault(format);

    public bool Set(string format, string data)
    {
        SetText(data);
        _data[format] = data;
        return true;
    }
}
