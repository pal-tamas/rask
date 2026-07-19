# Authentication in Rask

> **In practice:** [Tutorial Ch 3](tutorial/03-orders-and-auth.md) · recipe [require login on a page](recipes.md#require-login-on-a-page) · [cheat sheet](cheatsheet.md).

Rask ships the *plumbing* for authentication — a scoped current-user, a sign-in/out handshake, route
guards, and a declarative gate — and lets you bring any backing store (a cookie, a JWT, ASP.NET Identity,
Keycloak/OIDC, …). This guide shows the complete, copy-pasteable flow for each combination.

- [Concepts](#concepts)
- [Configuration](#configuration)
- [Declarative gating — the `Authorize` component](#declarative-gating)
- [Cookie + Server](#cookie--server)
- [Cookie + WASM](#cookie--wasm)
- [JWT + Server](#jwt--server)
- [JWT + WASM](#jwt--wasm)
- [Standalone WASM (no host)](#standalone-wasm-no-host)
- [ASP.NET Identity](authentication-providers.md#aspnet-identity)
- [Keycloak / OpenID Connect](authentication-providers.md#keycloak--openid-connect)
- [Other OIDC providers — Auth0, AWS Cognito, Duende IdentityServer](authentication-providers.md#other-oidc-providers)
- [Hardening reference](authentication-hardening.md#hardening-reference)
- [Security checklist](authentication-hardening.md#security-checklist)
- [Decision table](#decision-table)

---

## Concepts

| Piece | What it is |
|---|---|
| `IUserProvider` | Scoped source of the current `ClaimsPrincipal` (`Current`), a `Changed` event, optional `EnsureLoadedAsync`/`RefreshAsync`, and `IsLoading`. Server: `SessionUserProvider` (seeded from `HttpContext.User`). WASM: you supply one (or the anonymous default). |
| Injecting `IUserProvider` | Inject it via the constructor and read `.Current` — the never-null `ClaimsPrincipal` for the active render scope. Gate in `Render()` on `provider.Current.Identity?.IsAuthenticated` / `provider.Current.IsInRole(...)`. |
| `Authorize` component | Headless declarative gate with `Authorized` / `NotAuthorized` / `Authorizing` slots (see below). |
| `IAuthSignIn` | Event-handler-only `SignInAsync(principal, returnUrl)` / `SignOutAsync(returnUrl)`. Server drives the cookie handshake; WASM signs out via `/auth/logout`. |
| `[Authorize]` / `[AllowAnonymous]` | Route-level gating evaluated by `RouteAuthorizationGuard` → redirect to the auth scheme's `LoginPath` (401) or `AccessDeniedPath` (403). |

**The Server cookie handshake.** A WebSocket can't write a `Set-Cookie`, so sign-in is a four-step relay:
`IAuthSignIn.SignInAsync(principal)` (in an event handler) → the framework issues a single-use,
session-bound ticket → the browser `POST`s it to `/_rask/auth/redeem` → the endpoint calls
`HttpContext.SignInAsync` (sets the cookie) → the WS reconnects and re-seeds `SessionUserProvider` from the
now-authenticated `HttpContext.User`. You never touch this directly — just call `SignInAsync`.

---

## Configuration

**Rask has no auth options object of its own** — authentication is configured entirely through ASP.NET's
own primitives. You set the cookie name/flags/expiry and login path on `AddCookie(...)`, the JWT signing key
and token lifetimes on `AddJwtBearer(...)`, and roles/policies on `AddAuthorization(...)`. `AddRask()` takes
no auth configuration.

A few framework defaults are fixed (not configurable knobs):

| Behaviour | Value |
|---|---|
| Initial HTTP GET challenge / forbid | the configured auth scheme's own `LoginPath` / `AccessDeniedPath` (e.g. on `AddCookie`) |
| Client-side route-guard redirect (an in-app nav to a protected route) | `/login` / `/forbidden` — name your login route `/login` to match |
| Sign-in/out redeem ticket lifetime | 30 seconds |

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";          // ← where unauthenticated users are challenged (HTTP GET)
        o.AccessDeniedPath = "/forbidden";
        o.Cookie.Name = "rask.auth";
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    });
builder.Services.AddRask();              // no auth config here — it's all on AddCookie/AddJwtBearer
```

---

## Declarative gating

The headless `Authorize` component renders exactly one of three slots — no markup of its own — off
the current user (`IUserProvider`):

```csharp
// Shorthand: children are the "authorized" branch (static content, no principal needed).
Authorize(Roles: ["admin"])[ AdminPanel() ]

// Full three-slot form. `Authorized` is a delegate handed the current principal (Blazor's
// @context.User), so a greeting reads the name with no injected IUserProvider and no subscription.
Authorize(
    Roles: ["admin", "editor"],                       // ANY-of; omit for "any authenticated user"
    Authorized:    user => Div(Class: "card")[ $"Welcome, {user.Identity!.Name}" ],
    NotAuthorized: A(Href: "/login")[ "Please sign in" ],
    Authorizing:   Spinner())                     // shown while the principal/policy resolves
```

- **`Authorized`** is `Func<ClaimsPrincipal, Component>` — it receives the signed-in principal and re-runs
  whenever the gate re-renders (i.e. on `IUserProvider.Changed`), so user-dependent markup stays fresh
  on its own. For static authorized content that ignores the user, use the children-indexer shorthand
  `Authorize(...)[ content ]`.
- **`Roles`** and the authenticated check are synchronous → no flicker.
- **`Policy`** (e.g. `Authorize(Policy: "over-18")`) resolves via `IAuthorizationService` in the background;
  the `Authorizing` slot shows until it lands.
- **`Authorizing`** also covers the WASM bootstrap window: while a provider's `EnsureLoadedAsync`/`RefreshAsync`
  is in flight (`IUserProvider.IsLoading == true`), the slot bridges the anonymous→authenticated flash.

Use `Authorize` for *content* gating; use `[Authorize]` on a page for *route* gating; inject `IUserProvider`
and read `.Current` directly when you need imperative logic.

The imperative form, live — gate in `Render()` on the current user (sign in / out to flip the branch):

<!-- demo:auth-user-gate -->

And the declarative `Authorize` component, live — sign in as *user* or *admin* to switch between the
`NotAuthorized`, `Authorized`, and role-gated slots:

<!-- demo:auth-authorize -->

---

## Cookie + Server

The lowest-friction, most secure option for the Server (WS) host — the token lives in an HttpOnly cookie and
never reaches JavaScript.

> **Scaffold it:** `rask new MyApp --auth` generates exactly this — a `/login` form, a
> `DemoCredentialStore`, a protected `/members` page, and the `AddCookie` + `UseAuthentication` wiring below.
> A runnable reference also lives in `samples/Rask.Example.Auth`.

**A credential store (demo — swap for your real one):**

```csharp
public interface ICredentialStore
{
    IReadOnlyList<Claim>? Validate(string username, string password);
}

public sealed class DemoCredentialStore : ICredentialStore
{
    public IReadOnlyList<Claim>? Validate(string username, string password) =>
        (username, password) switch
        {
            ("alice", "password") => [new Claim(ClaimTypes.Name, "alice"), new Claim(ClaimTypes.Role, "user")],
            ("root",  "password") => [new Claim(ClaimTypes.Name, "root"),  new Claim(ClaimTypes.Role, "admin")],
            _ => null
        };
}
```

**The login page** — a normal Rask page; `SignInAsync` runs inside the form's submit handler:

```csharp
[Route("login")]
[AllowAnonymous]
public sealed class LoginPage(IAuthSignIn auth, ICredentialStore creds) : Component
{
    private readonly LoginModel _model = new();
    private string? _error;

    [QueryParam] public string? ReturnUrl { get; set; }

    protected override Component? Render() =>
        Div(Class: "mx-auto", Style: "max-width:24rem")[
            H1()["Sign in"],
            _error is null ? null : Div(Class: "alert alert-danger")[_error],
            Form(_model, OnValidSubmitAsync: SubmitAsync, Class: "vstack gap-3")[
                Input(() => _model.Username, Id: "username", Class: "form-control"),
                Input(() => _model.Password, Id: "password", Type: InputType.Password, Class: "form-control"),
                Button("submit", Class: "btn btn-primary")["Sign in"]
            ]
        ];

    private async Task SubmitAsync(LoginModel m)
    {
        var claims = creds.Validate(m.Username, m.Password);
        if (claims is null) { _error = "Invalid username or password."; return; }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await auth.SignInAsync(new ClaimsPrincipal(identity), returnUrl: ReturnUrl ?? "/");
    }
}

public sealed class LoginModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
```

**A protected page** redirects to `/login?returnUrl=/secure` for anonymous users (handled by the route
guard). Gate the *content* with the `Authorize` component; the `Authorized` slot is a delegate handed
the freshly-authenticated principal, so the greeting reads the name inline — no child component, no
subscription:

```csharp
[Route("secure")]
[Authorize]
public sealed class SecurePage : Component
{
    protected override Component? Render() =>
        Authorize(
            Authorizing:   P()["Signing you in…"],
            NotAuthorized: P()["Please sign in."],
            Authorized: user => Div()[      // ← receives the current principal, re-runs on sign-in/out
                H1()[$"Hello, {user.Identity!.Name}"],
                Authorize(Roles: ["admin"],
                    NotAuthorized: P()["You have standard access."])[
                    Div(Class: "alert alert-warning")["🔑 Admin tools"]]
            ]);
}
```

> **Reactivity.** Sign-in on the Server completes over a WS reconnect that re-seeds the principal and fires
> `IUserProvider.Changed`. The `Authorize` component subscribes to that event and re-renders, re-running its
> `Authorized` delegate with the fresh principal — so reading `user.Identity!.Name` **inside the slot** always
> reflects the current user with no extra work. By contrast, a page that reads `users.Current` *directly in its
> own `Render`* won't re-execute (it didn't subscribe), so a greeting built there can go stale after a
> mid-session sign-in. If you must read the principal outside the slot, either move that markup into a **child
> component** placed in the `Authorized` slot (it first renders once the gate opens), or subscribe the page
> itself: `OnMount() => users.Changed += StateHasChanged;`.

**`Program.cs` — wire cookie auth *before* `UseRask`:**

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "rask.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.LoginPath = "/login";
    });
builder.Services.AddSingleton<ICredentialStore, DemoCredentialStore>();
builder.Services.AddRask(); // no auth config on AddRask — it's all on AddCookie above

var app = builder.Build();

app.UseAuthentication();   // ⚠️ MUST precede UseRask — populates HttpContext.User on GET and WS upgrade
app.UseAuthorization();
app.UseRask<App>();
app.Run();
```

> **Ordering matters.** If `UseAuthentication` runs *after* `UseRask`, `HttpContext.User` is empty when the
> session is seeded and every `[Authorize]` page challenges. Keep it before `UseRask`.

Sign out from any event handler: `await auth.SignOutAsync(returnUrl: "/");`

---

## Cookie + WASM

The WASM client has no server pipeline of its own, so the **API host** owns the cookie. The client hydrates
its principal from `/api/me`. Runnable: **`samples/Rask.Example.Auth.WasmCookie(.Host)`** (with a browser E2E).

> **Scaffold it:** `rask new MyApp --template wasm-hosted --auth` generates this — the `MyApp.Server` host's
> `/api/login` + `/api/me` + `/auth/logout`, and the `MyApp.Client` SPA with `ApiUserProvider`, a login page,
> and a protected `/members` page.

**On the API host** (`MyApp.Server` / your ASP.NET server):

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
builder.Services.AddSingleton<ICredentialStore, DemoCredentialStore>();
// ... build app ...
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/login", async (HttpContext ctx, LoginDto dto, ICredentialStore creds) =>
{
    var claims = creds.Validate(dto.Username, dto.Password);
    if (claims is null) return Results.Unauthorized();
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(new ClaimsPrincipal(identity));   // sets the HttpOnly cookie
    return Results.Ok(new MeDto(dto.Username, claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToArray()));
});

app.MapGet("/api/me", (HttpContext ctx) =>
    ctx.User.Identity?.IsAuthenticated == true
        ? Results.Ok(new MeDto(ctx.User.Identity!.Name!, ctx.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()))
        : Results.NoContent());

app.MapPost("/auth/logout", async (HttpContext ctx) => { await ctx.SignOutAsync(); return Results.Ok(); });

public sealed record LoginDto(string Username, string Password);
public sealed record MeDto(string Name, string[] Roles);
```

**The client `IUserProvider`** bootstraps from `/api/me`:

```csharp
public sealed class ApiUserProvider(HttpClient http) : IUserProvider
{
    private ClaimsPrincipal _current = new(new ClaimsIdentity());
    public ClaimsPrincipal Current => _current;
    public bool IsLoading { get; private set; }
    public event Action? Changed;

    public Task EnsureLoadedAsync() => LoadAsync();

    public async Task RefreshAsync()
    {
        IsLoading = true; Changed?.Invoke();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var me = await http.GetFromJsonAsync("api/me", AuthJson.Default.MeDto);
            _current = me is { Name: { } name }
                ? new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, name), .. me.Roles.Select(r => new Claim(ClaimTypes.Role, r))], "api"))
                : new ClaimsPrincipal(new ClaimsIdentity());
        }
        catch (HttpRequestException) { _current = new ClaimsPrincipal(new ClaimsIdentity()); }
        finally { IsLoading = false; Changed?.Invoke(); }
    }
}

// Source-generated JSON keeps the WASM trim-clean (zero IL warnings).
[JsonSerializable(typeof(MeDto))]
[JsonSerializable(typeof(LoginDto))]
public partial class AuthJson : JsonSerializerContext { }
```

**Client login** posts credentials, then refreshes the provider (WASM `SignInAsync` is intentionally
unsupported — the cookie is set by the server):

```csharp
public sealed class WasmLoginService(HttpClient http, IUserProvider users, Navigator nav)
{
    public async Task<bool> LoginAsync(string username, string password, string? returnUrl)
    {
        var resp = await http.PostAsJsonAsync("api/login", new LoginDto(username, password), AuthJson.Default.LoginDto);
        if (!resp.IsSuccessStatusCode) return false;
        await users.RefreshAsync();
        nav.NavigateTo(returnUrl ?? "/members");
        return true;
    }

    public async Task LogoutAsync()
    {
        await http.PostAsync("auth/logout", null);
        // Navigate first (still in the click-handler scope), then clear the principal — refreshing first
        // closes the Authorize gate and unmounts the calling component before the navigation runs.
        nav.NavigateTo("/login");
        await users.RefreshAsync();
    }
}
```

**`Program.cs` (client)** — note `WasmHostBuilder.BaseAddress` (not Blazor's `HostEnvironment`):

```csharp
var host = WasmHostBuilder.CreateDefault();
host.Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });
host.Services.AddSingleton<ApiUserProvider>();
host.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<ApiUserProvider>()); // overrides the anonymous default
host.Services.AddSingleton<WasmLoginService>();
await host.RunAsync<App>();
```

Wire the form to `WasmLoginService.LoginAsync` and the sign-out button to `WasmLoginService.LogoutAsync`.
(The built-in `WasmAuthSignIn.SignOutAsync` also works, but doing it through your own service keeps the
navigate-before-refresh ordering explicit.)

---

## JWT + Server

Use this when you want a bearer-token API the same identity serves.

> **Recommended (and what the runnable sample does): hold the JWT in
> [`ProtectedSessionStorage`](https://learn.microsoft.com/aspnet/core/blazor/state-management).** It is
> encrypted at rest via ASP.NET Data Protection and decrypted **only server-side**, so the raw token never
> appears in the URL, in a cookie, or as a JS-readable value. Login validates the JWT into a principal and
> sets it on the live session directly (`SessionUserProvider.Set`); a small headless bootstrap re-reads it on
> refresh. Members pages gate with the `Authorize` component (a JWT bearer challenge returns 401, not a login
> redirect, so route `[Authorize]` is the wrong tool for an interactive page). See
> **`samples/Rask.Example.Auth.Jwt`**.

**Recommended — `ProtectedSessionStorage`:**

```csharp
// Program.cs
builder.Services.AddDataProtection();
builder.Services.AddScoped<ProtectedSessionStorage>();   // Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage
builder.Services.AddSingleton<JwtIssuer>();
builder.Services.AddSingleton<JwtValidator>();           // validates a raw JWT → ClaimsPrincipal
builder.Services.AddRask();
// no AddJwtBearer, no UseAuthentication — the principal is held in the live session, not middleware.

// LoginPage submit handler (the WS is up, so JS interop works):
var jwt = issuer.Issue(name, roles);
await sessionStore.SetAsync("rask.jwt", jwt);            // encrypted at rest, decrypted only server-side
if (validator.Validate(jwt) is { } principal)
    users.Set(principal);                               // SessionUserProvider.Set — authenticated in-session
nav.NavigateTo("/members");

// A headless bootstrap re-establishes the principal on a fresh session / refresh:
protected override async Task OnMountAsync()
{
    var result = await sessionStore.GetAsync<string>("rask.jwt");
    if (result.Success && result.Value is { } jwt && validator.Validate(jwt) is { } principal)
        users.Set(principal);
}
```

The members page gates with the `Authorize` component (not route `[Authorize]`, which would 401). Full code:
**`samples/Rask.Example.Auth.Jwt`**.

---

**Alternative — `?access_token` on the WS URL** (the SignalR pattern): simpler, but the token lands in
server access logs, so keep it short-lived and HTTPS-only.

**Issue + validate the token:**

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = "rask-demo",
        ValidateAudience = true, ValidAudience = "rask-demo",
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
        ValidateLifetime = true
    };

    // Read the bearer token off the Rask WS upgrade query string.
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/_rask/ws"))
                ctx.Token = token;
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddRask(); // the JWT scheme is configured on AddJwtBearer above, not on Rask

app.MapPost("/api/login", (LoginDto dto, ICredentialStore creds, IConfiguration cfg) =>
{
    var claims = creds.Validate(dto.Username, dto.Password);
    if (claims is null) return Results.Unauthorized();
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!));
    var jwt = new JwtSecurityToken("rask-demo", "rask-demo", claims,
        expires: DateTime.UtcNow.AddMinutes(15),
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(jwt) });
});
```

**On the client**, store the token and publish it to the WS hook so every (re)connect carries it:

```javascript
// after a successful /api/login on the client:
window.Rask = window.Rask || {};
window.Rask.authToken = () => sessionStorage.getItem("rask.jwt"); // read fresh each reconnect
```

When `window.Rask.authToken` returns a token, `rask.js` appends `?access_token=…` to the WS URL and
`AddJwtBearer`'s `OnMessageReceived` picks it up — `HttpContext.User` is then populated on the WS upgrade
exactly as for cookies.

> The token appears in the WS URL (and thus server access logs). Keep the JWT lifetime short (set on `AddJwtBearer`), require
> HTTPS, and prefer the cookie scheme for the Server host when you can.

---

## JWT + WASM

For a bearer-token SPA, the token rides the `Authorization` header on every `HttpClient` call. The runnable
sample (**`samples/Rask.Example.Auth.WasmJwt(.Host)`**, with a browser E2E) keeps it simple: a plain
**`localStorage`** token store (cleared on sign-out), a `BearerTokenHandler` that attaches it, and the host
validating it with `AddJwtBearer`.

**`TokenStore` (localStorage) + `BearerTokenHandler`:**

```csharp
public sealed class TokenStore(IJSRuntime js)
{
    public string? Token { get; private set; }   // in-memory copy the handler reads synchronously
    public async Task InitAsync()        => Token = await js.InvokeAsync<string?>("localStorage.getItem", "rask.jwt");
    public async Task SetAsync(string t) { Token = t; await js.InvokeVoidAsync("localStorage.setItem", "rask.jwt", t); }
    public async Task ClearAsync()       { Token = null; await js.InvokeVoidAsync("localStorage.removeItem", "rask.jwt"); }
}

public sealed class BearerTokenHandler(TokenStore tokens) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (tokens.Token is { } token)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(req, ct);
    }
}
```

Register the `HttpClient` with the handler; a `JwtUserProvider` hydrates from `/api/me` **only when a token is
present** (so anonymous never hits a 401). On the host, `AddJwtBearer` validates the bearer and `/api/login`
returns the signed JWT.

```csharp
host.Services.AddSingleton<TokenStore>();
host.Services.AddSingleton(sp => new HttpClient(
        new BearerTokenHandler(sp.GetRequiredService<TokenStore>()) { InnerHandler = new HttpClientHandler() })
    { BaseAddress = new Uri(WasmHostBuilder.BaseAddress) });
host.Services.AddSingleton<JwtUserProvider>();
host.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<JwtUserProvider>());
```

> ⚠️ A token in `localStorage` is readable by any script on the page (XSS). For maximum security prefer the
> **HttpOnly-cookie** scheme — the token never reaches JS at all (see [Cookie + WASM](#cookie--wasm)).
>
> Note: the runnable JWT samples and `rask new MyApp --template wasm --auth` scaffold this **plaintext-`localStorage`**
> `TokenStore` as the starting point. Treat it as the floor, not the recommendation — pair it with
> short-lived access tokens (minutes, not hours), HTTPS, and a strict CSP, or graft on the encrypted-at-rest
> `ProtectedTokenStore` below before going to production.

**Harden it — encrypted at rest (`ProtectedTokenStore`).** Instead of plain `localStorage`, encrypt the token
with ASP.NET Data Protection before storing — the browser holds only ciphertext (a server protect/unprotect
round-trip, since a standalone SPA has no key ring):

```csharp
public sealed class ProtectedTokenStore(IJSRuntime js, IDataProtectionProvider dp) : ITokenStore
{
    private readonly IDataProtector _protector = dp.CreateProtector("Rask.Auth.Token");
    private const string Key = "rask.jwt";

    public async ValueTask<string?> GetAsync()
    {
        var cipher = await js.InvokeAsync<string?>("localStorage.getItem", Key);
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return _protector.Unprotect(cipher); }
        catch (CryptographicException) { await ClearAsync(); return null; } // tampered / key rotated
    }

    public async ValueTask SetAsync(string token, bool persist) =>
        await js.InvokeVoidAsync($"{(persist ? "localStorage" : "sessionStorage")}.setItem", Key, _protector.Protect(token));

    public async ValueTask ClearAsync() => await js.InvokeVoidAsync("localStorage.removeItem", Key);
}
```

Silent refresh is an **app responsibility** (Rask doesn't run a refresh timer): from a timer in a long-lived
component, refresh the token before it expires and call `users.RefreshAsync()` — the provider raises `Changed`
and the session re-renders.

---

## Standalone WASM (no host)

A standalone `rask-wasm` app is published as **static files** and has **no server of its own** — the Rask
runtime is entirely in-browser (no WebSocket back to a Rask host). So there's no cookie handshake, no
`/api/me` you own, and no `?access_token=` WS hook. Auth is purely: **POST credentials to an external API,
store the JWT client-side, decode it to a principal, and attach it to your `HttpClient` calls.** The external
API must enable **CORS** for your app's origin and allow the `Authorization` header.

> **Scaffold it:** `rask new MyApp --template wasm --auth` generates the client pieces below (`TokenStore`,
> `BearerTokenHandler`, `JwtUserProvider`, a login page) with a `BaseAddress` stub — point it at your API.

> No host means no ASP.NET Data Protection key ring, so the encrypted "protected storage" option isn't
> available here. Use a **short-lived** JWT in `sessionStorage` (cleared on tab close) with refresh, a strict
> **CSP** to reduce XSS exposure, and HTTPS. If you need the token to never touch JS, you need a host — see
> [JWT + WASM](#jwt--wasm) or the cookie flows.

**A token store** (in-memory + `sessionStorage`, read synchronously by the handler):

```csharp
public sealed class TokenStore(IJSRuntime js)
{
    public string? Token { get; private set; }

    public async Task InitAsync() => Token = await js.InvokeAsync<string?>("sessionStorage.getItem", "rask.jwt");
    public async Task SetAsync(string t) { Token = t; await js.InvokeVoidAsync("sessionStorage.setItem", "rask.jwt", t); }
    public async Task ClearAsync() { Token = null; await js.InvokeVoidAsync("sessionStorage.removeItem", "rask.jwt"); }
}

public sealed class BearerTokenHandler(TokenStore tokens) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (tokens.Token is { } token)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(req, ct);
    }
}
```

**A provider that decodes the JWT to a principal** (no `/api/me` round-trip needed):

```csharp
public sealed class JwtUserProvider(TokenStore tokens) : IUserProvider
{
    private ClaimsPrincipal _current = new(new ClaimsIdentity());
    public ClaimsPrincipal Current => _current;
    public bool IsLoading { get; private set; }
    public event Action? Changed;

    // Awaited by WasmHostBuilder before the first render → no anonymous flash.
    public async Task EnsureLoadedAsync()
    {
        IsLoading = true;
        await tokens.InitAsync();
        Apply(tokens.Token);
        IsLoading = false;
        Changed?.Invoke();
    }

    public void SignedIn()  => Apply(tokens.Token);
    public void SignedOut() => Apply(null);

    private void Apply(string? jwt)
    {
        _current = jwt is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity(DecodeClaims(jwt), "jwt"));
        Changed?.Invoke();
    }

    private static IEnumerable<Claim> DecodeClaims(string jwt)
    {
        var json = Encoding.UTF8.GetString(Base64Url(jwt.Split('.')[1]));
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Name is "name" or "unique_name" or "sub")
                yield return new Claim(ClaimTypes.Name, p.Value.ToString());
            else if (p.Name is "role" or "roles")
            {
                if (p.Value.ValueKind == JsonValueKind.Array)
                    foreach (var r in p.Value.EnumerateArray()) yield return new Claim(ClaimTypes.Role, r.GetString()!);
                else yield return new Claim(ClaimTypes.Role, p.Value.GetString()!);
            }
        }
    }

    private static byte[] Base64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }
}
```

**A login service** posting to the external API:

```csharp
public sealed class LoginService(HttpClient http, TokenStore tokens, JwtUserProvider users, Navigator nav)
{
    public async Task<bool> LoginAsync(string username, string password, string? returnUrl)
    {
        var resp = await http.PostAsJsonAsync("api/login", new LoginDto(username, password), AuthJson.Default.LoginDto);
        if (!resp.IsSuccessStatusCode) return false;
        var dto = await resp.Content.ReadFromJsonAsync(AuthJson.Default.TokenDto);
        await tokens.SetAsync(dto!.Token);
        users.SignedIn();
        nav.NavigateTo(returnUrl ?? "/");
        return true;
    }

    public async Task LogoutAsync()
    {
        await tokens.ClearAsync();
        users.SignedOut();
        nav.NavigateTo("/");
    }
}

public sealed record LoginDto(string Username, string Password);
public sealed record TokenDto(string Token);

[JsonSerializable(typeof(LoginDto)), JsonSerializable(typeof(TokenDto))]
public partial class AuthJson : JsonSerializerContext { } // keeps the WASM publish trim-clean
```

**`Program.cs`:**

```csharp
var host = WasmHostBuilder.CreateDefault();

host.Services.AddSingleton<TokenStore>();
host.Services.AddSingleton(sp => new HttpClient(
        new BearerTokenHandler(sp.GetRequiredService<TokenStore>()) { InnerHandler = new HttpClientHandler() })
    { BaseAddress = new Uri("https://api.example.com/") });   // ← your external API (CORS-enabled)
host.Services.AddSingleton<JwtUserProvider>();
host.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<JwtUserProvider>()); // overrides the anonymous default
host.Services.AddSingleton<LoginService>();

await host.RunAsync<App>();
```

The injected `IUserProvider`, the `Authorize` component, and `[Authorize]` route gating all work against the decoded
principal exactly as everywhere else — they just resolve from `JwtUserProvider` instead of a server-backed
one. (Route-level `[Authorize]` redirects work client-side too, since the guard runs in the WASM session.)

### Cookie variant (static-file SPA + cookie)

You can keep the token out of JS entirely with an **HttpOnly cookie** even for a static-file SPA — but the
cookie is now **cross-origin** (your `app.example.com` SPA calls an `api.example.com` server), which has
hard browser constraints:

> ⚠️ **Cross-site cookies are increasingly blocked.** Safari and Brave block third-party cookies by default,
> and Chrome is phasing them out. A cross-site cookie needs `SameSite=None; Secure`, which many browsers
> drop for "third-party" contexts. **This is only reliable when the SPA and API share a registrable domain**
> (e.g. `app.example.com` + `api.example.com` — same site, so `SameSite=Lax`/`None` works). For truly
> different domains, use the [JWT approach above](#standalone-wasm-no-host) instead.

**On the API server:**

```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.None;            // required for cross-site; needs Secure + HTTPS
});
builder.Services.AddCors(p => p.AddDefaultPolicy(b => b
    .WithOrigins("https://app.example.com")           // the SPA origin — never "*" with credentials
    .AllowAnyHeader().AllowAnyMethod()
    .AllowCredentials()));                            // lets the browser carry the cookie cross-site

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
// /api/login → ctx.SignInAsync(...);  /api/me → reads ctx.User;  /auth/logout → ctx.SignOutAsync()
//   (identical endpoint bodies to the "Cookie + WASM" section)
```

**On the client**, the browser only attaches a cross-site cookie when the fetch uses `credentials: include`.
Set that per request with a handler, then reuse the `ApiUserProvider` (hydrates from `/api/me`) from the
[Cookie + WASM](#cookie--wasm) section unchanged:

```csharp
public sealed class CookieCredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        // .NET WASM BrowserHttpHandler honors this option (Blazor's SetBrowserRequestCredentials wraps it).
        req.Options.Set(
            new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"),
            new Dictionary<string, object> { ["credentials"] = "include" });
        return base.SendAsync(req, ct);
    }
}

// Program.cs
builder.Services.AddSingleton(sp => new HttpClient(
        new CookieCredentialsHandler { InnerHandler = new HttpClientHandler() })
    { BaseAddress = new Uri("https://api.example.com/") });
builder.Services.AddSingleton<ApiUserProvider>();
builder.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<ApiUserProvider>());
```

No token store, no `Authorization` header — the cookie rides every credentialed request automatically, and
`ApiUserProvider.EnsureLoadedAsync()` hydrates the principal from `/api/me` before the first render.

## Identity providers & production hardening

The provider integrations and the hardening reference now live in focused companion pages:

- **[Identity providers](authentication-providers.md)** — ASP.NET Identity, Keycloak / OpenID Connect,
  Auth0, AWS Cognito, and Duende IdentityServer.
- **[Production hardening](authentication-hardening.md)** — the hardening reference, running behind a reverse
  proxy, Content-Security-Policy, and the security checklist.


## Decision table

| Question | Choose |
|---|---|
| Server (WS) app, simplest + safest | **Cookie + Server** |
| WASM SPA talking to your own ASP.NET API, simplest + safest | **Cookie + WASM** |
| Static-file WASM SPA against a separate API (no host of your own) | **Standalone WASM** (JWT in `sessionStorage`) |
| Need a bearer-token API the same identity serves | **JWT** (cookie storage if you can, protected storage if not) |
| Existing user database, password hashing, 2FA | **ASP.NET Identity** (+ cookie) |
| Central SSO / social login / corporate IdP | **OIDC** (+ cookie) — Keycloak, Auth0, AWS Cognito, Duende IdentityServer |

See the [`Authorize`](#declarative-gating) component and [Configuration](#configuration) for how each of
these gates content and is configured.
