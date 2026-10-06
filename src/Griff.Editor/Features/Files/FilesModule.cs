using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Files;

public sealed class FilesModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services) => services
        .AddCommand<NewFileCommand>()
        .AddCommand<OpenFileCommand>()
        .AddCommand<SaveFileCommand>()
        .AddCommand<SaveFileAsCommand>()
        .AddCommand<ExitCommand>();
}
