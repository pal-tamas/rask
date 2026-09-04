# Authentication in Rask

> **In practice:** [Tutorial Ch 3](tutorial/03-orders-and-auth.md) · recipe [require login on a page](recipes.md#require-login-on-a-page) · [cheat sheet](cheatsheet.md).

**Authentication is on by default.** A fresh app can register somebody, sign them in and sign them out
without a line of auth code: accounts are backed by ASP.NET Core Identity, the flows are routed at
`/login`, `/register` and `/logout`, and the first account to register becomes the administrator.

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
(a JWT, Keycloak/OIDC, an existing users table) is still supported: the pages, the guards and the
`Authorize` component are written against `ClaimsPrincipal`, so they do not care where it came from.

## On this page

- [Concepts](#concepts)
- [The first account is the administrator](#the-first-account-is-the-administrator)
- [Confirming an address, and resetting a password](#confirming-an-address-and-resetting-a-password)
- [Configuration](#configuration)
- [Declarative gating — the `Authorize` component](#declarative-gating)
- [Cookie authentication](authentication-cookie.md) — cookie login/session on Server and WASM.
- [JWT authentication](authentication-jwt.md) — bearer tokens on Server, WASM, and standalone WASM.
- [ASP.NET Identity](authentication-providers.md#aspnet-identity)
- [Keycloak / OpenID Connect](authentication-providers.md#keycloak--openid-connect)
- [Other OIDC providers — Auth0, AWS Cognito, Duende IdentityServer](authentication-providers.md#other-oidc-providers)
- [Hardening reference](authentication-hardening.md#hardening-reference)
- [Security checklist](authentication-hardening.md#security-checklist)
- [Decision table](#decision-table)

---

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
`c.Auth.Configure(o => o.FirstUserIsAdmin = false)` and `o.RequireFirstRunToken = false`.

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

Session and account policy live on `AuthOptions`, reached through the battery. Everything ASP.NET owns
— the cookie itself, an additional JWT or OIDC scheme, roles and policies — is still configured through
ASP.NET's own primitives, and an app that registers its own scheme keeps it: the battery notices and
does not register a second one.

```csharp
app.Configure(c => c.Auth.Configure(o =>
{
    o.MinimumPasswordLength = 12;
    o.MaxFailedAccessAttempts = 5;
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
}));
```

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
| Static-file WASM SPA against a separate API (no host of your own) | **Standalone WASM** (JWT in `sessionStorage`) |
| Need a bearer-token API the same identity serves | **JWT** (cookie storage if you can, protected storage if not) |
| Existing user database, password hashing, 2FA | **ASP.NET Identity** (+ cookie) |
| Central SSO / social login / corporate IdP | **OIDC** (+ cookie) — Keycloak, Auth0, AWS Cognito, Duende IdentityServer |

See the [`Authorize`](#declarative-gating) component and [Configuration](#configuration) for how each of
these gates content and is configured.
