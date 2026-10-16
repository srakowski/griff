using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Panels;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Inspector;

/// <summary>The Inspector panel, and the commands on its menus: a component's, and a field a prefab copy changed.</summary>
public sealed class InspectorModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services) => services
        .AddPanel<InspectorPanel, InspectorView>()
        .AddCommand<ResetComponentCommand>()
        .AddCommand<AddComponentToAllCommand>()
        .AddCommand<CopyComponentValuesCommand>()
        .AddCommand<PasteComponentValuesCommand>()
        .AddCommand<MoveComponentUpCommand>()
        .AddCommand<MoveComponentDownCommand>()
        .AddCommand<RemoveComponentCommand>()
        .AddCommand<RevertFieldToPrefabCommand>()
        .AddCommand<ApplyFieldToPrefabCommand>();
}
