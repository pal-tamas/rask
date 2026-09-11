using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Core.Diagnostics.DevTools;

namespace Rask.DevTools;

/// <summary>
///     The entry point a Rask host constructs by name (<see cref="RaskDevToolsLoader.BootstrapTypeName" />)
///     when a Debug build carries this package. Renaming or moving it breaks that lookup silently, which is
///     what <c>DevToolsBootstrapTests</c> pins.
/// </summary>
internal sealed partial class DevToolsBootstrap : IRaskDevToolsBootstrap
{
    public void Attach(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<DevToolsRegistration>();
        AttachHost(services);
    }

    /// <summary>
    ///     What only one host face adds. Implemented in a file compiled for that target framework alone
    ///     (<c>DevToolsBootstrap.Server.cs</c>); a face with nothing to add has no implementation, and the
    ///     compiler removes the call.
    /// </summary>
    static partial void AttachHost(IServiceCollection services);
}

/// <summary>
///     Present in a container exactly when the devtools attached to it. The feed, the probe and the panel
///     hang off this registration in later slices; on its own it is inert.
/// </summary>
internal sealed class DevToolsRegistration;
