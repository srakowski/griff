using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Panels;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Assets;

public sealed class AssetsModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services) => services
        .AddPanel<AssetsPanel, AssetsView>()
        .AddSingleton<AssetPlacement>()
        .AddCommand<AddAssetToSceneCommand>()
        .AddCommand<OpenAssetExternallyCommand>()
        .AddCommand<ShowAssetInExplorerCommand>();
}
