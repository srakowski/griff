using System.Runtime.ExceptionServices;
using Griff.Editor.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Tests;

/// <summary>The editor's real services, built from every module, for a test to run against on a WPF thread.</summary>
internal static class EditorHost
{
    // WPF objects (the scene view, the game surface, the clipboard) need a single-threaded apartment.
    public static void Run(Action<IServiceProvider> test) => Run(_ => { }, test);

    /// <summary>As <see cref="Run(Action{IServiceProvider})"/>, with services swapped first (a fake for one that reaches outside the editor).</summary>
    public static void Run(Action<IServiceCollection> configure, Action<IServiceProvider> test)
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
                test(provider);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
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
