using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.DevTools.Endpoints;
using Rask.Server.DevTools;

namespace Rask.DevTools;

internal sealed partial class DevToolsBootstrap
{
    // The Server face: UseRask finds this and hands it the route builder the bootstrap never sees.
    static partial void AttachHost(IServiceCollection services) =>
        services.TryAddSingleton<IRaskServerDevTools, DevToolsServerEndpoints>();
}
