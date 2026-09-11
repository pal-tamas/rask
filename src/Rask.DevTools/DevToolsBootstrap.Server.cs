using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Routing;
using Rask.DevTools.Endpoints;
using Rask.DevTools.Panel;
using Rask.Server.DevTools;

namespace Rask.DevTools;

internal sealed partial class DevToolsBootstrap
{
    /// <summary>The route pattern the panel application is mounted on.</summary>
    internal const string PanelPattern = DevToolsServerEndpoints.Prefix + "/{**path}";

    // The Server face: UseRask finds the endpoints and hands them the route builder the bootstrap never sees, and the
    // panel is mounted as its own application so its pages share neither the host's document nor its route table.
    static partial void AttachHost(IServiceCollection services)
    {
        services.TryAddSingleton<IRaskServerDevTools, DevToolsServerEndpoints>();

        // Once, however often the devtools attach: a second mount of the same pattern would be a second endpoint.
        if (!services.Any(d => !d.IsKeyedService && d.ImplementationInstance is RaskMountedApp { Root: var root }
                               && root == typeof(DevToolsShell)))
        {
            services.AddSingleton(new RaskMountedApp(typeof(DevToolsShell), PanelPattern, typeof(DevToolsShell).Assembly));
        }

        // An app VS Code's F5 launched: the .test address when `rask dev` has set it up, and where to point the
        // browser. Registers nothing unless the build was a dev session.
        Server.EditorDevSessionServices.Add(services);
    }
}
