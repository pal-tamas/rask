using Rask.Cli.Dev;
using Rask.Hosting.Shared;

namespace Rask.Cli.Commands;

internal sealed partial class DevCommand
{
    /// <summary>
    ///     Starts the front end's own dev server for a SPA-hosted app, or does nothing for anything else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two processes rather than one because the browser talks to the bundler, not to ASP.NET: that
    ///         is what makes HMR native and instant, and it is why the scaffolded <c>vite.config.ts</c>
    ///         proxies <c>/_rask</c> back to the host. In production neither of those exists — the host
    ///         serves the built bundle and answers the wire itself.
    ///     </para>
    ///     <para>
    ///         A failure here is reported and then let go. The host is the process this command is really
    ///         running, and killing it because a bundler would not start would take away the API too — and
    ///         with it any chance of reading the error against a working server.
    ///     </para>
    /// </remarks>
    private async Task StartClientDevServer(DevTarget target, CancellationToken cancellationToken)
    {
        if (target.Kind != DevTemplateKind.SpaHosted)
        {
            return;
        }

        if (target.ClientDirectory is not { } directory)
        {
            Console.WriteLine(
                "No client directory found beside this host, so no dev server was started. Run the "
                + "bundler yourself, or point RaskSpaClientDir at it.",
                ConsoleStyle.Dim);
            return;
        }

        try
        {
            // A dev session skips the build that would have installed them, so a fresh scaffold or clone
            // arrives here with no node_modules and `npm run dev` would not find its bundler.
            if (!_fileSystem.DirectoryExists(Path.Combine(directory, "node_modules")))
            {
                var install = _fileSystem.FileExists(Path.Combine(directory, "package-lock.json")) ? "ci" : "install";
                Console.WriteLine($"Installing the client's dependencies (npm {install})…", ConsoleStyle.Dim);
                if (await _process.RunAsync("npm", [install], directory, cancellationToken).ConfigureAwait(false) != 0)
                {
                    Console.WriteErrorLine(
                        $"npm {install} failed, so the client dev server did not start. The API is still running.",
                        ConsoleStyle.Error);
                    return;
                }
            }

            Console.WriteLine(
                $"Starting the client dev server in {Path.GetFileName(directory)} "
                + $"(npm run {target.ClientDevScript ?? "dev"})…",
                ConsoleStyle.Dim);

            await _process
                .RunAsync("npm", ["run", target.ClientDevScript ?? "dev"], directory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host exited and took this with it. Expected, every time.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.WriteErrorLine(
                "npm is not available, so the client dev server did not start. The API is still running. "
                + "Install Node.js from https://nodejs.org.",
                ConsoleStyle.Error);
        }
    }

    /// <summary>
    ///     Serves this project's islands from a Vite dev server, so editing a <c>.tsx</c> or a
    ///     <c>.svelte</c> hot-replaces instead of rebuilding.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without this an island edit goes through the whole MSBuild path: <c>dotnet watch</c> sees
    ///         the file, rebuilds, and runs a production <c>vite build</c> over every island in the
    ///         project. Correct, and far too slow to work in — and the page reloads, so whatever state
    ///         the island held is gone.
    ///     </para>
    ///     <para>
    ///         The config it runs is the one the BUILD generated, which is why this waits for it rather
    ///         than computing a path: only MSBuild knows which configuration and target framework were
    ///         chosen, and the pointer file it drops at the stable <c>obj/rask-external/</c> path is how
    ///         it says so. Waiting is also correct on the first run of a clean clone, where the config
    ///         does not exist until the build that is starting right now has written it.
    ///     </para>
    ///     <para>
    ///         A failure here is reported and let go, exactly as for the SPA client: the host is the
    ///         process this command is really running, and losing hot reload is not a reason to take the
    ///         app down with it.
    ///     </para>
    /// </remarks>
    private async Task StartIslandDevServer(DevTarget target, CancellationToken cancellationToken)
    {
        if (!target.HasIslands)
        {
            return;
        }

        var config = await IslandDevPointer
            .WaitAsync(target.ProjectDirectory, TimeSpan.FromMinutes(3), cancellationToken)
            .ConfigureAwait(false);
        if (config is null)
        {
            return;
        }

        Console.WriteLine($"Serving islands from {config.Value.Url} (hot reload)…", ConsoleStyle.Dim);

        try
        {
            await _process
                .RunAsync(
                    "npx",
                    ["--no-install", "vite", "--config", config.Value.Config],
                    target.ProjectDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host exited and took this with it. Expected, every time.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.WriteErrorLine(
                "npx is not available, so the island dev server did not start. The app is still running, "
                + "but islands will not hot-reload. Install Node.js from https://nodejs.org.",
                ConsoleStyle.Error);
        }
    }

    /// <summary>
    ///     Set the machine up to serve this project on <c>https://&lt;name&gt;.test</c>, or return null to
    ///     use the ordinary localhost URLs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only on the lanes where Kestrel is what the browser talks to. On the SPA and meta lanes
    ///         the page is served by the front end's own dev server over plain HTTP, so a certificate on
    ///         the ASP.NET host behind it is not the one the browser would ever see; and an island
    ///         project loads its modules from a second HTTP dev server, which an HTTPS page is not
    ///         allowed to do at all (mixed content). Both need their bundler taught to serve TLS before
    ///         this can cover them — until then they keep the localhost URL that does work.
    ///     </para>
    ///     <para>
    ///         Skipped whenever <c>--urls</c> is given: someone naming the addresses to listen on is
    ///         being explicit, and quietly serving somewhere else would be the opposite of helpful.
    ///     </para>
    /// </remarks>
    private async Task<DevHostResult?> TryPrepareDevHostAsync(
        DevTarget target,
        ParsedArguments parsed,
        bool nonInteractive,
        CancellationToken cancellationToken)
    {
        if (parsed.HasFlag("no-host")
            || parsed.Option("urls") is { Length: > 0 }
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RASK_DEV_NO_HOST"))
            || !DevHostSetup.IsSupported)
        {
            return null;
        }

        if (target.Kind is not (DevTemplateKind.Server or DevTemplateKind.WasmHosted or DevTemplateKind.Unknown))
        {
            return null;
        }

        if (target.HasIslands)
        {
            return null;
        }

        var setup = new DevHostSetup(Console, _process, new DevHostStore(DevHostStore.DefaultRoot));
        return await setup.TryPrepareAsync(target.Name, !nonInteractive, cancellationToken).ConfigureAwait(false);
    }
}
