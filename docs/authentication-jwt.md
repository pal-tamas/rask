# JWT authentication

Bearer-token authentication with JWTs on the Rask Server host, on a WASM SPA with an API host, and on a standalone static-file WASM app.

‹ Back to [Authentication](authentication.md)

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
> **HttpOnly-cookie** scheme — the token never reaches JS at all (see [Cookie + WASM](authentication-cookie.md#cookie--wasm)).
>
> Note: the runnable JWT samples show this **plaintext-`localStorage`** `TokenStore` as a starting point.
> Treat it as the floor, not the recommendation — pair it with short-lived access tokens (minutes, not
> hours), HTTPS, and a strict CSP, or graft on the encrypted-at-rest `ProtectedTokenStore` below before
> going to production. `rask new` no longer scaffolds any of it: a browser app calls
> `AddRaskAuthClient()` and authenticates against its own server over a same-origin cookie, so no token
> reaches JavaScript at all.

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

> **Not scaffolded.** This is the hand-written path for a browser app talking to an API that is *not* a
> Rask server. If yours is one — the ordinary case — `AddRaskAuthClient()` gives you `IAuth` and
> `IUserProvider` over a same-origin cookie and none of the code below is needed. See
> [the browser half](authentication.md).

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
[Cookie + WASM](authentication-cookie.md#cookie--wasm) section unchanged:

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
