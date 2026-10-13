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

                // Tests never touch the user's own settings (recent projects and the like).
                services.AddSingleton<ISettingsStore, TestSettings>();
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
