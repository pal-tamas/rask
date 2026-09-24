# Authentication in Rask

> **In practice:** [Tutorial Ch 3](tutorial/03-orders-and-auth.md) · recipe [require login on a page](recipes.md#require-login-on-a-page) · [cheat sheet](cheatsheet.md).

**Authentication is on by default.** A fresh app can register somebody, sign them in and sign them out
without a line of auth code. The account is your own `User` aggregate, each signed-in device is a session
row you can list and end, and the first account to register becomes the administrator. There is no ASP.NET
Core Identity underneath: Rask.Auth hashes passwords, issues links, throttles guessing and tracks sessions
itself, on standard .NET pieces (PBKDF2 or bcrypt, Data Protection, cookie authentication).

**The sign-in pages are yours.** `rask new` writes them into `Features/Auth` — `/login`, `/register`,
`/logout`, `/forgot-password`, `/reset-password`, `/confirm-email` and `/devices` — drawn with
[daisyUI](ui-kit.md) and compiled by the app's own Tailwind like every other page, so restyling them is
editing them. They are ordinary components written against `IAuth`, so the flows themselves keep coming
from `Rask.Auth`.

The API is the same on every host. A component injects `IAuth` to move somebody between signed-out and
signed-in, and `IUserProvider` to read who that is — identical on the Server host, in WebAssembly, and
inside an island. A TypeScript front end and a meta framework's Node process reach the same flows
through `/api/auth`.

## Two packages, one battery

| Package | Use it when | What it adds |
| --- | --- | --- |
| `Rask.Auth.Api` | The host renders **no** Rask components — a SPA template, a meta template, or a plain ASP.NET app | Accounts and sessions, the `/api/auth` endpoints, the cookie, bearer tokens, the account lifecycle |
| `Rask.Auth` | The app **is** a Rask app — the `server` and `wasm` templates | All of the above, plus `IAuth` for components and email bodies written as components; `rask new` adds the pages |

Reference one or the other, never both: `Rask.Auth` already contains `Rask.Auth.Api`. Both put their
types in the `Rask.Auth` namespace and both call the battery `AddRaskAuth` / `MapRaskAuth`, so moving an
app from one lane to the other changes a `PackageReference` and nothing else.

The split exists because `Rask.Core` — the renderer — is not a package. It travels *inside* the host
packages that render components (`Rask.Server`, `Rask.Wasm`), so `Rask.Spa.Hosting` and
`Rask.Meta.Hosting` ship no copy of it. Until #1069 the accounts battery reached for Core on every lane,
which meant a scaffolded SPA or meta app could not start at all: the assembly was simply absent and the
process aborted before `Main`, after a build that succeeded. `Rask.Auth.Api` is the battery with that
dependency removed; it speaks the wire contract in `Rask.Wire` — the `/api/auth` paths, the request and
response shapes, `AuthResult` — which the browser-side `Rask.Auth.Client` also takes, so both halves
agree without either one carrying the renderer.

What you give up on `Rask.Auth.Api` is exactly what needs a renderer: no `IAuth` (there are no components to
inject it into) — the front end owns the sign-in UI, which it does on every lane anyway. The
endpoints, the options, the roles and the emails are the same code.

```csharp
public sealed partial class SignIn(IAuth auth) : Component
{
    private async Task SubmitAsync(Credentials c) =>
        await auth.SignInAsync(c.Email, c.Password, returnUrl: "/");
}

public sealed partial class Header(IUserProvider users) : Component
{
    protected override Component? Render() =>
        Authorize
            .NotAuthorized(NavLink.Href(Routes.LoginPage())["Sign in"])
            .Authorized(user => Span[$"Hi, {user.Identity?.Name}"]);
}
```

To do without it, drop the `AddRaskAuth` line from `Program.cs` — or, in an app built on the `Rask`
package, write `app.Configure(c => c.Auth.Off())`. Bringing your own store or an external provider
(Keycloak/OIDC, an existing users table) is still supported: the pages, the guards and the
`Authorize` component are written against `ClaimsPrincipal`, so they do not care where it came from.

**The session is a cookie unless you say otherwise.** Rask authenticates one kind of session in a
browser and `Rask.Auth` owns that scheme, so there is nothing to hold in `localStorage` and nothing to
choose. An external provider composes the ordinary ASP.NET way — it adds a *challenge* scheme beside
the cookie and signs in through it — which is what
[identity providers](authentication-providers.md) documents.

### Bearer tokens, for the callers a cookie cannot serve

A native client, a CLI or a service-to-service call has no cookie jar. `AuthOptions.Bearer` adds a JWT
scheme **beside** the cookie — never instead of it — so every page, form and redirect behaves exactly
as before, and only a caller sending `Authorization: Bearer …` takes the new path.

```jsonc
// appsettings.json
{
  "Rask": {
    "Auth": {
      "Bearer": true,
      "BearerLifetime": "01:00:00"
    }
  }
}
```

```bash
# The signing key never goes in that file.
dotnet user-secrets set "Rask:Auth:BearerSigningKey" "<at least 32 bytes>"   # development
rask deploy --env "Rask__Auth__BearerSigningKey=…"                           # deployed
```

`AddRaskAuth<AppDbContext>()` reads `Rask:Auth` itself. A callback — `AddRaskAuth<AppDbContext>(o => …)` —
runs after the section and wins.

Ask for a token by sending `X-Rask-Auth-Mode: bearer` alongside the usual `X-Rask-Auth` header on
`POST /api/auth/login`. The answer is a `BearerSession` — the token, its type, its remaining seconds and
the same user `/me` describes. The token is in the **body only**: never a cookie, never a response
header, so nothing stores it on the caller's behalf.

Three things about this are decisions rather than defaults, and each is deliberate:

- **The key comes from configuration and nowhere else.** A key generated at startup does not survive a
  restart and is not shared between instances, so every token would die on deploy and nothing would work
  behind two replicas. It must be at least 32 bytes; keep it in user-secrets, the environment or a
  secret store.
- **A missing or unusable key refuses to start**, outside Development. An operator who believes they
  enabled bearer, and whose app quietly did not, is the more expensive failure — the same reasoning that
  makes `MailOptions.From` throw. In Development it warns and leaves the app on cookies, so a first run
  needs no configuration.
- **There is no refresh token.** Refresh needs a revocation story, revocation needs storage, and that is
  a much larger feature than this one. A short access token is honest about what it is: when it expires,
  the caller signs in again.

**Keep browsers on the cookie.** A token in `localStorage` is readable by any script that gets onto the
page, which is precisely what the cookie path avoids — `HttpOnly`, `Secure`, and never visible to
JavaScript. Bearer is for clients that are not a browser.

An app that would rather bring its own JWT setup should **leave `Bearer` off** and call
`AddAuthentication().AddJwtBearer(…)` itself: with the option off, `AddRaskAuth` registers nothing
bearer-related and does not touch your scheme. Turning it on means the opposite — Rask owns the bearer
scheme'''s settings and configures them last, exactly as it owns the cookie'''s — so the two are a
choice between, not a layering.

## On this page

- [Your `User`](#your-user)
- [Sessions and devices](#sessions-and-devices)
- [Passwords and throttling](#passwords-and-throttling)
- [Passkeys](#passkeys)
- [Concepts](#concepts)
- [The first account is the administrator](#the-first-account-is-the-administrator)
- [Accounts and tenants](#accounts-and-tenants)
- [Confirming an address, and resetting a password](#confirming-an-address-and-resetting-a-password)
- [Configuration](#configuration)
- [Declarative gating — the `Authorize` component](#declarative-gating)
- [Cookie authentication](authentication-cookie.md) — cookie login/session on Server and WASM.
- [Keycloak / OpenID Connect](authentication-providers.md#keycloak--openid-connect)
- [Other OIDC providers — Auth0, AWS Cognito, Duende IdentityServer](authentication-providers.md#other-oidc-providers)
- [Hardening reference](authentication-hardening.md#hardening-reference)
- [Security checklist](authentication-hardening.md#security-checklist)
- [Decision table](#decision-table)

---

## Your `User`

Rask ships no user class. Your app declares one — `rask new` writes it into `Features/Shared/User.cs` — and
that is the account:

```csharp
public sealed class User : Authenticatable
{
    [MaxLength(100)]
    public string DisplayName { get; private set; } = "";

    public void Rename(string displayName) => DisplayName = displayName.Trim();
}
```

`Authenticatable` carries what a sign-in needs: `Email` (stored trimmed and lower-cased, unique — within a tenant,
when the app [has tenants](#accounts-and-tenants)),
`EmailConfirmedAt`, `PasswordChangedAt`, `Roles`, and the password hash — which is `internal`, so nothing that
serializes a `User`'s public properties can ever carry it. Add the columns your app needs, then
`rask db add AddUserColumns && rask db update`.

**It is an [aggregate](data.md) like any other.** `Authenticatable` derives from `Aggregate<Guid>`, so a `User`
has the reads, the writes, a generated `UserModel` for a profile form (never with the credentials on it), a
`Version` and domain events. It does **not** soft-delete: soft delete is [opt-in](data.md#choosing-what-a-table-carries),
and a deleted account has to free its address for the person to sign up again, so deleting a user removes the row.
`Session` is the one Rask.Auth table that opts in (below):

```csharp
var me = users.Current.UserId() is { } id
    ? await User.Read.Where(u => u.Id == id).FirstOrDefaultAsync(CancellationToken)
    : null;
await User.UpdateAsync(id, u => u.Rename(name));
await User.UpdateAsync(id, u => u.GrantRole("editor"));
```

Other aggregates refer to a user by id — `public Guid OwnerId { get; private set; }` — rather than holding one.

**Set your own columns while registering.** `IAuth.RegisterAsync` takes a lambda that runs on the new user
before it is saved, in the same insert; the scaffolded register page uses it for the display name:

```csharp
await auth.RegisterAsync(model.Email, model.Password, (User user) => user.Rename(model.DisplayName), ReturnUrl);
```

**Nothing has to name it.** A source generator finds the one `Authenticatable` in your project, so
`AddRaskAuth()` and `modelBuilder.AddRaskAuth()` take no type argument. Two user types is
[RASK074](diagnostics.md#rask074). Declare none and you have no accounts, and auth is not wired. An app that
would rather be explicit can be: `AddRaskAuth<AppDbContext, User>()` and `modelBuilder.AddRaskAuth<User>()`.

## Sessions and devices

**Each signed-in device is a row.** Signing in starts a `Session` (the user, the address and browser it came from,
when it was last seen, when it expires, whether it is remembered) and the cookie carries only that session's id,
sealed. Every request loads the session and rebuilds the user's claims from it, so a role granted or removed shows
up without signing in again. Signing out ends the row — `Session` declares `Deletes = Deletion.Soft`, so an ended
session is stamped rather than removed, and the device list can still say "signed out yesterday". A sweep removes
the rows of sessions that ended or expired more than a day ago, once an hour and when the host starts.

```csharp
var devices = await Session.Read.Where(s => s.UserId == me)
                                .OrderByDescending(s => s.LastSeenAt).ToListAsync();

await auth.SignOutOtherDevicesAsync();   // every session but this one
await auth.SignOutEverywhereAsync();     // this one too
```

The scaffolded `/devices` page lists them and has the button. An ended session stops working on that device's
**next request** — and on a live page, before its next handler runs: the Server host re-checks a page's session at
most every 30 seconds (`ISessionRevalidator`), so a page left open for hours is signed out rather than staying
signed in until its socket reconnects. A resumed session is cached for up to 30 seconds per process, which is the
longest another replica can keep honouring one.

**Remember me** makes the cookie outlive the browser; without it the cookie is a session cookie. The session lasts
`ExpireTimeSpan` (14 days) from its last use either way, sliding while it is in use. Bearer tokens carry the session
too, so ending it ends the token.

## Passwords and throttling

**Passwords are hashed with PBKDF2-SHA256 at 600,000 iterations** by default, from the base class library. Choose
bcrypt instead with one option — the format other frameworks write, so hashes imported from them keep working:

```csharp
app.Configure(c => c.Auth.Configure(o =>
{
    o.PasswordHashing = PasswordHashing.Bcrypt;
    o.BcryptWorkFactor = 12;
}));
```

Changing it is safe on a live app: every format is read — Rask's PBKDF2, bcrypt (`$2a$`, `$2b$`, `$2y$`) and ASP.NET
Core Identity's V3 hashes — and a user's hash moves to the configured one the next time they sign in. bcrypt uses
only the first 72 bytes of a password, so with bcrypt on a longer one is refused rather than silently cut.

**Guessing is throttled, never locked out.** After `SignInAttemptsPerMinute` (5) failed sign-ins for one address from
one client, that client is told to wait a minute (`AuthError.TooManyAttempts`, HTTP 429). The account is not locked:
somebody typing your address five times cannot keep *you* out. Registration and reset requests are throttled the
same way. Guesses sent in parallel do not get extra tries: a sign-in counts against the limit from the moment it starts,
so the sixth of six simultaneous wrong passwords is refused like the sixth of six in a row. Behind a proxy, run `UseForwardedHeaders` so the client address is the visitor's rather than the proxy's.

An unknown address costs a password check anyway, a reset request answers the same for every address, and "confirm
your email" is only said after the right password — so no answer tells anybody which addresses have an account.

## Passkeys

**A passkey is another way in, not a replacement for the password.** A signed-in person adds one on `/devices`;
`/login` then offers "Sign in with a passkey", with no email and no password typed. Everything else is unchanged:
the sign-in starts the same `Session` row, shows up on the same device list, and the account keeps its password.

```csharp
await auth.AddPasskeyAsync("MacBook");                          // signed in; runs the browser ceremony
await auth.SignInWithPasskeyAsync(remember: true, returnUrl);   // discoverable — nothing is typed
await auth.RemovePasskeyAsync(passkeyId);

var keys = await Passkey.Read.Where(p => p.UserId == me).ToListAsync();   // list them like sessions
```

**Call these from a click handler.** Browsers only show the passkey dialog for a real gesture. A dismissed dialog
comes back as `AuthError.PasskeyRejected`, never an exception, so the page renders a message.

It is on by default and needs no configuration in development: with no origin configured, a ceremony is held to the
relying party id — its host must be that domain or a subdomain of it — which is the binding that protects the account
whatever port the app is served on. In production behind a proxy, or on more than one subdomain, say where the app is,
and the list becomes exact:

```csharp
app.Configure(c => c.Auth.Configure(o =>
{
    o.PasskeyRelyingPartyId = "example.com";              // a bare domain — no scheme, no port
    o.PasskeyOrigins.Add("https://app.example.com");      // one per origin you serve
}));
```

> **Changing `PasskeyRelyingPartyId` invalidates every passkey already registered.** A passkey is bound to that
> domain and cannot be used on another — which is exactly what makes it unphishable. Set it to the parent domain
> (`example.com`) if one passkey should work across subdomains.

**What the server checks.** Rask verifies WebAuthn itself, on the base class library — `System.Formats.Cbor` for the
CBOR and `ECDsa`/`RSA` for the signature, with no FIDO library. Every ceremony must present the challenge this
server issued (sealed, five minutes, good once), come from an allowed origin, hash to the right relying party, and
report that the user was **present and verified** — so a passkey is two factors: the device, and the biometric or
PIN that unlocked it. ES256 and RS256 keys are accepted and nothing else. The authenticator's signature counter is
checked for clones, except where it stays at zero, which is what a synced passkey reports.

Attestation is `none`: Rask does not verify which *model* of authenticator a person owns, because a site that
forces particular hardware is a site people cannot sign in to from the device they have.

Failures are throttled per client and answer `InvalidCredentials`, exactly as a wrong password does — which of the
checks failed is written only to the log, at debug level.

**Turning them off** hides the button and refuses the endpoints. Passkeys already added stay in the table and work
again the moment it is turned back on:

```csharp
app.Configure(c => c.Auth.Configure(o => o.Passkeys = false));
```

A TypeScript front end has the same three calls — `addPasskey`, `signInWithPasskey`, `removePasskey`, plus
`passkeysSupported()` to gate the button — from the `auth` module.

## Concepts

| Piece | What it is |
|---|---|
| `Passkey` | One registered credential: the account, the credential id, the public key, what the person called it, and when it was last used. Rask adds and removes them; read them like sessions. |
| `IAuth` | The flows: `RegisterAsync` / `SignInAsync` / `SignOutAsync`, `SignOutOtherDevicesAsync` / `SignOutEverywhereAsync`, `AddPasskeyAsync` / `SignInWithPasskeyAsync` / `RemovePasskeyAsync`, plus `SendPasswordResetAsync` / `ResetPasswordAsync` / `ConfirmEmailAsync`. The same injected type on every host — the server implementation validates against the account store and drives the handshake below; the browser one posts to `/api/auth`. |
| `IUserProvider` | Scoped source of the current `ClaimsPrincipal` (`Current`), a `Changed` event, `EnsureLoadedAsync`/`RefreshAsync`, and `IsLoading`. Server: `SessionUserProvider` (seeded from `HttpContext.User`). WASM: `HttpUserProvider`, from `AddRaskAuthClient()`. |
| Injecting `IUserProvider` | Inject it via the constructor and read `.Current` — the never-null `ClaimsPrincipal` for the active render scope. Gate in `Render()` on `provider.Current.Identity?.IsAuthenticated` / `provider.Current.IsInRole(...)`. |
| `Current` (Rask.Data) | The signed-in user with nothing injected — `Current.UserId` / `RequiredUserId` / `Principal` — for code with no constructor to inject into, like a `Product.Create(…)` factory. Set for a live session, every HTTP request and a background job (which runs for the user who enqueued it). See [data.md](data.md#the-current-user--current). |
| `Authorize` component | Headless declarative gate with `Authorized` / `NotAuthorized` / `Authorizing` slots (see below). |
| `ClaimsPrincipal.UserId()` / `SessionId()` | The signed-in user's id (to load the row: `User.Read.Where(u => u.Id == id)`) and the session's id (to mark "this device"). |
| `IAuthSignIn` | Event-handler-only `SignInAsync(principal, returnUrl, persistent)` / `SignOutAsync(returnUrl)`. Server drives the cookie handshake; WASM signs out via `/auth/logout`. |
| `[Authorize]` / `[AllowAnonymous]` | Route-level gating evaluated by `RouteAuthorizationGuard` → redirect to the auth scheme's `LoginPath` (401) or `AccessDeniedPath` (403). |

## The first account is the administrator

The first account to register gets the `admin` role; every one after it gets `user`. That removes the
worst step in self-hosting — "it is deployed, now how do I make an admin?" — with no seeding migration
and no create-admin command. `/_rask`, the operator console, is gated on that role.

It is a single-winner guarantee rather than a race: one row with a constant primary key records the
claim, so two registrations arriving together cannot both take it, on any database provider.

Because an app with an empty user table and an open registration page is a land-grab, the **first**
registration — and only the first — needs a one-time token. It is generated while the instance is
unclaimed and written to the startup log:

```text
warn: Rask.Auth[1]
      This Rask app has no accounts yet. The first registration claims it and becomes the
      administrator, and needs this one-time token: 8f2c…  Claim it at /register.
```

Every registration after that is an ordinary open one. Both behaviours are options:
`c.Auth.Configure(o => o.FirstUserIsAdmin = false)` and `o.RequireFirstRunToken = false` — or
`Rask:Auth:FirstUserIsAdmin` and `Rask:Auth:RequireFirstRunToken` in configuration. To make the token
predictable rather than reading it from the log, set `Rask:Auth:FirstRunToken` (in the environment,
`Rask__Auth__FirstRunToken`).

**The Server cookie handshake.** A WebSocket can't write a `Set-Cookie`, so sign-in is a four-step relay:
`IAuthSignIn.SignInAsync(principal)` (in an event handler) → the framework issues a single-use,
session-bound ticket → the browser `POST`s it to `/_rask/auth/redeem` → the endpoint calls
`HttpContext.SignInAsync` (sets the cookie) → the WS reconnects and re-seeds `SessionUserProvider` from the
now-authenticated `HttpContext.User`. You never touch this directly — just call `SignInAsync`.

## Accounts and tenants

In an app with [multi-tenancy](multi-tenancy.md), the tenant is an outcome of signing in: the user row says
which tenant it belongs to, sign-in puts that on the principal as the `rask:tenant` claim, and every
tenant-scoped read filters by it with nothing passed. A restored session carries the claim too, so a
reconnect comes back in the same tenant.

The accounts table carries a tenant without being partitioned by one, because an administrator belongs to no
tenant and a partitioned table refuses a row without one. That shapes three rules:

- **An address is unique within a tenant**, not across all of them, so the same person can hold an account at
  two companies. With no tenants in play every account has none and the address is simply unique. The index
  folds a missing tenant to one value, so it means the same on SQLite, PostgreSQL and SQL Server.
- **Sign-in refuses an address two tenants hold.** Sign-in has to find the user before it can know their
  tenant, so the lookup spans tenants — and with the address in two of them there is no right answer to guess.
  It is refused, logged as an error for the operator to resolve, and answered as ordinary invalid credentials,
  so the response says nothing about which addresses exist.
- **An administrator carries no tenant claim**, so a tenant-scoped read throws until they say which tenant
  they are acting in (`Tenant.Use(id)`). Reading across every tenant is a deliberate `Tenant.Across()`.

---

## Confirming an address, and resetting a password

Both flows ship on, and both go out through [the mail battery](mail.md) — the same queue the rest of
the app's email uses, so a confirmation survives a restart between "the account exists" and "the email
went out". There is nothing to register: `Rask.Auth` asks for `IMail` when it needs to send.

**Registering sends a confirmation link.** Every time, whether or not confirmation is required, so an
app that starts requiring it later finds its existing accounts already confirmed instead of locking all
of them out at once.

**Confirmation does not block sign-in by default.** Turn it on in one line:

```csharp
app.Configure(c => c.Auth.Configure(o => o.RequireConfirmedEmail = true));
```

It is off by default because a freshly scaffolded app has no SMTP configured. With the gate on, the
first registration would succeed and then be unable to sign in — including yours — and the email needed
to fix it is the one that cannot be sent. In development the mail battery writes each message to
`./mail-pickup` as an `.eml`, so the link is there to open even with no mail server anywhere.

Three scaffolded pages, yours to edit like `/login`:

| Route | What it does |
|---|---|
| `/forgot-password` | Takes an address and emails a link. Answers the same way whether or not that address has an account, so it cannot be used to find out which addresses are registered. |
| `/reset-password` | Where the emailed link lands, carrying `?userId=&token=`. Sets the new password, and ends every session for that account. |
| `/confirm-email` | Where a confirmation link lands. Confirms behind a button, never on arrival: mail scanners and link previewers fetch a link first, and would spend it. |

A completed reset also confirms the address: holding that token proves the same thing the confirmation
link proves. Without it, an account created before `RequireConfirmedEmail` was switched on could reset
its password and still not get in.

The reset **ends every session for the account** — its rows are deleted — so if the reason for the reset was
that somebody else had the password, their next request, and their open page's next handler, finds nobody signed
in rather than staying signed in until a cookie expires.

**The links are signed and single-use.** A token is sealed with Data Protection — the keys the cookie is sealed
with — and fingerprints the state it may change: the password hash for a reset, the address and its confirmation
for a confirm. Using it changes that state, so the same link does not work twice, and a password changed any other
way kills every reset link already sent. Nothing is stored.

**Set `PublicOrigin` behind a proxy.** An emailed link has to be absolute. Rask uses `PublicOrigin`
first, then the current request's own origin — never a forwarded host header, because that is
attacker-controlled on a request that reaches the app directly, and a reset link built from it would
send a working token to a domain of the attacker's choosing.

```csharp
app.Configure(c => c.Auth.Configure(o =>
{
    o.PublicOrigin = "https://app.example.com";   // required behind a proxy
    o.RequireConfirmedEmail = true;
    o.TokenLifetime = 1.Hour;      // what the email promises AND what the token honours
}));
```

From TypeScript, the same three flows are three functions on the shared browser layer:

```ts
import {auth} from './rask/browser'

await auth.sendPasswordReset(email)
await auth.resetPassword(userId, token, password)
await auth.confirmEmail(userId, token)
```

---

## Configuration

Session and account policy live on `AuthOptions`, reached through the battery. Roles, policies and any
additional OIDC scheme are still configured through ASP.NET's own primitives.

```csharp
app.Configure(c => c.Auth.Configure(o =>
{
    o.MinimumPasswordLength = 12;
    o.SignInAttemptsPerMinute = 5;
    o.PasswordHashing = PasswordHashing.Bcrypt;
    o.ExpireTimeSpan = 14.Days;
}));
```

**The battery owns the cookie scheme.** `AddRaskAuth` registers it whatever the app did, makes it the
default, and applies the `AuthOptions` values above it — so an app that also wrote
`AddAuthentication().AddCookie(...)` starts normally and gets Rask's settings rather than a
"Scheme already exists" crash on its first request. For a cookie knob `AuthOptions` does not carry,
configure the same named options *after* `AddRaskAuth`:

```csharp
builder.Services.Configure<CookieAuthenticationOptions>(
    CookieAuthenticationDefaults.AuthenticationScheme, o => o.Cookie.Domain = ".example.com");
```

A few framework defaults are fixed (not configurable knobs):

| Behaviour | Value |
|---|---|
| Initial HTTP GET challenge / forbid | the cookie scheme's `LoginPath` / `AccessDeniedPath` (`AuthOptions.LoginPath` / `AccessDeniedPath`) |
| Client-side route-guard redirect (an in-app nav to a protected route) | `/login` / `/forbidden` — name your login route `/login` to match |
| Sign-in/out redeem ticket lifetime | 30 seconds |

The first row's two paths are `AuthOptions` values, shown here at their defaults:

```csharp
app.Configure(c => c.Auth.Configure(o =>
{
    o.LoginPath = "/login";          // ← where unauthenticated users are challenged (HTTP GET)
    o.AccessDeniedPath = "/forbidden";
    o.CookieName = "rask.auth";      // Secure + HttpOnly + SameSite=Lax are not knobs
}));
```

---

## Declarative gating

The headless `Authorize` component renders exactly one of three slots — no markup of its own — off
the current user (`IUserProvider`):

```csharp
// Shorthand: children are the "authorized" branch (static content, no principal needed).
Authorize.Roles(["admin"])[ AdminPanel() ]

// Full three-slot form. `Authorized` is a delegate handed the current principal (Blazor's
// @context.User), so a greeting reads the name with no injected IUserProvider and no subscription.
Authorize.Roles(["admin", "editor"])// ANY-of; omit for "any authenticated user"
.Authorized(user => Div.Class("panel")[ $"Welcome, {user.Identity!.Name}" ]).NotAuthorized(A.Href("/login")[ "Please sign in" ]).Authorizing(Spinner())                     // shown while the principal/policy resolves
```

- **`Authorized`** is `Func<ClaimsPrincipal, Component>` — it receives the signed-in principal and re-runs
  whenever the gate re-renders (i.e. on `IUserProvider.Changed`), so user-dependent markup stays fresh
  on its own. For static authorized content that ignores the user, use the children-indexer shorthand
  `Authorize(...)[ content ]`.
- **`Roles`** and the authenticated check are synchronous → no flicker.
- **`Policy`** (e.g. `Authorize.Policy("over-18")`) resolves via `IAuthorizationService` in the background;
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

## Identity providers & production hardening

The provider integrations and the hardening reference now live in focused companion pages:

- **[Identity providers](authentication-providers.md)** — Keycloak / OpenID Connect, Auth0, AWS Cognito, and
  Duende IdentityServer.
- **[Production hardening](authentication-hardening.md)** — the hardening reference, running behind a reverse
  proxy, Content-Security-Policy, and the security checklist.


## Decision table

| Question | Choose |
|---|---|
| Server (WS) app, simplest + safest | **Cookie + Server** |
| WASM SPA talking to your own ASP.NET API, simplest + safest | **Cookie + WASM** |
| Static-file WASM SPA against an API on another origin | **Cookie + WASM**, with the API setting the cookie for its own origin — CORS with credentials, `SameSite=None; Secure` |
| Your own accounts, sessions you can list and end | **Rask.Auth** (on by default) |
| An existing ASP.NET Core Identity database | **Rask.Auth** reads its V3 password hashes: copy the rows into `User` and each rehashes on first sign-in |
| Central SSO / social login / corporate IdP | **OIDC** (+ cookie) — Keycloak, Auth0, AWS Cognito, Duende IdentityServer |

See the [`Authorize`](#declarative-gating) component and [Configuration](#configuration) for how each of
these gates content and is configured.
