using System.Windows;
using Griff.Editor.Core.Undo;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Core.Panels;

/// <summary>A panel and its view: their types, and how to get the panel (a singleton) and make a view for it.</summary>
public sealed record PanelDescriptor(Type PanelType, Type ViewType, Func<ToolPanel> Panel, Func<FrameworkElement> View);

public static class PanelServiceCollectionExtensions
{
    /// <summary>
    /// Registers a panel and its view. A panel that edits a document of its own (an <see cref="IDocumentEditor"/>, as the
    /// Tables panel is) is one Edit &gt; Undo can go to.
    /// </summary>
    public static IServiceCollection AddPanel<TPanel, TView>(this IServiceCollection services)
        where TPanel : ToolPanel
        where TView : FrameworkElement
    {
        services.AddSingleton<TPanel>();
        services.AddTransient<TView>();
        services.AddSingleton(sp => new PanelDescriptor(typeof(TPanel), typeof(TView), () => sp.GetRequiredService<TPanel>(), () => sp.GetRequiredService<TView>()));
        if (typeof(IDocumentEditor).IsAssignableFrom(typeof(TPanel)))
            services.AddSingleton(sp => (IDocumentEditor)sp.GetRequiredService<TPanel>());
        return services;
    }
}
