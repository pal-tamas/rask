# Rask.Auth

Accounts and the three flows — **register, sign in, sign out** — for a [Rask](https://rask.sh) app.

Accounts are backed by **ASP.NET Core Identity** (versioned password hashing, lockout, security
stamps, token providers), wrapped behind Rask's own host-neutral surface. The code you write to read
the current user or gate a page does not change between hosts.

```csharp
// the same three calls on the Server host, in WebAssembly, and inside an island
public sealed class LoginForm(IAuth auth) : Component
{
    private async Task SubmitAsync(Credentials c) =>
        await auth.SignInAsync(c.Email, c.Password, returnUrl: "/");
}

// reading who is signed in — unchanged, and the same everywhere
public sealed class Header(IUserProvider users) : Component
{
    protected override Component Render() =>
        Authorize
            .NotAuthorized(NavLink.Href("/login")["Sign in"])
            .Authorized(user => Span[$"Hi, {user.Identity?.Name}"]);
}
```

## The first account is the administrator

The first account to register gets the `admin` role; every one after it gets `user`. There is no
seeding migration and no create-admin command.

Because an app deployed with an empty user table and an open registration page is a land-grab, the
**first** registration — and only the first — needs a one-time token, generated while the instance is
unclaimed and written to the startup log. Every registration after it is an ordinary open one.

The single-winner guarantee is a constant primary key on one row, not a count of the users table: two
registrations arriving together cannot both award themselves the role, on any database provider.

## Getting started

```csharp
builder.Services.AddRaskAuth<AppDbContext>();
```

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskAuth();
```

Then `rask db add AddAuth && rask db update`.

In an app that references the `Rask` meta-package this is already wired — auth is on by default, and
`app.Configure(c => c.Auth.Off())` is how an app does without it.

Full documentation: [rask.sh](https://rask.sh) · `docs/authentication.md`
