using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Core.ScopedCss;
using Rask.Server.Authentication;
using IQueryCollection = Microsoft.AspNetCore.Http.IQueryCollection;
using QueryCollection = Rask.Core.Routing.QueryCollection;
using QueryString = Rask.Core.Routing.QueryString;

namespace Rask.Server;

public static class RaskEndpointExtensions
{
    private const string RuntimePath = "/rask/rask.js";
    private const string WebSocketPath = "/rask/ws";

    private static FileSystemWatcher? _sourceWatcher;
    private static long _lastSourceChangeTicks;

    internal static TimeSpan SessionGracePeriod = TimeSpan.FromSeconds(30);

    private static readonly byte[] SessionUnknownPayload =
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "session", status = "unknown" }));

    public static IServiceCollection AddRask(this IServiceCollection services)
    {
        services.AddSingleton<LiveSessionStore>();
        services.AddSingleton<RaskLiveMarker>();
        services.AddScoped<RouteState>();
        services.AddScoped<Navigator>();
        services.AddScoped<AuthSignIn>();
        services.AddScoped<IAuthSignIn>(sp => sp.GetRequiredService<AuthSignIn>());
        services.AddSingleton<IAuthTicketStore, AuthTicketStore>();
        services.AddSingleton<IRaskRuntimeScript, ServerRuntimeScript>();

        services.AddScoped<SessionUserProvider>();
        services.AddScoped<IUserProvider>(sp => sp.GetRequiredService<SessionUserProvider>());
        services.TryAddSingleton<RaskAuthorizationOptions>();
        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddRask(
        this IServiceCollection services,
        Action<RaskAuthorizationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new RaskAuthorizationOptions();
        configure(options);
        services.AddSingleton(options);
        return services.AddRask();
    }

    public static WebApplication UseRask<TApp>(
        this WebApplication app,
        string pattern = "/{**path}")
        where TApp : Component
    {
        app.UseWebSockets();
        ((IEndpointRouteBuilder)app).UseRask<TApp>(pattern);
        return app;
    }

    public static IEndpointRouteBuilder UseRask<TApp>(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/{**path}")
        where TApp : Component
    {
        EnsureRuntimeMapped(endpoints);

        endpoints.MapGet(pattern, async (HttpContext httpContext, LiveSessionStore store) =>
        {
            var path = httpContext.Request.Path.Value ?? "/";
            var user = httpContext.User ?? new ClaimsPrincipal(new ClaimsIdentity());

            if (RouteResolver.TryResolve(path, out var chain))
            {
                var authResult = await RouteAuthorizationGuard
                    .EvaluateAsync(httpContext.RequestServices, chain, user)
                    .ConfigureAwait(false);

                switch (authResult.Outcome)
                {
                    case RouteAuthorizationOutcome.Challenge:
                        await ChallengeAsync(httpContext, authResult.AuthenticationScheme).ConfigureAwait(false);
                        return;
                    case RouteAuthorizationOutcome.Forbid:
                        await ForbidAsync(httpContext, authResult.AuthenticationScheme).ConfigureAwait(false);
                        return;
                }
            }

            var session = store.Create(sp =>
            {
                // Wrap the App in an implicit RootErrorBoundary so an uncaught render /
                // lifecycle / event-handler exception anywhere in the user's tree renders a
                // styled fallback page instead of an HTTP 500.
                var app = ActivatorUtilities.CreateInstance<TApp>(sp);
                return new RootErrorBoundary(app);
            });
            session.Services.GetRequiredService<SessionUserProvider>().Set(user);
            var routeState = session.Services.GetRequiredService<RouteState>();
            routeState.Path = path;
            routeState.Query = AdaptQuery(httpContext.Request.Query);
            var html = session.View.RenderAsLiveRoot(session.Services);
            var content = LivePayload.InjectRootAttr(html, session.Id);
            httpContext.Response.ContentType = "text/html; charset=utf-8";
            await httpContext.Response.WriteAsync(content).ConfigureAwait(false);
        });
        return endpoints;
    }

    private static Task ChallengeAsync(HttpContext ctx, string? scheme) =>
        scheme is null ? ctx.ChallengeAsync() : ctx.ChallengeAsync(scheme);

    private static Task ForbidAsync(HttpContext ctx, string? scheme) =>
        scheme is null ? ctx.ForbidAsync() : ctx.ForbidAsync(scheme);

    private static QueryCollection AdaptQuery(IQueryCollection source)
    {
        if (source.Count == 0)
        {
            return QueryCollection.Empty;
        }

        var dict = new Dictionary<string, StringValues>(source.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in source)
        {
            dict[kv.Key] = kv.Value;
        }

        return new QueryCollection(dict);
    }

    private static void EnsureRuntimeMapped(IEndpointRouteBuilder endpoints)
    {
        var marker = endpoints.ServiceProvider.GetRequiredService<RaskLiveMarker>();
        if (marker.RuntimeMapped)
        {
            return;
        }

        marker.RuntimeMapped = true;

        endpoints.Map(WebSocketPath,
            async (HttpContext ctx, LiveSessionStore store, IHostApplicationLifetime lifetime) =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                var wsUser = ctx.User ?? new ClaimsPrincipal(new ClaimsIdentity());
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    ctx.RequestAborted, lifetime.ApplicationStopping);
                await RunSocketLoop(ws, store, wsUser, linked.Token, lifetime.ApplicationStopping);
            });

        var script = LoadEmbeddedScript();
        endpoints.MapGet(RuntimePath, () => Results.Text(script, "text/javascript; charset=utf-8"));

        endpoints.MapGet("/_rask/scoped.css", static ctx => ServeScopedCssAsync(ctx));

        endpoints.MapPost("/_rask/auth/redeem",
                (HttpContext ctx, IAuthTicketStore tickets) => RedeemAuthTicketAsync(ctx, tickets))
            .DisableAntiforgery();

        var sessionStore = endpoints.ServiceProvider.GetRequiredService<LiveSessionStore>();
        ScopedCssRegistry.BundleChanged += () => _ = sessionStore.RerenderAllAsync();
        TryEnableSourceWatcher(sessionStore);
    }

    private static void TryEnableSourceWatcher(LiveSessionStore sessionStore)
    {
        if (_sourceWatcher is not null)
        {
            return;
        }

        if (Environment.GetEnvironmentVariable("DOTNET_WATCH") != "1")
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(Environment.CurrentDirectory)
            {
                IncludeSubdirectories = true,
                Filter = "*.cs",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            void Trigger()
            {
                var now = DateTime.UtcNow.Ticks;
                var prev = Interlocked.Exchange(ref _lastSourceChangeTicks, now);
                if (now - prev < TimeSpan.FromMilliseconds(250).Ticks)
                {
                    return;
                }

                _ = Task.Run(async () =>
                {
                    await Task.Delay(150).ConfigureAwait(false);
                    try { await sessionStore.RerenderAllAsync().ConfigureAwait(false); }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Rask: source-watch rerender failed: {ex.Message}");
                    }
                });
            }

            watcher.Changed += (_, _) => Trigger();
            watcher.Created += (_, _) => Trigger();
            watcher.Renamed += (_, _) => Trigger();
            _sourceWatcher = watcher;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Rask: source watcher disabled ({ex.Message})");
        }
    }

    private static async Task RunSocketLoop(WebSocket ws, LiveSessionStore store, ClaimsPrincipal wsUser,
        CancellationToken ct, CancellationToken stopping)
    {
        using var abortReg = stopping.Register(() =>
        {
            try { ws.Abort(); }
            catch { }
        });
        LiveSession? session = null;
        var buffer = new byte[16 * 1024];
        var message = new StringBuilder();

        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                message.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    if (result.Count > 0)
                    {
                        message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                } while (!result.EndOfMessage);

                var text = message.ToString();
                if (text.Length == 0)
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString()
                    : null;

                if (type == "hello")
                {
                    var sessionId = root.TryGetProperty("session", out var sid) && sid.ValueKind == JsonValueKind.String
                        ? sid.GetString()
                        : null;
                    if (sessionId is null)
                    {
                        continue;
                    }

                    session = store.Get(sessionId);
                    if (session is null)
                    {
                        await SendSessionUnknownAsync(ws, ct).ConfigureAwait(false);
                        return;
                    }

                    session.AttachSocket(ws, ct);
                    session.Services.GetRequiredService<SessionUserProvider>().Set(wsUser);
                    // Push a fresh render now that the socket is live: between the initial
                    // HTTP GET and this hello, the page may have completed an async
                    // OnMountAsync — that StateHasChanged fired with no socket
                    // attached and was silently dropped. Re-render so the client picks up
                    // the latest state. If the lifecycle is still in flight, this just
                    // re-emits the Loading view; the lifecycle's terminal StateHasChanged
                    // will then morph it into the loaded view through the live socket.
                    await session.RequestRenderAsync().ConfigureAwait(false);
                    continue;
                }

                if (session is null)
                {
                    continue;
                }

                if (session.SuppressEventsUntilReconnect)
                {
                    // Auth handoff in flight: a redeem fetch + WS reconnect are happening on
                    // the client. Drop everything except a future hello (handled above).
                    continue;
                }

                if (type == "navigate")
                {
                    await HandleNavigateAsync(session, root, ct);
                    continue;
                }

                var handlerId = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    ? idEl.GetString()
                    : null;
                if (handlerId is null)
                {
                    continue;
                }

                await session.Lock.WaitAsync(ct);
                session.InHandlerScope = true;
                try
                {
                    var navigator = session.Services.GetRequiredService<Navigator>();
                    var authSignIn = session.Services.GetRequiredService<AuthSignIn>();
                    var ticketStore = session.Services.GetRequiredService<IAuthTicketStore>();
                    try
                    {
                        using (navigator.EnterHandler())
                        using (authSignIn.EnterHandler())
                        {
                            if (await session.View.TryInvokeHandlerAsync(handlerId, root))
                            {
                                string? historyUrl = null;
                                var historyReplace = false;
                                AuthInstruction? authInstruction = null;

                                if (authSignIn.TryConsume(out var pending))
                                {
                                    var safeReturn = SanitizeReturnUrl(pending.ReturnUrl);
                                    var ticketId = ticketStore.Issue(
                                        pending.Action,
                                        pending.Principal,
                                        pending.Scheme,
                                        session.Id);
                                    authInstruction = new AuthInstruction(ticketId, safeReturn);

                                    var routeState = session.Services.GetRequiredService<RouteState>();
                                    var (path, query) = SplitUrl(safeReturn);
                                    routeState.Path = path;
                                    routeState.Query = query;
                                    historyUrl = safeReturn;
                                    historyReplace = true;
                                }

                                if (navigator.TryConsumeHistory(out var url, out var replace))
                                {
                                    if (authInstruction is null)
                                    {
                                        historyUrl = url;
                                        historyReplace = replace;
                                    }
                                }

                                await EnforceAuthAndRenderAsync(
                                        session, historyUrl, historyReplace, authInstruction)
                                    .ConfigureAwait(false);

                                if (authInstruction is not null)
                                {
                                    session.SuppressEventsUntilReconnect = true;
                                }
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Console.Error.WriteLine($"Rask Live handler '{handlerId}' threw: {ex}");
                    }
                }
                finally
                {
                    session.InHandlerScope = false;
                    session.Lock.Release();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            if (session is not null)
            {
                session.DetachSocket();
                store.ScheduleRemoval(session.Id, SessionGracePeriod);
            }

            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token); }
                catch { }
            }
        }
    }

    private static async Task SendSessionUnknownAsync(WebSocket ws, CancellationToken ct)
    {
        try
        {
            await ws.SendAsync(SessionUnknownPayload, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }
        catch { }
    }

    private static async Task HandleNavigateAsync(LiveSession session, JsonElement root, CancellationToken ct)
    {
        var navPath = root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
        if (string.IsNullOrEmpty(navPath))
        {
            return;
        }

        var navQueryString = root.TryGetProperty("query", out var q) && q.ValueKind == JsonValueKind.String
            ? q.GetString() ?? string.Empty
            : string.Empty;
        var replace = root.TryGetProperty("replace", out var rEl) && rEl.ValueKind == JsonValueKind.True;

        var fullUrl = string.IsNullOrEmpty(navQueryString)
            ? navPath
            : navQueryString.StartsWith("?", StringComparison.Ordinal)
                ? navPath + navQueryString
                : navPath + "?" + navQueryString;

        await session.Lock.WaitAsync(ct);
        session.InHandlerScope = true;
        try
        {
            var routeState = session.Services.GetRequiredService<RouteState>();
            routeState.Path = navPath;
            routeState.Query = QueryString.Parse(navQueryString);

            try
            {
                await EnforceAuthAndRenderAsync(session, fullUrl, replace).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"Rask Live navigate '{navPath}' threw: {ex}");
            }
        }
        finally
        {
            session.InHandlerScope = false;
            session.Lock.Release();
        }
    }

    private static async Task EnforceAuthAndRenderAsync(
        LiveSession session,
        string? historyUrl,
        bool replace,
        AuthInstruction? auth = null)
    {
        // When emitting an auth instruction, skip route auth re-eval: the cookie hasn't
        // landed yet on this WS, so the SessionUserProvider still holds the pre-SignIn
        // principal. The post-reconnect render does the real check with the new identity.
        if (auth is null)
        {
            var routeState = session.Services.GetRequiredService<RouteState>();
            if (RouteResolver.TryResolve(routeState.Path, out var chain))
            {
                var user = session.Services.GetRequiredService<SessionUserProvider>().Current;
                var result = await RouteAuthorizationGuard
                    .EvaluateAsync(session.Services, chain, user)
                    .ConfigureAwait(false);
                if (result.Outcome != RouteAuthorizationOutcome.Allow)
                {
                    var options = session.Services.GetRequiredService<RaskAuthorizationOptions>();
                    var originalUrl = QueryString.Build(routeState.Path, routeState.Query);
                    var redirectPath = result.Outcome == RouteAuthorizationOutcome.Forbid
                        ? options.ForbidPath
                        : options.ChallengePath;
                    routeState.Path = redirectPath;
                    if (result.Outcome == RouteAuthorizationOutcome.Challenge)
                    {
                        routeState.Query = QueryString.Parse("?returnUrl=" + Uri.EscapeDataString(originalUrl));
                        historyUrl = redirectPath + "?returnUrl=" + Uri.EscapeDataString(originalUrl);
                    }
                    else
                    {
                        routeState.Query = QueryCollection.Empty;
                        historyUrl = redirectPath;
                    }

                    replace = true;
                }
            }
        }

        await session.RenderAndSendAsync(historyUrl, replace, auth).ConfigureAwait(false);
    }

    private static (string Path, QueryCollection Query) SplitUrl(string url)
    {
        var idx = url.IndexOf('?');
        if (idx < 0)
        {
            return (url, QueryCollection.Empty);
        }

        var path = url[..idx];
        var query = QueryString.Parse(url[idx..]);
        return (path, query);
    }

    private static async Task RedeemAuthTicketAsync(HttpContext ctx, IAuthTicketStore tickets)
    {
        string? ticketId = null;
        string? sessionId = null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("ticket", out var t) && t.ValueKind == JsonValueKind.String)
            {
                ticketId = t.GetString();
            }

            if (doc.RootElement.TryGetProperty("session", out var s) && s.ValueKind == JsonValueKind.String)
            {
                sessionId = s.GetString();
            }
        }
        catch (JsonException)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (string.IsNullOrEmpty(ticketId) || string.IsNullOrEmpty(sessionId))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!tickets.TryRedeem(ticketId, sessionId, out var ticket))
        {
            ctx.Response.StatusCode = StatusCodes.Status410Gone;
            return;
        }

        var scheme = await ResolveAuthSchemeAsync(ctx.RequestServices, ticket.Scheme).ConfigureAwait(false);
        if (ticket.Action == AuthAction.SignIn)
        {
            await ctx.SignInAsync(scheme, ticket.Principal!).ConfigureAwait(false);
        }
        else
        {
            await ctx.SignOutAsync(scheme).ConfigureAwait(false);
        }

        ctx.Response.StatusCode = StatusCodes.Status200OK;
    }

    private static async Task<string> ResolveAuthSchemeAsync(IServiceProvider services, string? explicitScheme)
    {
        if (!string.IsNullOrEmpty(explicitScheme))
        {
            return explicitScheme;
        }

        var provider = services.GetService<IAuthenticationSchemeProvider>();
        if (provider is not null)
        {
            var resolved = await provider.GetDefaultSignInSchemeAsync().ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved.Name;
            }
        }

        return CookieAuthenticationDefaults.AuthenticationScheme;
    }

    internal static string SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            return "/";
        }

        if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }

    internal static Task ServeScopedCssAsync(HttpContext ctx)
    {
        var (css, hash) = ScopedCssRegistry.GetBundle();
        var etag = $"\"{hash}\"";
        if (string.Equals(ctx.Request.Headers.IfNoneMatch.ToString(), etag, StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = StatusCodes.Status304NotModified;
            return Task.CompletedTask;
        }

        ctx.Response.ContentType = "text/css; charset=utf-8";
        ctx.Response.Headers.ETag = etag;
        ctx.Response.Headers.CacheControl = "no-cache";
        return ctx.Response.WriteAsync(css);
    }

    private static string LoadEmbeddedScript()
    {
        var asm = typeof(RaskEndpointExtensions).Assembly;
        var name = asm.GetManifestResourceNames()
                       .FirstOrDefault(n => n.EndsWith("rask.js", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException("rask.js embedded resource not found.");
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class ServerRuntimeScript : IRaskRuntimeScript
    {
        public Component Render() => Tags.Script(RuntimePath);
    }

    internal sealed class RaskLiveMarker
    {
        public bool RuntimeMapped;
    }
}
