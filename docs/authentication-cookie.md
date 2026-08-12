# Cookie authentication

Cookie-based login and session for the Rask Server (WS) host and for a WASM SPA backed by your own API host.

‹ Back to [Authentication](authentication.md)

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
        Div.Class("mx-auto").Style("max-width:24rem")[
            H1["Sign in"],
            _error is null ? null : Div.Class("alert alert-danger")[_error],
            Form.Model(_model).OnValidSubmitAsync(SubmitAsync).Class("vstack gap-3")[
                Input.Bind(() => _model.Username).Id("username").Class("form-control"),
                Input.Bind(() => _model.Password).Id("password").Type(InputType.Password).Class("form-control"),
                Button.Type("submit").Class("btn btn-primary")["Sign in"]
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
        Authorize.Authorizing(P["Signing you in…"]).NotAuthorized(P["Please sign in."]).Authorized(user => Div[      // ← receives the current principal, re-runs on sign-in/out
                H1[$"Hello, {user.Identity!.Name}"],
                Authorize.Roles(["admin"]).NotAuthorized(P["You have standard access."])[
                    Div.Class("alert alert-warning")["🔑 Admin tools"]]
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
