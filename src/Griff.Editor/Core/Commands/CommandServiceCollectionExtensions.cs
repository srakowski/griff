using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Core.Commands;

public static class CommandServiceCollectionExtensions
{
    /// <summary>Registers a command so it can be injected directly and is discovered by the shell.</summary>
    public static IServiceCollection AddCommand<TCommand>(this IServiceCollection services)
        where TCommand : EditorCommand
    {
        services.AddSingleton<TCommand>();
        services.AddSingleton<EditorCommand>(sp => sp.GetRequiredService<TCommand>());
        return services;
    }

    public static IServiceCollection AddMenu(this IServiceCollection services, string id, string text, int order) =>
        services.AddSingleton(new MenuDefinition(id, text, order));
}
