using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Core.Panels;

public sealed record PanelDescriptor(Type PanelType, Type ViewType);

public static class PanelServiceCollectionExtensions
{
    public static IServiceCollection AddPanel<TPanel, TView>(this IServiceCollection services)
        where TPanel : ToolPanel
        where TView : FrameworkElement
    {
        services.AddSingleton<TPanel>();
        services.AddTransient<TView>();
        services.AddSingleton(new PanelDescriptor(typeof(TPanel), typeof(TView)));
        return services;
    }
}
