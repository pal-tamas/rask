using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.DevTools.Endpoints;
using Rask.Server.DevTools;

namespace Rask.DevTools;

internal sealed partial class DevToolsBootstrap
{
    static partial void AttachHost(IServiceCollection services)
    {
        // The Server face: UseRask finds this and hands it the route builder the bootstrap never sees.
        services.TryAddSingleton<IRaskServerDevTools, DevToolsServerEndpoints>();

        // An app VS Code's F5 launched: the .test address when `rask dev` has set it up, and where to point the
        // browser. Registers nothing unless the build was a dev session.
        Server.EditorDevSessionServices.Add(services);
    }
}
