# Authentication — identity providers

Provider integrations for [Rask authentication](authentication.md): bring your own user store, or sign in
through an external OpenID Connect provider. For the core cookie/JWT flows and the `Authorize` gate, see the
[main authentication guide](authentication.md).


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

    protected override Component? Render() => /* same form as Cookie + Server */ ...;

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

Everything else (the injected `IUserProvider`, `Authorize`, `[Authorize(Roles = "...")]`) works against the Identity
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
