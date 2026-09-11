using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Diagnostics.DevTools;

namespace Rask.DevTools;

/// <summary>
///     The entry point a Rask host constructs by name (<see cref="RaskDevToolsLoader.BootstrapTypeName" />)
///     when a Debug build carries this package. Renaming or moving it breaks that lookup silently, which is
///     what <c>DevToolsBootstrapTests</c> pins.
/// </summary>
internal sealed class DevToolsBootstrap : IRaskDevToolsBootstrap
{
    public void Attach(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<DevToolsRegistration>();

#if !BROWSER
        // An app VS Code's F5 launched: the .test address when `rask dev` has set it up, and where to point the
        // browser. Registers nothing unless the build was a dev session. The browser face has no Kestrel.
        Server.EditorDevSessionServices.Add(services);
#endif
    }
}

/// <summary>
///     Present in a container exactly when the devtools attached to it. The feed, the probe and the panel
///     hang off this registration in later slices; on its own it is inert.
/// </summary>
internal sealed class DevToolsRegistration;
