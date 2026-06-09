# Authentication in Rask

Rask ships the *plumbing* for authentication — a scoped current-user, a sign-in/out handshake, route
guards, and a declarative gate — and lets you bring any backing store (a cookie, a JWT, ASP.NET Identity,
Keycloak/OIDC, …). This guide shows the complete, copy-pasteable flow for each combination.

- [Concepts](#concepts)
- [Configuration](#configuration)
- [Declarative gating — the `Authorize` component](#declarative-gating)
- [Cookie + Server](#cookie--server)
- [Cookie + WASM](#cookie--wasm)
- [JWT + Server](#jwt--server)
- [JWT + WASM (protected storage)](#jwt--wasm-protected-storage)
- [Standalone WASM (no host)](#standalone-wasm-no-host)
- [ASP.NET Identity](#aspnet-identity)
- [Keycloak / OpenID Connect](#keycloak--openid-connect)
- [Other OIDC providers — Auth0, AWS Cognito, Duende IdentityServer](#other-oidc-providers)
- [Hardening reference](#hardening-reference)
- [Security checklist](#security-checklist)
- [Decision table](#decision-table)

---

## Concepts

| Piece | What it is |
|---|---|
| `IUserProvider` | Scoped source of the current `ClaimsPrincipal` (`Current`), a `Changed` event, optional `EnsureLoadedAsync`/`RefreshAsync`, and `IsLoading`. Server: `SessionUserProvider` (seeded from `HttpContext.User`). WASM: you supply one (or the anonymous default). |
| `Component.User` | The never-null `ClaimsPrincipal` resolved from `IUserProvider` in the active render scope. Gate in `Render()` on `User.Identity?.IsAuthenticated` / `User.IsInRole(...)`. |
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
`Component.User`:

```csharp
// Shorthand: children are the "authorized" branch.
Authorize(Roles: ["admin"])[ AdminPanel() ]

// Full three-slot form.
Authorize(
    Roles: ["admin", "editor"],                       // ANY-of; omit for "any authenticated user"
    Authorized:    Div(Class: "card")[ "Members-only content" ],
    NotAuthorized: A(Href: "/login")[ "Please sign in" ],
    Authorizing:   Spinner())                     // shown while the principal/policy resolves
```

- **`Roles`** and the authenticated check are synchronous → no flicker.
- **`Policy`** (e.g. `Authorize(Policy: "over-18")`) resolves via `IAuthorizationService` in the background;
  the `Authorizing` slot shows until it lands.
- **`Authorizing`** also covers the WASM bootstrap window: while a provider's `EnsureLoadedAsync`/`RefreshAsync`
  is in flight (`IUserProvider.IsLoading == true`), the slot bridges the anonymous→authenticated flash.

Use `Authorize` for *content* gating; use `[Authorize]` on a page for *route* gating; use `Component.User`
directly when you need imperative logic.

---

## Cookie + Server

The lowest-friction, most secure option for the Server (WS) host — the token lives in an HttpOnly cookie and
never reaches JavaScript.

> **Scaffold it:** `dotnet new rask-server --auth` generates exactly this — a `/login` form, a
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

    protected override RenderResult Render() =>
        Div(Class: "mx-auto", Style: "max-width:24rem")[
            H1()["Sign in"],
            _error is null ? Fragment() : Div(Class: "alert alert-danger")[_error],
            Form(_model, OnValidSubmitAsync: SubmitAsync, Class: "vstack gap-3")[
                Input(() => _model.Username, Id: "username", Class: "form-control"),
                Input(() => _model.Password, Id: "password", Type: "password", Class: "form-control"),
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
guard). Gate the *content* with the `Authorize` component and keep the user-dependent view in a child
component in the `Authorized` slot — see the reactivity note below for why:

```csharp
[Route("secure")]
[Authorize]
public sealed class SecurePage : Component
{
    protected override RenderResult Render() =>
        Authorize(
            Authorizing:   P()["Signing you in…"],
            NotAuthorized: P()["Please sign in."],
            Authorized:    SecureContent());     // child component → renders fresh when the gate opens
}

public sealed class SecureContent : Component
{
    protected override RenderResult Render() =>
        Div()[
            H1()[$"Hello, {User.Identity!.Name}"],
            Authorize(Roles: ["admin"],
                Authorized:    Div(Class: "alert alert-warning")["🔑 Admin tools"],
                NotAuthorized: P()["You have standard access."])
        ];
}
```

> **Reactivity.** Sign-in on the Server completes over a WS reconnect that re-seeds the principal and fires
> `IUserProvider.Changed`. The `Authorize` component subscribes to that event and re-renders — but a page
> that reads `User` *directly in its own `Render`* won't re-execute (it didn't subscribe), so a greeting
> built there can go stale after a mid-session sign-in. Two fixes: put `User`-dependent markup inside a
> **child component** in the `Authorized` slot (it first renders only once the gate opens, as above), or
> subscribe the page itself: `OnMount() => users.Changed += StateHasChanged;`. The child-component form
> needs no manual subscription.

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
its principal from `/api/me`.

**On the API host** (`Rask.Wasm.Host` / your ASP.NET server):

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
    public async Task<bool> LoginAsync(LoginModel m, string? returnUrl)
    {
        var resp = await http.PostAsJsonAsync("api/login", new LoginDto(m.Username, m.Password), AuthJson.Default.Options);
        if (!resp.IsSuccessStatusCode) return false;
        await users.RefreshAsync();
        nav.Navigate(returnUrl ?? "/");
        return true;
    }
}
```

**`Program.cs` (client):**

```csharp
var builder = WasmHostBuilder.CreateDefault(args);
builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<ApiUserProvider>();
builder.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<ApiUserProvider>()); // overrides the anonymous default
builder.Services.AddSingleton<WasmLoginService>();
await builder.RunAsync<App>();
```

Sign out with the built-in WASM signer (POSTs `/auth/logout`, refreshes, navigates):
`await auth.SignOutAsync(returnUrl: "/");`

---

## JWT + Server

Use this when you want a bearer-token API the same identity serves. Because browsers can't set an
`Authorization` header on a WebSocket upgrade, Rask carries the token on the WS URL as `?access_token=`
(the SignalR pattern).

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

## JWT + WASM (protected storage)

For a bearer-token SPA, the token rides the `Authorization` header on every `HttpClient` call. Per the
"never store a plaintext token" rule, the token is **encrypted with ASP.NET Data Protection** before it
touches browser storage.

**`ProtectedTokenStore` (reference implementation of `ITokenStore`):**

```csharp
public sealed class ProtectedTokenStore(IJSRuntime js, IDataProtectionProvider dp) : ITokenStore
{
    private readonly IDataProtector _protector = dp.CreateProtector("Rask.Auth.Token");
    private const string Key = "rask.jwt";

    public async ValueTask<string?> GetAsync()
    {
        var cipher = await js.InvokeAsync<string?>("localStorage.getItem", Key)
                     ?? await js.InvokeAsync<string?>("sessionStorage.getItem", Key);
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return _protector.Unprotect(cipher); }
        catch (CryptographicException) { await ClearAsync(); return null; } // tampered / key rotated
    }

    public async ValueTask SetAsync(string token, bool persist)
    {
        var cipher = _protector.Protect(token);                     // ← ciphertext, never the raw JWT
        var store = persist ? "localStorage" : "sessionStorage";    // remember-me vs tab-scoped
        await js.InvokeVoidAsync($"{store}.setItem", Key, cipher);
    }

    public async ValueTask ClearAsync()
    {
        await js.InvokeVoidAsync("localStorage.removeItem", Key);
        await js.InvokeVoidAsync("sessionStorage.removeItem", Key);
    }
}
```

> On WASM, ASP.NET Data Protection needs a key ring. For a browser-only SPA, deliver the protected blob from
> the server (protect in the API, unprotect on `/api/me`), or — simpler and stronger — use the **HttpOnly
> cookie** scheme so the token never reaches JS at all. Protected storage is the right call when you must
> hold a bearer token client-side for a non-cookie API.

**Attach the bearer token to outgoing calls:**

```csharp
public sealed class BearerTokenHandler(ITokenStore tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        if (await tokens.GetAsync() is { } token)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(req, ct);
    }
}
```

The `ApiUserProvider` from the cookie section works unchanged — it hydrates from `/api/me`, which is now
validated by `AddJwtBearer`. Silent refresh is an **app responsibility** (Rask doesn't run a refresh timer):
from a timer in a long-lived component, refresh the token before it expires and call `users.RefreshAsync()`
— the provider raises `Changed` and the session re-renders.

---

## Standalone WASM (no host)

A standalone `rask-wasm` app is published as **static files** and has **no server of its own** — the Rask
runtime is entirely in-browser (no WebSocket back to a Rask host). So there's no cookie handshake, no
`/api/me` you own, and no `?access_token=` WS hook. Auth is purely: **POST credentials to an external API,
store the JWT client-side, decode it to a principal, and attach it to your `HttpClient` calls.** The external
API must enable **CORS** for your app's origin and allow the `Authorization` header.

> No host means no ASP.NET Data Protection key ring, so the encrypted "protected storage" option isn't
> available here. Use a **short-lived** JWT in `sessionStorage` (cleared on tab close) with refresh, a strict
> **CSP** to reduce XSS exposure, and HTTPS. If you need the token to never touch JS, you need a host — see
> [JWT + WASM](#jwt--wasm-protected-storage) or the cookie flows.

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
        var resp = await http.PostAsJsonAsync("api/login", new LoginDto(username, password), AuthJson.Default.Options);
        if (!resp.IsSuccessStatusCode) return false;
        var dto = await resp.Content.ReadFromJsonAsync(AuthJson.Default.TokenDto);
        await tokens.SetAsync(dto!.Token);
        users.SignedIn();
        nav.Navigate(returnUrl ?? "/");
        return true;
    }

    public async Task LogoutAsync()
    {
        await tokens.ClearAsync();
        users.SignedOut();
        nav.Navigate("/");
    }
}

public sealed record LoginDto(string Username, string Password);
public sealed record TokenDto(string Token);

[JsonSerializable(typeof(LoginDto)), JsonSerializable(typeof(TokenDto))]
public partial class AuthJson : JsonSerializerContext { } // keeps the WASM publish trim-clean
```

**`Program.cs`:**

```csharp
var builder = WasmHostBuilder.CreateDefault(args);

builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton(sp => new HttpClient(
        new BearerTokenHandler(sp.GetRequiredService<TokenStore>()) { InnerHandler = new HttpClientHandler() })
    { BaseAddress = new Uri("https://api.example.com/") });   // ← your external API (CORS-enabled)
builder.Services.AddSingleton<JwtUserProvider>();
builder.Services.AddSingleton<IUserProvider>(sp => sp.GetRequiredService<JwtUserProvider>()); // overrides the anonymous default
builder.Services.AddSingleton<LoginService>();

await builder.RunAsync<App>();
```

`Component.User`, the `Authorize` component, and `[Authorize]` route gating all work against the decoded
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

---

## ASP.NET Identity

ASP.NET Identity is just a richer `ICredentialStore` + cookie. Wire Identity for storage/password hashing,
then sign in through Rask's handshake.

```csharp
builder.Services
    .AddIdentityCore<IdentityUser>(o => o.Password.RequiredLength = 8)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme);
builder.Services.AddRask();
// app: UseAuthentication(); UseAuthorization(); UseRask<App>();
```

In the login page, validate with `SignInManager` / `UserManager` and build the principal Identity provides:

```csharp
[Route("login"), AllowAnonymous]
public sealed class LoginPage(
    SignInManager<IdentityUser> signIn,
    UserManager<IdentityUser> users,
    IAuthSignIn auth) : Component
{
    private readonly LoginModel _model = new();
    private string? _error;
    [QueryParam] public string? ReturnUrl { get; set; }

    protected override RenderResult Render() => /* same form as Cookie + Server */ ...;

    private async Task SubmitAsync(LoginModel m)
    {
        var user = await users.FindByNameAsync(m.Username);
        if (user is null || !await signIn.CanSignInAsync(user) ||
            !(await signIn.CheckPasswordSignInAsync(user, m.Password, lockoutOnFailure: true)).Succeeded)
        {
            _error = "Invalid credentials."; return;
        }

        var principal = await signIn.CreateUserPrincipalAsync(user); // includes Identity's claims + roles
        await auth.SignInAsync(principal, returnUrl: ReturnUrl ?? "/", scheme: IdentityConstants.ApplicationScheme);
    }
}
```

Everything else (`Component.User`, `Authorize`, `[Authorize(Roles = "...")]`) works against the Identity
principal unchanged. Registration/2FA/lockout are standard Identity APIs called from your pages.

---

## Keycloak / OpenID Connect

For an external IdP, let ASP.NET's OIDC handler own the login redirect; Rask reads the resulting cookie.

```csharp
builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, o =>
    {
        o.Authority = "https://keycloak.example.com/realms/rask"; // your realm
        o.ClientId = "rask-app";
        o.ClientSecret = builder.Configuration["Keycloak:ClientSecret"];
        o.ResponseType = "code";
        o.SaveTokens = true;
        o.GetClaimsFromUserInfoEndpoint = true;
        o.Scope.Add("roles");
        o.TokenValidationParameters.RoleClaimType = "roles"; // map Keycloak realm roles → User.IsInRole
        o.TokenValidationParameters.NameClaimType = "preferred_username";
    });
builder.Services.AddRask(auth => auth.ChallengePath = "/login");
// app: UseAuthentication(); UseAuthorization(); UseRask<App>();
```

Because the challenge is an HTTP redirect to Keycloak (not an in-app form), expose plain endpoints for the
round trip and point users at them:

```csharp
app.MapGet("/login", (string? returnUrl, HttpContext ctx) =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl ?? "/" },
        [OpenIdConnectDefaults.AuthenticationScheme]));

app.MapPost("/logout", (HttpContext ctx) =>
    Results.SignOut(new AuthenticationProperties { RedirectUri = "/" },
        [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
```

A "Sign in" link (`A(Href: "/login?returnUrl=/secure")`) sends the user through Keycloak; on return the
cookie is set and the next Rask GET/WS sees the authenticated `User`. Route `[Authorize]` pages now
challenge straight to Keycloak via `ChallengePath`.

---

## Other OIDC providers

Auth0, AWS Cognito, and Duende IdentityServer integrate exactly like [Keycloak](#keycloak--openid-connect):
ASP.NET's OIDC handler owns the login redirect, Rask reads the resulting cookie, and the
`/login` + `/logout` endpoints and "Sign in" link are **identical**. Only the `AddOpenIdConnect(...)` block
and the provider's logout quirk differ. Swap the relevant block into the Keycloak setup.

### Auth0

```csharp
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, o =>
{
    o.Authority = $"https://{builder.Configuration["Auth0:Domain"]}";        // e.g. dev-abc123.us.auth0.com
    o.ClientId = builder.Configuration["Auth0:ClientId"];
    o.ClientSecret = builder.Configuration["Auth0:ClientSecret"];
    o.ResponseType = "code";
    o.SaveTokens = true;
    o.Scope.Clear(); o.Scope.Add("openid"); o.Scope.Add("profile"); o.Scope.Add("email");
    o.CallbackPath = "/callback";
    o.TokenValidationParameters.NameClaimType = "name";
    // Auth0 emits roles only if an Action/Rule adds a namespaced claim:
    o.TokenValidationParameters.RoleClaimType = "https://your-app.example.com/roles";

    // Auth0 logout needs its own endpoint (clears the Auth0 session, then returnTo):
    o.Events.OnRedirectToIdentityProviderForSignOut = ctx =>
    {
        var returnTo = Uri.EscapeDataString($"{ctx.Request.Scheme}://{ctx.Request.Host}/");
        ctx.Response.Redirect($"https://{builder.Configuration["Auth0:Domain"]}/v2/logout" +
            $"?client_id={builder.Configuration["Auth0:ClientId"]}&returnTo={returnTo}");
        ctx.HandleResponse();
        return Task.CompletedTask;
    };
});
```

Add an `audience` (and `o.Scope.Add("...")`) only if you also call an Auth0-protected API. Define roles with
an Auth0 **Action** that adds `event.authorization.roles` to the ID token under your namespace.

### AWS Cognito

```csharp
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, o =>
{
    var region = builder.Configuration["Cognito:Region"];        // e.g. eu-west-1
    var poolId = builder.Configuration["Cognito:UserPoolId"];    // e.g. eu-west-1_AbCdEf
    o.Authority = $"https://cognito-idp.{region}.amazonaws.com/{poolId}";
    o.MetadataAddress = $"{o.Authority}/.well-known/openid-configuration";
    o.ClientId = builder.Configuration["Cognito:ClientId"];
    o.ClientSecret = builder.Configuration["Cognito:ClientSecret"];
    o.ResponseType = "code";
    o.SaveTokens = true;
    o.Scope.Clear(); o.Scope.Add("openid"); o.Scope.Add("profile"); o.Scope.Add("email");
    o.TokenValidationParameters.NameClaimType = "cognito:username";
    o.TokenValidationParameters.RoleClaimType = "cognito:groups";  // User Pool groups → User.IsInRole

    // Cognito uses its own Hosted-UI logout endpoint:
    o.Events.OnRedirectToIdentityProviderForSignOut = ctx =>
    {
        var domain = builder.Configuration["Cognito:Domain"];      // your-app.auth.{region}.amazoncognito.com
        var logoutUri = Uri.EscapeDataString($"{ctx.Request.Scheme}://{ctx.Request.Host}/");
        ctx.Response.Redirect($"https://{domain}/logout" +
            $"?client_id={builder.Configuration["Cognito:ClientId"]}&logout_uri={logoutUri}");
        ctx.HandleResponse();
        return Task.CompletedTask;
    };
});
```

Map users to roles by assigning them to **Cognito User Pool groups** — they arrive in the `cognito:groups`
claim.

### Duende IdentityServer

```csharp
.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, o =>
{
    o.Authority = builder.Configuration["Duende:Authority"];      // e.g. https://ids.example.com
    o.ClientId = "rask-app";
    o.ClientSecret = builder.Configuration["Duende:ClientSecret"];
    o.ResponseType = "code";
    o.UsePkce = true;
    o.SaveTokens = true;
    o.GetClaimsFromUserInfoEndpoint = true;
    o.Scope.Clear(); o.Scope.Add("openid"); o.Scope.Add("profile"); o.Scope.Add("roles");
    o.TokenValidationParameters.NameClaimType = "name";
    o.TokenValidationParameters.RoleClaimType = "role";
    o.SignedOutCallbackPath = "/signout-callback-oidc"; // Duende honors standard RP-initiated logout
});
```

Duende supports standard RP-initiated logout, so the `/logout` endpoint from the Keycloak section works as-is
(no provider-specific redirect needed) — `Results.SignOut(... [Cookie, OpenIdConnect])` clears both sessions.
Define an IdentityResource/`role` claim and add it to the client's allowed scopes on the Duende side.

---

## Hardening reference

| Concern | How Rask handles it |
|---|---|
| **Redeem-ticket TTL** | 30s, fixed. The ticket is single-use, session-bound, 128-bit. |
| **CSRF on `/_rask/auth/redeem`** | The secret session-bound ticket defeats classic CSRF; the endpoint additionally **rejects cross-origin `Origin`/`Referer`** (HTTP 403). |
| **Sign-out invalidation** | Redeem clears the cookie; the WS reconnect re-seeds `SessionUserProvider` to anonymous. `SessionUserProvider.Clear()` is available for explicit invalidation. |
| **Session expiry → re-auth** | A swept live session pushes `{type:"session",status:"unknown"}`; `rask.js` reloads → fresh GET → route guard challenges to `ChallengePath?returnUrl=…`. |
| **JWT on WebSocket** | Token rides `?access_token=` on the WS URL via `window.Rask.authToken`; pair with `AddJwtBearer`'s `OnMessageReceived`. |
| **Token at rest (JWT/WASM)** | `ProtectedTokenStore` encrypts with Data Protection before storage; the HttpOnly-cookie scheme keeps it out of JS entirely. |

---

## Security checklist

- ☑ Serve auth over **HTTPS** only (`Cookie.SecurePolicy = Always` on `AddCookie`).
- ☑ Cookies: `HttpOnly`, `Secure`, `SameSite=Lax` (or `Strict`).
- ☑ Prefer the **cookie scheme** — the token never reaches JavaScript (immune to XSS token theft).
- ☑ If a token must be in the browser, store it **encrypted** (`ProtectedTokenStore`), never plaintext.
- ☑ Short JWT lifetime + app-driven silent refresh so sessions stay smooth without long-lived tokens.
- ☑ Keep `UseAuthentication()` **before** `UseRask()`.
- ☑ Validate redirect targets — Rask sanitizes the `returnUrl` to local paths.
- ☑ Rotate signing keys; manage the Data Protection key ring (persisted, encrypted at rest).

---

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
