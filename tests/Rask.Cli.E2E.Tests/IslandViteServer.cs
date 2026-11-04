using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Rask.Hosting.Shared;

namespace Rask.Cli.E2E.Tests;

/// <summary>
///     The islands' Vite dev server, started exactly as <c>rask dev</c> starts it: from the pointer the
///     dev-session build drops, with <see cref="IslandDevCommand" />'s command line.
/// </summary>
internal sealed partial class IslandViteServer : IAsyncDisposable
{
    private readonly StringBuilder _log = new();
    private readonly object _logLock = new();
    private readonly Process _process;

    private IslandViteServer(Process process, Uri origin)
    {
        _process = process;
        Origin = origin;
        Http = new HttpClient { BaseAddress = origin };
    }

    /// <summary>Where the dev server listens: the URL the build baked into the manifest and the pointer.</summary>
    public Uri Origin { get; }

    public HttpClient Http { get; }

    public string Log
    {
        get { lock (_logLock) { return "\n--- island dev server output ---\n" + _log; } }
    }

    /// <summary>
    ///     Waits for the build to name the dev server's config, starts it, and waits until it serves its HMR client;
    ///     null when the build never named one before the timeout or <paramref name="cancellationToken" />.
    /// </summary>
    public static async Task<IslandViteServer?> StartAsync(
        string projectDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var pointer = await IslandDevPointer.WaitAsync(projectDirectory, timeout, cancellationToken);
        if (pointer is null)
        {
            return null;
        }

        var psi = new ProcessStartInfo(IslandDevCommand.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = projectDirectory,
        };
        foreach (var argument in IslandDevCommand.Arguments(pointer.Value.Config))
        {
            psi.ArgumentList.Add(argument);
        }

        psi.Environment["CI"] = "true";
        psi.Environment["NO_COLOR"] = "1";

        var server = new IslandViteServer(Process.Start(psi)!, new Uri(pointer.Value.Url));
        _ = server.DrainAsync(server._process.StandardOutput);
        _ = server.DrainAsync(server._process.StandardError);

        try
        {
            Assert.True(
                await server.ServesAsync("/@vite/client", TimeSpan.FromSeconds(90)),
                $"the island dev server never answered on {server.Origin}.{server.Log}");
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     A socket on the dev server's HMR channel, connected the way <c>@vite/client</c> connects one, and
    ///     past Vite's <c>connected</c> greeting.
    /// </summary>
    /// <remarks>
    ///     The token is read off the served <c>@vite/client</c>, which is where a browser gets it: Vite refuses
    ///     an HMR socket without it, so a page on another origin cannot listen in on a developer's edits.
    /// </remarks>
    public async Task<ClientWebSocket> ConnectHmrAsync(WatchApp app, string pageOrigin)
    {
        var client = await Http.GetStringAsync("/@vite/client").ConfigureAwait(false);
        var token = WsToken().Match(client);
        var query = token.Success ? "?token=" + Uri.EscapeDataString(token.Groups["token"].Value) : string.Empty;

        var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("vite-hmr");
        socket.Options.SetRequestHeader("Origin", pageOrigin);
        await socket.ConnectAsync(new Uri($"ws://{Origin.Authority}/{query}"), CancellationToken.None)
            .ConfigureAwait(false);

        await app.ReadFramesUntilAsync(
            socket, f => f.Contains("\"connected\"", StringComparison.Ordinal), TimeSpan.FromSeconds(30));
        return socket;
    }

    /// <summary>Polls until <paramref name="path" /> answers 200, or the server exits, or the timeout elapses.</summary>
    private async Task<bool> ServesAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !_process.HasExited)
        {
            try
            {
                using var response = await Http.GetAsync(path).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(300).ConfigureAwait(false);
        }

        return false;
    }

    private async Task DrainAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                lock (_logLock)
                {
                    _log.AppendLine(line);
                }
            }
        }
        catch (Exception)
        {
            // The process went away; the log has whatever it managed to say.
        }
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        try
        {
            if (!_process.HasExited)
            {
                // The tree: npx runs Vite as a child, and a leaked one holds its strictPort for good.
                _process.Kill(entireProcessTree: true);
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Already gone.
        }

        _process.Dispose();
    }

    [GeneratedRegex("""wsToken\s*=\s*"(?<token>[^"]+)"\s*;""")]
    private static partial Regex WsToken();
}
