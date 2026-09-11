# Authentication in Rask

> **In practice:** [Tutorial Ch 3](tutorial/03-orders-and-auth.md) · recipe [require login on a page](recipes.md#require-login-on-a-page) · [cheat sheet](cheatsheet.md).

**Authentication is on by default.** A fresh app can register somebody, sign them in and sign them out
without a line of auth code: accounts are backed by ASP.NET Core Identity, the flows are routed at
`/login`, `/register` and `/logout`, and the first account to register becomes the administrator.

**The built-in pages are drawn with [daisyUI](ui-kit.md)**, so they match the rest of a scaffolded app
rather than looking like something bolted on. They carry their own stylesheet — compiled at
`Rask.Auth`'s build from the six pages themselves, so it is a fraction of the kit's size — because a
page's class names live in a compiled assembly that no application's Tailwind can scan. That is what
keeps the promise that these pages render on an app with no CSS of its own, and it is scoped to their
own wrapper, so referencing the package cannot repaint your application. An app that wants them in its
own colours declares its own page at the same route, which takes precedence.

The API is the same on every host. A component injects `IAuth` to move somebody between signed-out and
signed-in, and `IUserProvider` to read who that is — identical on the Server host, in WebAssembly, and
inside an island. A TypeScript front end and a meta framework's Node process reach the same flows
through `/api/auth`.

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

- [Concepts](#concepts)
- [The first account is the administrator](#the-first-account-is-the-administrator)
- [Confirming an address, and resetting a password](#confirming-an-address-and-resetting-a-password)
- [Configuration](#configuration)
- [Declarative gating — the `Authorize` component](#declarative-gating)
- [Cookie authentication](authentication-cookie.md) — cookie login/session on Server and WASM.
- [ASP.NET Identity](authentication-providers.md#aspnet-identity)
- [Keycloak / OpenID Connect](authentication-providers.md#keycloak--openid-connect)
- [Other OIDC providers — Auth0, AWS Cognito, Duende IdentityServer](authentication-providers.md#other-oidc-providers)
- [Hardening reference](authentication-hardening.md#hardening-reference)
- [Security checklist](authentication-hardening.md#security-checklist)
- [Decision table](#decision-table)

---

## Your `User`

Rask ships no user class. Your app declares one — `rask new` writes it into
`Features/Shared/User.cs` — and that is the account:

```csharp
using Microsoft.AspNetCore.Identity;

public class User : IdentityUser
{
}
```

Everything an account already has comes from Identity: the password hash, the security stamp, the
lockout counters, the confirmation flags, and `ConcurrencyStamp`. Add the columns your app needs —
a display name, a locale, a team id — then `rask db add AddUserColumns && rask db update`.

**Nothing has to name it.** A source generator finds the one `IdentityUser` subclass in your project and
wires Identity to it, so `AddRaskAuth()` and `modelBuilder.AddRaskAuth()` take no type argument. Two user
types is [RASK074](diagnostics.md#rask074) — with two, picking either would map one set of account tables
and silently strand the other. Declare none and you have no accounts, and auth is not wired.

An app that would rather be explicit can be: `AddRaskAuth<AppDbContext, User>()` and
`modelBuilder.AddRaskAuth<User>()` still exist.

### Audit stamps on an account

Accounts take the same convention as every [model](data.md): add `ITimestamped` and `CreatedAt` /
`UpdatedAt` are stamped on every write, as shadow columns unless you declare them.

```csharp
public class User : IdentityUser, ITimestamped
{
}
```

**Do not add `IVersioned`.** Identity already maintains `ConcurrencyStamp` as its optimistic-concurrency
token, and a second token on the same row is a race rather than a guard.

Your `User` is an Identity type, not a `Model<TId>` — C# has single inheritance, so it cannot be both.
That means no `User.Where(…)` static surface: accounts go through `UserManager<User>`, which is the right
tool for them anyway, since it owns password hashing, lockout and the security stamp. Your own models
reference an account by its key, which Identity types as a `string`:

```csharp
public sealed class Order : Model<Guid>, ITimestamped
{
    public string OwnerId { get; private set; } = "";   // AspNetUsers.Id
}
```

## Concepts

| Piece | What it is |
|---|---|
| `IAuth` | The flows: `RegisterAsync` / `SignInAsync` / `SignOutAsync`, plus `SendPasswordResetAsync` / `ResetPasswordAsync` / `ConfirmEmailAsync`. The same injected type on every host — the server implementation validates against the account store and drives the handshake below; the browser one posts to `/api/auth`. |
| `IUserProvider` | Scoped source of the current `ClaimsPrincipal` (`Current`), a `Changed` event, `EnsureLoadedAsync`/`RefreshAsync`, and `IsLoading`. Server: `SessionUserProvider` (seeded from `HttpContext.User`). WASM: `HttpUserProvider`, from `AddRaskAuthClient()`. |
| Injecting `IUserProvider` | Inject it via the constructor and read `.Current` — the never-null `ClaimsPrincipal` for the active render scope. Gate in `Render()` on `provider.Current.Identity?.IsAuthenticated` / `provider.Current.IsInRole(...)`. |
| `Authorize` component | Headless declarative gate with `Authorized` / `NotAuthorized` / `Authorizing` slots (see below). |
| `IAuthSignIn` | Event-handler-only `SignInAsync(principal, returnUrl)` / `SignOutAsync(returnUrl)`. Server drives the cookie handshake; WASM signs out via `/auth/logout`. |
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

Three built-in pages, overridable exactly like `/login` by declaring your own route:

| Route | What it does |
|---|---|
| `/forgot-password` | Takes an address and emails a link. Answers the same way whether or not that address has an account, so it cannot be used to find out which addresses are registered. |
| `/reset-password` | Where the emailed link lands, carrying `?userId=&token=`. Sets the new password, and signs out every other session for that account. |
| `/confirm-email` | Where a confirmation link lands. Confirms on arrival — the click in the inbox was the deliberate act. |

A completed reset also confirms the address: holding that token proves the same thing the confirmation
link proves. Without it, an account created before `RequireConfirmedEmail` was switched on could reset
its password and still not get in.

The reset **ends every other session for the account**, not just the one that asked. Identity rolls the
security stamp, and Rask revalidates it on every socket reconnect and before every handler dispatch — so
if the reason for the reset was that somebody else had the password, their open page stops working
rather than staying signed in until its cookie expires.

**Set `PublicOrigin` behind a proxy.** An emailed link has to be absolute. Rask uses `PublicOrigin`
first, then the current request's own origin — never a forwarded host header, because that is
attacker-controlled on a request that reaches the app directly, and a reset link built from it would
send a working token to a domain of the attacker's choosing.

```csharp
app.Configure(c => c.Auth.Configure(o =>
{
    o.PublicOrigin = "https://app.example.com";   // required behind a proxy
    o.RequireConfirmedEmail = true;
    o.TokenLifetime = TimeSpan.FromHours(2);      // what the email promises AND what the token honours
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
    o.MaxFailedAccessAttempts = 5;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
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

- **[Identity providers](authentication-providers.md)** — ASP.NET Identity, Keycloak / OpenID Connect,
  Auth0, AWS Cognito, and Duende IdentityServer.
- **[Production hardening](authentication-hardening.md)** — the hardening reference, running behind a reverse
  proxy, Content-Security-Policy, and the security checklist.


## Decision table

| Question | Choose |
|---|---|
| Server (WS) app, simplest + safest | **Cookie + Server** |
| WASM SPA talking to your own ASP.NET API, simplest + safest | **Cookie + WASM** |
| Static-file WASM SPA against an API on another origin | **Cookie + WASM**, with the API setting the cookie for its own origin — CORS with credentials, `SameSite=None; Secure` |
| Existing user database, password hashing, 2FA | **ASP.NET Identity** (+ cookie) |
| Central SSO / social login / corporate IdP | **OIDC** (+ cookie) — Keycloak, Auth0, AWS Cognito, Duende IdentityServer |

See the [`Authorize`](#declarative-gating) component and [Configuration](#configuration) for how each of
these gates content and is configured.
