using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rask.Cqrs.Client;
using Rask.Cqrs.Server;

namespace Rask.Cqrs.Transport.Tests;

/// <summary>
///     One client transport and one server endpoint, joined by real HTTP: the client builds the request,
///     the server's own routing, authentication and parsing receive it, and the answer is decoded back
///     by the client. Nothing between them is stood in for.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why the client is constructed rather than registered.</b> <c>AddRaskCqrsClient()</c> installs
///         its remote invokers into <see cref="CqrsRegistry" />, which is <em>process-wide and one-way</em>
///         — and the server endpoint reaches its handler through <c>contract.LocalInvoker</c>, which
///         dispatches through that same registry. Calling it here would therefore make the server send
///         every message it received straight back out again. A client process and a server process are
///         two processes in every real deployment; this harness keeps that separation by giving the client
///         half its own <see cref="IServiceProvider" /> holding the transport, and handing the generated
///         <c>contract.Invoker</c> that provider — which is the exact delegate <c>AddRaskCqrsClient()</c>
///         registers, called the exact way <c>IDispatcher</c> calls it.
///     </para>
///     <para>
///         <b>What this cannot prove.</b> The wire codecs are generated once per compilation, so both
///         halves here share one copy — the same arrangement the one-project <c>rask new --wasm</c> build
///         produces, and a faithful one for a two-project app since the same generator emits both. A codec
///         change that alters the encoding on both sides symmetrically stays invisible to this project, as
///         it does to a real deployment. What it does prove is that the request one half <em>builds</em> is
///         the request the other half <em>accepts</em>: the route, the verb, the headers, the multipart
///         layout, the upload protocol, and the failure mapping — none of which is shared code.
///     </para>
/// </remarks>
internal sealed class Wire : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly ServiceProvider _clientServices;

    private Wire(
        IHost host,
        HttpClient http,
        RecordingHandler recorder,
        RaskCqrsClientOptions options)
    {
        _host = host;
        Http = http;
        Recorder = recorder;

        var services = new ServiceCollection();
        services.AddSingleton<IRemoteDispatch>(new RemoteDispatch(http, options));
        _clientServices = services.BuildServiceProvider();
        Transport = _clientServices.GetRequiredService<IRemoteDispatch>();
    }

    /// <summary>The client half's <c>HttpClient</c> — a browser app's own-origin one, in effect.</summary>
    public HttpClient Http { get; }

    /// <summary>Every request the client actually put on the wire, in order.</summary>
    public RecordingHandler Recorder { get; }

    public IRemoteDispatch Transport { get; }

    public Ledger Ledger => _host.Services.GetRequiredService<Ledger>();

    /// <summary>
    ///     Boots the pair.
    /// </summary>
    /// <param name="user">The signed-in caller, attached to every request the way a bearer token would be.</param>
    /// <param name="roles">Roles that caller holds.</param>
    /// <param name="configureServer">Endpoint options.</param>
    /// <param name="configureClient">Transport options.</param>
    public static Wire Connect(
        string? user = "tester",
        string? roles = null,
        Action<RaskCqrsServerOptions>? configureServer = null,
        Action<RaskCqrsClientOptions>? configureClient = null)
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddLogging(b => b.ClearProviders());
                services.AddRouting();
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", static _ => { });
                services.AddAuthorization();
                services.AddSingleton<Ledger>();
                services.AddRaskCqrsServer(configureServer);
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(e => e.MapRaskCqrs());
            });
        });

        var host = builder.Start();
        var server = host.GetTestServer();

        var recorder = new RecordingHandler { InnerHandler = server.CreateHandler() };

        // BaseAddress set the way a browser app's registration sets it: the page's own origin. The
        // transport builds a rooted path and lets the client resolve it, so this is the piece that
        // decides where "/_rask/cqrs/request/…" actually goes.
        var http = new HttpClient(recorder) { BaseAddress = server.BaseAddress };

        var options = new RaskCqrsClientOptions
        {
            // The credential hook, used for what it is for: the server authenticates from a header, so a
            // test states the identity it means and the transport carries it on every request — chunk
            // requests included, which is what an upload session's owner is derived from.
            ConfigureRequestAsync = (request, _) =>
            {
                if (user is not null)
                {
                    request.Headers.TryAddWithoutValidation("X-Test-User", user);
                }

                if (roles is not null)
                {
                    request.Headers.TryAddWithoutValidation("X-Test-Role", roles);
                }

                return Task.CompletedTask;
            },
        };

        configureClient?.Invoke(options);

        return new Wire(host, http, recorder, options);
    }

    /// <summary>
    ///     Dispatches exactly the way a client app's <c>IDispatcher</c> does — through the invoker the
    ///     generator emitted and <c>AddRaskCqrsClient()</c> installs.
    /// </summary>
    public async Task<TResult> SendAsync<TResult>(object message, CancellationToken cancellationToken = default)
    {
        var contract = Contract(message);
        Assert.NotNull(contract.Invoker);
        return await (Task<TResult>)contract.Invoker!(_clientServices, message, cancellationToken);
    }

    /// <summary>The void-command shape: the invoker answers a bare <see cref="Task" />.</summary>
    public async Task SendAsync(object message, CancellationToken cancellationToken = default)
    {
        var contract = Contract(message);
        Assert.NotNull(contract.Invoker);
        await contract.Invoker!(_clientServices, message, cancellationToken);
    }

    /// <summary>The notification shape: no invoker is generated, so a transport publishes directly.</summary>
    public Task PublishAsync(object notification, CancellationToken cancellationToken = default) =>
        Transport.PublishAsync(Contract(notification), notification, cancellationToken);

    public static RemoteContract Contract(object message)
    {
        Assert.True(
            RemoteContractRegistry.TryGet(message.GetType(), out var contract),
            $"no wire contract was generated for {message.GetType()}");

        return contract!;
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _clientServices.DisposeAsync();
        _host.Dispose();
    }

    /// <summary>
    ///     Keeps what the client sent, so a test can assert the shape as well as the outcome — and can
    ///     make one upload chunk vanish, which is the only way to reach the resume path from outside.
    /// </summary>
    internal sealed class RecordingHandler : DelegatingHandler
    {
        private readonly List<(HttpMethod Method, Uri Uri, string? ContentType)> _sent = [];
        private int _chunks;

        public IReadOnlyList<(HttpMethod Method, Uri Uri, string? ContentType)> Sent
        {
            get
            {
                lock (_sent)
                {
                    return _sent.ToArray();
                }
            }
        }

        public (HttpMethod Method, Uri Uri, string? ContentType) Last => Sent[^1];

        /// <summary>How many upload-chunk requests were sent, dropped ones included.</summary>
        public int Chunks => Volatile.Read(ref _chunks);

        /// <summary>
        ///     The 1-based chunk request to answer <c>200</c> without forwarding — a chunk lost between
        ///     the two halves, which the client cannot tell from one that landed.
        /// </summary>
        public int? DropChunk { get; set; }

        /// <summary>True once <see cref="DropChunk" /> has actually been spent.</summary>
        public bool Dropped { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Recorded before sending: the request is disposed by the time the caller sees the answer,
            // and a MultipartFormDataContent's headers go with it.
            lock (_sent)
            {
                _sent.Add((request.Method, request.RequestUri!, request.Content?.Headers.ContentType?.MediaType));
            }

            var isChunk = request.RequestUri!.AbsolutePath.EndsWith(
                "/" + RemoteEndpointDefaults.UploadSegment, StringComparison.Ordinal);

            if (isChunk && Interlocked.Increment(ref _chunks) == DropChunk)
            {
                Dropped = true;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { RequestMessage = request };
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    ///     Authenticates from a request header, so a test states the identity it means inline rather than
    ///     through a sign-in flow that has nothing to do with what is under test.
    /// </summary>
    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.Name, user.ToString()) };
            if (Request.Headers.TryGetValue("X-Test-Role", out var role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            }

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test")));
        }
    }
}

/// <summary>The route a message name resolves to, spelled from the shared constants rather than typed.</summary>
internal static class WireAssert
{
    public static void Path(Uri uri, string messageName) =>
        Assert.Equal(
            RemoteEndpointDefaults.RoutePrefix + "/" + Uri.EscapeDataString(messageName),
            uri.AbsolutePath);
}
