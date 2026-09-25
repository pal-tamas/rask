using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Rask.Hosting.Shared;

namespace Rask.DevTools.Server;

/// <summary>
///     Writes <c>Rask dev: open &lt;url&gt;</c> once the app is listening, which the scaffolded
///     <c>launch.json</c>'s <c>serverReadyAction</c> opens.
/// </summary>
/// <remarks>
///     Written to standard output rather than logged: the editor watches the debug console for the line, and
///     an app that filters or reformats its logs must not quietly stop the browser from opening.
/// </remarks>
internal sealed class EditorDevSessionAnnouncer(
    IHostEnvironment environment,
    IDevHostMachine machine,
    IHostApplicationLifetime lifetime,
    IServer server) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!EditorDevSession.IsActive(Assembly.GetEntryAssembly(), environment.IsDevelopment(), Environment.GetEnvironmentVariable))
        {
            return Task.CompletedTask;
        }

        lifetime.ApplicationStarted.Register(() =>
        {
            var url = EditorDevHostKestrelSetup.Resolve(environment, machine)?.Url
                      ?? PreferredAddress(server.Features.Get<IServerAddressesFeature>()?.Addresses);

            if (url is not null)
            {
                Console.Out.WriteLine(EditorDevSession.OpenLinePrefix + url);
            }
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     The address a browser should open: HTTPS when the app listens on it, with a wildcard bind turned
    ///     into <c>localhost</c> (a browser cannot open <c>0.0.0.0</c> or <c>[::]</c>).
    /// </summary>
    internal static string? PreferredAddress(IEnumerable<string>? addresses)
    {
        var list = addresses?.ToList();
        if (list is not { Count: > 0 })
        {
            return null;
        }

        var chosen = list.FirstOrDefault(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) ?? list[0];

        return chosen
            .Replace("://0.0.0.0", "://localhost", StringComparison.Ordinal)
            .Replace("://[::]", "://localhost", StringComparison.Ordinal)
            .Replace("://+", "://localhost", StringComparison.Ordinal)
            .Replace("://*", "://localhost", StringComparison.Ordinal);
    }
}
