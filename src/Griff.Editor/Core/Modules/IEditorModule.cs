using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Core.Modules;

/// <summary>
/// A self-contained feature area. Each module registers its own services, commands and menus;
/// the shell never references a module's types directly.
/// </summary>
public interface IEditorModule
{
    void ConfigureServices(IServiceCollection services);
}
