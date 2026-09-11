using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Hosting.Shared;

namespace Rask.Server.Dev;

/// <summary>
///     Serves this app's islands from a Vite dev server the app starts itself, when an editor launched it.
/// </summary>
/// <remarks>
///     <para>
///         Under <c>rask dev</c> the CLI starts that server beside the app and hands its address over as
///         <c>RASK_ISLANDS_DEV</c>. Under VS Code's F5 the debugger launches the app and nothing else runs, so
///         without this every island would import from a port nobody is listening on — a dev-session build
///         (<c>RaskExternalDevServer</c>) writes a manifest that points at the dev server instead of bundling.
///     </para>
///     <para>
///         It acts only for an editor-launched session (<see cref="EditorDevSession.IsActive" />): never under
///         <c>dotnet watch</c>, never outside Development, and never for a build that was not a dev session.
///     </para>
/// </remarks>
internal sealed partial class IslandDevServer : BackgroundService
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<IslandDevServer> _logger;
    private volatile string? _url;

    // Public on an internal type: the container resolves only public constructors.
    public IslandDevServer(IHostEnvironment environment, ILogger<IslandDevServer> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    /// <summary>Where the dev server this app started listens, or null when it started none.</summary>
    internal string? Url => _url;

    /// <summary>
    ///     Registers the server for a dev-session build, once. Nothing at all for any other build.
    /// </summary>
    internal static void Register(IServiceCollection services, Assembly? entryAssembly)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!EditorDevSession.IsDevSessionBuild(entryAssembly)
            || services.Any(d => d.ServiceType == typeof(IslandDevServer)))
        {
            return;
        }

        // One instance, reachable both as the hosted service and by the page endpoint that asks for its URL.
        services.AddSingleton<IslandDevServer>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<IslandDevServer>());
    }

    /// <summary>Records where the islands are served from, or that they no longer are.</summary>
    internal void ServingFrom(string? url) => _url = url;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!EditorDevSession.IsActive(Assembly.GetEntryAssembly(), _environment.IsDevelopment(), Environment.GetEnvironmentVariable))
        {
            return;
        }

        var contentRoot = _environment.ContentRootPath;
        var pointer = await IslandDevPointer
            .WaitAsync(contentRoot, TimeSpan.FromMinutes(3), stoppingToken)
            .ConfigureAwait(false);

        if (pointer is not { } found)
        {
            return;
        }

        DevServerProcess process;
        try
        {
            process = DevServerProcess.Start(
                "islands-vite",
                "npx",
                ["--no-install", "vite", "--config", found.Config],
                contentRoot,
                Path.Combine(contentRoot, "obj", "rask"),
                LogViteLine);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The app is still worth running under the debugger; only island hot reload is lost.
            LogNpxUnavailable();
            return;
        }

        try
        {
            ServingFrom(found.Url);
            LogServing(found.Url);
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The app is stopping.
        }
        finally
        {
            ServingFrom(null);
            await process.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void LogViteLine(string line) => LogVite(line);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Islands dev server: {Line}")]
    private partial void LogVite(string line);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Serving islands from {Url} (hot reload).")]
    private partial void LogServing(string url);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "npx is not available, so the islands dev server did not start. The app is still running, but islands will not load until Node.js is installed (https://nodejs.org).")]
    private partial void LogNpxUnavailable();
}
