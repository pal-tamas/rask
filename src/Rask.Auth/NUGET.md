# Rask.Auth

Accounts for a [Rask](https://rask.sh) app: **register, sign in, sign out**, plus **email confirmation
and password reset**.

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

## Confirming an address, and resetting a password

Registering emails a confirmation link; `/forgot-password` emails a reset link. Both go out through
the app's own mail queue, and `/reset-password` and `/confirm-email` are where those links land —
built-in pages, overridable exactly like `/login`.

**Confirmation does not block sign-in by default.** A freshly scaffolded app has no SMTP configured,
so requiring it out of the box would let the first registration succeed and then be unable to sign in,
with the email that would fix it being the one that cannot be sent. Turn it on in one line:

```csharp
app.Configure(c => c.Auth.Configure(o =>
{
    o.RequireConfirmedEmail = true;
    o.PublicOrigin = "https://app.example.com";   // required behind a proxy
}));
```

`PublicOrigin` matters: an emailed link has to be absolute, and Rask never builds one from a forwarded
host header — that value is attacker-controlled on a request that reaches the app directly, and a
reset link built from it would send a working token to a domain of the attacker's choosing.

`/forgot-password` answers the same way whether or not the address has an account, so it cannot be
used to find out which addresses are registered. A completed reset ends every other session for that
account.

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
