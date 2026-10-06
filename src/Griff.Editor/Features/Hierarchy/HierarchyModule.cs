using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Panels;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Hierarchy;

public sealed class HierarchyModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services) => services.AddPanel<HierarchyPanel, HierarchyView>();
}
