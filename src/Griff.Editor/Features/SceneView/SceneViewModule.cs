using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.SceneView;

// The Scene view: where the level is laid out. Its view models (what it does with the selection, where it looks, the
// tile palette) are services agents reach too; the control draws them and turns the mouse and keys into calls on them.
public sealed class SceneViewModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SceneViewModel>();
        services.AddSingleton<SceneViewport>();
        services.AddSingleton<TilePalette>();
        services.AddSingleton<SceneViewControl>();
        services
            .AddCommand<FrameSelectionCommand>()
            .AddCommand<ScaleHandlesCommand>()
            .AddCommand<ResizeHandlesCommand>()
            .AddCommand<ColliderHandlesCommand>()
            .AddCommand<DistanceHandlesCommand>();
    }
}
