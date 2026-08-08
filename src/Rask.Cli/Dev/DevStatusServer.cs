using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Rask.Cli.Dev;

/// <summary>
///     A tiny loopback endpoint, owned by <c>rask dev</c>, that answers "is the app down because it is
///     broken, or because it is restarting?".
/// </summary>
/// <remarks>
///     <para>
///         This is the whole reason #603 could not be a live-protocol frame. The existing out-of-band
///         frames (<c>hotReload</c>, <c>shutdown</c>) are broadcast <em>by the app</em>; when a rebuild
///         fails the app process is <b>down</b>, so there is nothing left to send — which is precisely why
///         the browser reports a network problem. The signal has to come from something that outlives the
///         app, and the only such thing on the machine is <c>rask dev</c> itself.
///     </para>
///     <para>
///         Loopback only, and dev only. It binds <c>127.0.0.1</c> on an OS-assigned port (never a fixed
///         one — see <c>LoopbackPort</c> in the E2E suite for what fixed ports cost) and serves one
///         read-only document. It allows any origin because the app is on a different port and the browser
///         treats that as cross-origin; that is safe here and only here, because nothing reachable is
///         secret and nothing is writable.
///     </para>
/// </remarks>
internal sealed class DevStatusServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly DevBuildWatcher _watcher;
    private readonly CancellationTokenSource _stopping = new();

    private DevStatusServer(DevBuildWatcher watcher, int port)
    {
        _watcher = watcher;
        Port = port;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    /// <summary>The port it is listening on.</summary>
    public int Port { get; }

    /// <summary>The URL the browser polls.</summary>
    public string Url => $"http://127.0.0.1:{Port}/status";

    /// <summary>
    ///     Starts a status server, or returns <c>null</c> when one could not be bound.
    /// </summary>
    /// <remarks>
    ///     Failure is never fatal: this is a development affordance, and a machine that won't hand out a
    ///     loopback port should still be able to run <c>rask dev</c>. The caller carries on without it and
    ///     the browser falls back to the reconnect overlay it showed before.
    /// </remarks>
    public static DevStatusServer? TryStart(DevBuildWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            int port;
            try
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
            }
            catch (SocketException)
            {
                return null;
            }

            var server = new DevStatusServer(watcher, port);
            try
            {
                server._listener.Start();
                _ = server.ServeAsync();
                return server;
            }
            catch (HttpListenerException)
            {
                // The probe released the port before HttpListener claimed it, and something took it in
                // between. Another candidate costs nothing.
                server.Dispose();
            }
        }

        return null;
    }

    private async Task ServeAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_stopping.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            try
            {
                Respond(context);
            }
            catch (Exception)
            {
                // A dropped poll is not worth a word: the client polls again in a moment, and `rask dev`'s
                // console belongs to the build output, not to this.
            }
        }
    }

    private void Respond(HttpListenerContext context)
    {
        var response = context.Response;

        // The app runs on a different port, so every poll is cross-origin. Safe here and only here: this
        // is 127.0.0.1, read-only, and carries build errors the developer is already looking at.
        response.AddHeader("Access-Control-Allow-Origin", "*");
        response.AddHeader("Cache-Control", "no-store");

        if (context.Request.HttpMethod == "OPTIONS")
        {
            response.AddHeader("Access-Control-Allow-Methods", "GET");
            response.StatusCode = 204;
            response.Close();
            return;
        }

        var body = Encoding.UTF8.GetBytes(_watcher.ToJson());
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
        response.Close();
    }

    public void Dispose()
    {
        _stopping.Cancel();
        try { _listener.Stop(); }
        catch
        {
            /* already stopped */
        }

        try { _listener.Close(); }
        catch
        {
            /* already closed */
        }

        _stopping.Dispose();
    }
}
