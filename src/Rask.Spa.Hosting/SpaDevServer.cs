using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Hosting.Shared;

namespace Rask.Spa.Hosting;

/// <summary>
///     Runs the bundler's own dev server as this app's child, when an editor launched the app.
/// </summary>
/// <remarks>
///     <para>
///         Under <c>rask dev</c> the CLI starts the client's dev server beside the host, and the browser talks
///         to it. Under VS Code's F5 the debugger launches only the host — and a dev-session build skipped the
///         production bundle, so without this there would be nothing to serve at all. It runs the same
///         <c>npm run</c> script <c>rask dev</c> would, in the client directory the build baked, and once the
///         dev server answers it tells the editor where to point the browser.
///     </para>
///     <para>
///         It acts only for an editor-launched session (<see cref="EditorDevSession.IsActive" />): never under
///         <c>dotnet watch</c>, never outside Development, never for a build that was not a dev session.
///     </para>
/// </remarks>
internal sealed partial class SpaDevServer : BackgroundService
{
    /// <summary>Vite's default, for a client whose csproj names no dev server URL — what <c>rask dev</c> assumes too.</summary>
    internal const string DefaultDevServerUrl = "http://localhost:5173";

    private readonly IHostEnvironment _environment;
    private readonly ILogger<SpaDevServer> _logger;

    // Public on an internal type: the container resolves only public constructors.
    public SpaDevServer(IHostEnvironment environment, ILogger<SpaDevServer> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    /// <summary>What to run, where, and where the browser should go.</summary>
    internal sealed record Plan(string ClientDirectory, string Script, string Url);

    /// <summary>Registers the server for a dev-session build, once. Nothing for any other build.</summary>
    internal static void Register(IServiceCollection services, Assembly? entryAssembly)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (EditorDevSession.IsDevSessionBuild(entryAssembly))
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SpaDevServer>());
        }
    }

    /// <summary>
    ///     The dev server to run, or null when there is none to run: not an editor session, or no client the
    ///     build knew about.
    /// </summary>
    /// <param name="editorSession">Whether this is an editor-launched dev session.</param>
    /// <param name="readMetadata">Reads a value the build baked into the app assembly.</param>
    /// <param name="readFile">Reads a file's text, or null when it cannot.</param>
    internal static Plan? PlanFor(bool editorSession, Func<string, string?> readMetadata, Func<string, string?> readFile)
    {
        ArgumentNullException.ThrowIfNull(readMetadata);
        ArgumentNullException.ThrowIfNull(readFile);

        if (!editorSession || readMetadata(SpaAppBundle.ClientMetadataKey) is not { Length: > 0 } client)
        {
            return null;
        }

        return new Plan(
            client,
            DevScript.FromManifest(readFile(Path.Combine(client, "package.json"))),
            readMetadata(SpaAppBundle.DevServerMetadataKey) ?? DefaultDevServerUrl);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var entry = Assembly.GetEntryAssembly();
        var plan = PlanFor(
            EditorDevSession.IsActive(entry, _environment.IsDevelopment(), Environment.GetEnvironmentVariable),
            key => SpaAppBundle.Read(entry, key),
            ReadOrNull);

        if (plan is null)
        {
            return;
        }

        DevServerProcess process;
        try
        {
            process = DevServerProcess.Start(
                "spa-dev",
                "npm",
                ["run", plan.Script],
                plan.ClientDirectory,
                Path.Combine(_environment.ContentRootPath, "obj", "rask"),
                LogClientLine);
        }
        catch (Win32Exception)
        {
            LogNpmUnavailable();
            return;
        }

        try
        {
            LogStarting(plan.ClientDirectory, plan.Script);

            if (Uri.TryCreate(plan.Url, UriKind.Absolute, out var uri)
                && await DevServerProcess
                    .WaitForPortAsync(uri.Host, uri.Port, () => process.HasExited, TimeSpan.FromMinutes(2), stoppingToken)
                    .ConfigureAwait(false))
            {
                // Standard output, not the log: the editor watches the debug console for this exact line.
                Console.Out.WriteLine(EditorDevSession.OpenLinePrefix + plan.Url);
            }

            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The app is stopping.
        }
        finally
        {
            await process.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string? ReadOrNull(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void LogClientLine(string line) => LogClient(line);

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Client dev server: {Line}")]
    private partial void LogClient(string line);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Starting the client dev server in {Directory} (npm run {Script}).")]
    private partial void LogStarting(string directory, string script);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "npm is not available, so the client dev server did not start. The API is still running. Install Node.js from https://nodejs.org.")]
    private partial void LogNpmUnavailable();
}
