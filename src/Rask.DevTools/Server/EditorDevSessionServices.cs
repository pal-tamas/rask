using System.Reflection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rask.Hosting.Shared;

namespace Rask.DevTools.Server;

/// <summary>
///     What an app VS Code's F5 launched needs from Rask that <c>rask dev</c> would otherwise have provided:
///     the <c>.test</c> address when it is already set up, and a line telling the editor where to point the
///     browser.
/// </summary>
internal static class EditorDevSessionServices
{
    /// <summary>
    ///     Registers both, but only for a build that was a dev session — an ordinary Debug build carries
    ///     nothing of this. Whether they then act is decided at startup, when the environment is known.
    /// </summary>
    internal static void Add(IServiceCollection services) => Add(services, Assembly.GetEntryAssembly());

    /// <summary>The same, for an explicit app assembly — the seam the tests use.</summary>
    internal static void Add(IServiceCollection services, Assembly? entryAssembly)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!EditorDevSession.IsDevSessionBuild(entryAssembly))
        {
            return;
        }

        services.TryAddSingleton<IDevHostMachine, DevHostMachine>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IConfigureOptions<KestrelServerOptions>, EditorDevHostKestrelSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EditorDevSessionAnnouncer>());
    }
}
