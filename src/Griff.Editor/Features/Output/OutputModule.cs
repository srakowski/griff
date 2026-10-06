using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Panels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Output;

public sealed class OutputModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<OutputLog>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, OutputLoggerProvider>());
        services.AddPanel<OutputPanel, OutputPanelView>();
    }
}
