# Authentication in Rask

> **In practice:** [Tutorial Ch 3](tutorial/03-orders-and-auth.md) · recipe [require login on a page](recipes.md#require-login-on-a-page) · [cheat sheet](cheatsheet.md).

Rask ships the *plumbing* for authentication — a scoped current-user, a sign-in/out handshake, route
guards, and a declarative gate — and lets you bring any backing store (a cookie, a JWT, ASP.NET Identity,
Keycloak/OIDC, …). This guide shows the complete, copy-pasteable flow for each combination.

## On this page

- [Concepts](#concepts)
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
| `IUserProvider` | Scoped source of the current `ClaimsPrincipal` (`Current`), a `Changed` event, optional `EnsureLoadedAsync`/`RefreshAsync`, and `IsLoading`. Server: `SessionUserProvider` (seeded from `HttpContext.User`). WASM: you supply one (or the anonymous default). |
| Injecting `IUserProvider` | Inject it via the constructor and read `.Current` — the never-null `ClaimsPrincipal` for the active render scope. Gate in `Render()` on `provider.Current.Identity?.IsAuthenticated` / `provider.Current.IsInRole(...)`. |
| `Authorize` component | Headless declarative gate with `Authorized` / `NotAuthorized` / `Authorizing` slots (see below). |
| `IAuthSignIn` | Event-handler-only `SignInAsync(principal, returnUrl)` / `SignOutAsync(returnUrl)`. Server drives the cookie handshake; WASM signs out via `/auth/logout`. |
| `[Authorize]` / `[AllowAnonymous]` | Route-level gating evaluated by `RouteAuthorizationGuard` → redirect to the auth scheme's `LoginPath` (401) or `AccessDeniedPath` (403). |

**The Server cookie handshake.** A WebSocket can't write a `Set-Cookie`, so sign-in is a four-step relay:
`IAuthSignIn.SignInAsync(principal)` (in an event handler) → the framework issues a single-use,
session-bound ticket → the browser `POST`s it to `/_rask/auth/redeem` → the endpoint calls
`HttpContext.SignInAsync` (sets the cookie) → the WS reconnects and re-seeds `SessionUserProvider` from the
now-authenticated `HttpContext.User`. You never touch this directly — just call `SignInAsync`.

---

## Configuration

**Rask has no auth options object of its own** — authentication is configured entirely through ASP.NET's
own primitives. You set the cookie name/flags/expiry and login path on `AddCookie(...)`, the JWT signing key
and token lifetimes on `AddJwtBearer(...)`, and roles/policies on `AddAuthorization(...)`. `AddRask()` takes
no auth configuration.

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
Authorize(Roles: ["admin"])[ AdminPanel() ]

// Full three-slot form. `Authorized` is a delegate handed the current principal (Blazor's
// @context.User), so a greeting reads the name with no injected IUserProvider and no subscription.
Authorize(
    Roles: ["admin", "editor"],                       // ANY-of; omit for "any authenticated user"
    Authorized:    user => Div(Class: "card")[ $"Welcome, {user.Identity!.Name}" ],
    NotAuthorized: A(Href: "/login")[ "Please sign in" ],
    Authorizing:   Spinner())                     // shown while the principal/policy resolves
```

- **`Authorized`** is `Func<ClaimsPrincipal, Component>` — it receives the signed-in principal and re-runs
  whenever the gate re-renders (i.e. on `IUserProvider.Changed`), so user-dependent markup stays fresh
  on its own. For static authorized content that ignores the user, use the children-indexer shorthand
  `Authorize(...)[ content ]`.
- **`Roles`** and the authenticated check are synchronous → no flicker.
- **`Policy`** (e.g. `Authorize(Policy: "over-18")`) resolves via `IAuthorizationService` in the background;
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
