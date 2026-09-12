# Rask.Auth.Api

Accounts as **JSON endpoints**, for an ASP.NET Core app that renders no [Rask](https://rask.sh)
components — a TypeScript SPA host, a meta-framework host, or a plain ASP.NET app where the front end
owns the UI.

Register, sign in, sign out, `/me`, email confirmation and password reset, all at `/api/auth`, backed
by **ASP.NET Core Identity** (versioned password hashing, lockout, security stamps, token providers).
The first account to register becomes the administrator.

```csharp
builder.Services.AddRaskAuth<AppDbContext>();
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapRaskAuth();          // POST /register, /login, /logout — GET /me — plus the recovery flows
```

Map the tables in `OnModelCreating` and create the schema:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskAuth();
```

```bash
rask db add AddAuth && rask db update
```

Declare the account type once, anywhere in the app — the generator finds it, and adding a column is
adding a property:

```csharp
public class User : IdentityUser
{
}
```

## Which package

| | |
| --- | --- |
| **Rask.Auth.Api** | This one. The front end owns sign-in; the host answers `/api/auth`. No components, no renderer, no `Rask.Core`. |
| **Rask.Auth** | The app *is* a Rask app. Everything here, plus overridable `/login`, `/register` and `/logout` **pages** and a host-neutral `IAuth` for components. |
| **Rask.Auth.Client** | The same flows called from a WebAssembly client. |

Reference one of the first two, never both: `Rask.Auth` already contains this package.

## Why it is separate

`Rask.Core` — the renderer — is not a package of its own. It travels inside the host packages that
render components (`Rask.Server`, `Rask.Wasm`), so a host that renders nothing ships no copy of it.
A battery that needed Core could therefore not run on `Rask.Spa.Hosting` or `Rask.Meta.Hosting` at
all: the assembly is simply absent and the app aborts before `Main`.

This package is the accounts battery with that dependency removed. It talks to the wire contract in
`Rask.Wire` — the `/api/auth` paths, the request and response shapes, `AuthResult` — which the
browser-side `Rask.Auth.Client` also takes, so both halves agree without either one carrying the
renderer.

## Bearer tokens

Cookies are the default and the right answer for anything running in a page. A non-browser caller
opts in:

```csharp
builder.Services.AddRaskAuth<AppDbContext>(o => o.Bearer = new BearerOptions { SigningKey = key });
```

Then send `X-Rask-Auth-Mode: bearer` alongside `X-Rask-Auth` and a sign-in answers with a token in
the body — never a cookie, never a header, so nothing stores it on the caller's behalf.

## Email

Confirmation and reset links go out through the app's own mail queue (`Rask.Mail`), so they survive a
restart between "the account exists" and "the email went out". An app with no mail battery is told it
cannot send a reset link rather than silently queueing one nowhere.

---

Part of [Rask](https://rask.sh). Full documentation: <https://rask.sh/docs/guides/authentication>.
