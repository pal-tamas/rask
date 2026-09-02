using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace Rask.Meta.Hosting;

/// <summary>
///     Forwards a request Kestrel did not answer itself to the framework's Node server on loopback.
/// </summary>
/// <remarks>
///     <para>
///         The handler below is configured for proxying rather than for calling an API, and each
///         departure from the defaults matters. Redirects are not followed and cookies are not stored,
///         because both belong to the browser and handling them here would silently rewrite the
///         visitor's session. Decompression is off so a compressed response streams through untouched
///         rather than being inflated and re-deflated.
///     </para>
///     <para>
///         <see cref="IHttpForwarder" /> keeps the response UNBUFFERED, which is the property the whole
///         lane depends on: React Server Components and streaming SSR send a response over many
///         flushes, and a proxy that waits for the end turns a streaming page into a slow blank one
///         without failing a single test.
///     </para>
/// </remarks>
internal sealed partial class NodeForwarder : IDisposable
{
    private readonly string _destination;
    private readonly IHttpForwarder _forwarder;
    private readonly HttpMessageInvoker _invoker;
    private readonly ILogger<NodeForwarder> _logger;
    private readonly MetaHostingOptions _options;
    private readonly NodeReadiness _readiness;
    private readonly StaticAssets _static;
    private readonly ForwarderRequestConfig _requestConfig;
    private readonly ForwarderRequestConfig _upgradeConfig;

    // Public on an internal type: the container resolves only public constructors, and this one is
    // never visible outside the assembly because the type is not.
    public NodeForwarder(
        MetaHostingOptions options,
        MetaPaths paths,
        NodeReadiness readiness,
        IHttpForwarder forwarder,
        ILogger<NodeForwarder> logger)
    {
        _options = options;
        _readiness = readiness;
        _static = new StaticAssets(options.Framework, paths.AppDirectory);
        _forwarder = forwarder;
        _logger = logger;
        _destination = string.Create(
            CultureInfo.InvariantCulture,
            $"http://127.0.0.1:{options.Port}/");

        _invoker = new HttpMessageInvoker(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
        });

        _requestConfig = new ForwarderRequestConfig
        {
            // Long enough for a slow first render and for a streamed response that pauses between
            // flushes; short enough that a wedged renderer does not hold the connection for ever.
            ActivityTimeout = TimeSpan.FromSeconds(100),
        };

        // An upgraded connection gets no idle cap. ActivityTimeout is an IDLE timeout that applies to
        // WebSockets too, so the ordinary one would tear down any socket quiet for 100 seconds — a
        // chat with nobody typing, a notification channel between pings — and the client would see a
        // disconnect with no cause. Liveness on an upgraded connection is the application's job, and
        // it has the ping frames to do it with.
        _upgradeConfig = new ForwarderRequestConfig { ActivityTimeout = null };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _invoker.Dispose();
        _static.Dispose();
    }

    /// <summary>Forwards one request, or refuses it while the front end is still starting.</summary>
    internal async Task ForwardAsync(HttpContext context)
    {
        // Assets first, and before the readiness gate on purpose: they are files on disk and do not
        // need Node to be up. A page served while the front end is restarting still finds its CSS.
        if (await _static.TryServeAsync(context).ConfigureAwait(false))
        {
            return;
        }

        if (!_readiness.IsReady)
        {
            await WriteStartingAsync(context).ConfigureAwait(false);
            return;
        }

        var config = context.WebSockets.IsWebSocketRequest ? _upgradeConfig : _requestConfig;

        var error = await _forwarder
            .SendAsync(context, _destination, _invoker, config)
            .ConfigureAwait(false);

        // No HasStarted guard on the LOGGING. A failure part-way through a streamed response — the
        // Node process dying mid-render — is the one the visitor actually sees, as a page that stops
        // half-written, and gating the log on "nothing was sent yet" is precisely what would leave
        // that case recorded nowhere.
        if (error != ForwarderError.None)
        {
            var reason = context.GetForwarderErrorFeature()?.Exception?.Message ?? error.ToString();
            LogForwardFailed(context.Request.Path.ToString(), _options.Framework.Name, reason);
        }
    }

    /// <summary>
    ///     Answers 503 while the Node process is not accepting connections.
    /// </summary>
    /// <remarks>
    ///     A 503 with <c>Retry-After</c> rather than forwarding into a closed port and letting it
    ///     surface as a 502: this state is normal for the first seconds of a container's life, and it
    ///     is genuinely temporary, so it should say so in the one way clients and orchestrators
    ///     already understand.
    /// </remarks>
    private static async Task WriteStartingAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "1";
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("The front end is still starting.").ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error,
        Message = "Forwarding {Path} to {Framework} failed: {Reason}")]
    private partial void LogForwardFailed(string path, string framework, string reason);
}
