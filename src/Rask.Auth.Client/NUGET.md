# Rask.Auth.Client

The browser half of [Rask.Auth](https://www.nuget.org/packages/Rask.Auth). Register it and a
WebAssembly app has the same three flows a server-rendered one has — written the same way.

```csharp
var builder = WasmHostBuilder.CreateDefault();
builder.Services.AddRaskAuthClient();
await builder.RunAsync<App>();
```

That is the whole of it. After that a component reads the current user and signs somebody in with
exactly the code it would use on the Server host:

```csharp
public sealed class LoginForm(IAuth auth) : Component
{
    private async Task SubmitAsync(Credentials c) =>
        await auth.SignInAsync(c.Email, c.Password, returnUrl: "/");
}

public sealed class Header(IUserProvider users) : Component
{
    protected override Component Render() =>
        Authorize
            .NotAuthorized(NavLink.Href("/login")["Sign in"])
            .Authorized(user => Span[$"Hi, {user.Identity?.Name}"]);
}
```

## What it does

`AddRaskAuthClient()` replaces the browser host's anonymous `IUserProvider` with one that reads the
app's own `GET /api/auth/me`, and registers an `IAuth` that posts to `/api/auth/register`, `/login`
and `/logout`. Calls are same-origin, so the auth cookie rides along on its own — **no token is ever
held in JavaScript**, and there is nothing for a script on the page to read.

The current user is loaded before the first render, so a page never paints anonymous and then flips.

## What it deliberately does not carry

ASP.NET Core Identity and Entity Framework. Those live in `Rask.Auth`, on the server, and have no
business in a trimmed browser publish. The two halves agree through the `AuthApi` wire contract in
Rask.Core rather than by referencing each other.

Full documentation: [rask.sh](https://rask.sh) · `docs/authentication.md`
