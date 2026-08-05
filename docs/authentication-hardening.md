# Authentication — production hardening

Production-hardening reference for [Rask authentication](authentication.md): security headers, running behind
a reverse proxy, Content-Security-Policy, and the pre-ship checklist. For the auth flows themselves, see the
[main authentication guide](authentication.md).


## Hardening reference

| Concern | How Rask handles it |
|---|---|
| **Redeem-ticket TTL** | 30s, fixed. The ticket is single-use, session-bound, 128-bit. |
| **CSRF on `/_rask/auth/redeem`** | The secret session-bound ticket defeats classic CSRF; the endpoint additionally **rejects cross-origin `Origin`/`Referer`** (HTTP 403). |
| **Session identity trust model** | A live session is keyed by an unguessable 128-bit id embedded in the page (same model as a Blazor circuit). The WS `hello` handler binds the session's user to the principal authenticated on that socket, so security rests on the **secrecy of the session id** — serve over HTTPS, don't log it or leak it via `Referer`. Cross-origin pages can't read it (same-origin policy). |
| **Cross-Site WebSocket Hijacking** | CORS doesn't apply to WS handshakes and the upgrade carries the auth cookie, so the `/rask/ws` endpoint **rejects a cross-origin handshake** (HTTP 403) using the same host-only same-origin check as redeem. Clients sending no `Origin` (non-browser) are allowed. |
| **Session-store growth (DoS)** | Each session pins a component tree + DI scope. Sessions are reclaimed shortly after their socket disconnects; set `RaskLiveOptions.MaxSessions` for a hard ceiling (a GET over the cap gets `503` + `Retry-After`). `0` = unlimited (default). Pair with a reverse-proxy rate limit. |
| **Inbound WS frame abuse (DoS)** | Three built-in per-connection bounds the receive loop enforces with sane fixed defaults: a reassembled frame past **8 MB** aborts the socket (bounds a fragmented-frame memory DoS); more than **1000 frames/second** closes it (bounds a small-frame parse-CPU DoS the size cap misses); and more than **512 queued handler dispatches** closes it (backpressure when a client outpaces handler draining or a handler hangs). On a close the client reconnects against the intact session and resumes. These guard a single misbehaving connection — still front the app with a **reverse-proxy / WAF rate limit** to bound connection-count and cross-connection floods. |
| **Sign-out invalidation** | Redeem clears the cookie; the WS reconnect re-seeds `SessionUserProvider` to anonymous. `SessionUserProvider.Clear()` is available for explicit invalidation. |
| **Session expiry → re-auth** | A swept live session pushes `{type:"session",status:"unknown"}`; `rask.js` reloads → fresh GET → route guard challenges to `ChallengePath?returnUrl=…`. |
| **JWT on WebSocket** | Token rides `?access_token=` on the WS URL via `window.Rask.authToken`; pair with `AddJwtBearer`'s `OnMessageReceived`. |
| **Token at rest (JWT/WASM)** | `ProtectedTokenStore` encrypts with Data Protection before storage; the HttpOnly-cookie scheme keeps it out of JS entirely. |

---

## Behind a reverse proxy (ForwardedHeaders)

Rask's anti-CSWSH and redeem-CSRF defenses are a **host-only same-origin check**: the `/rask/ws`
handshake and `/_rask/auth/redeem` compare the request's host to the `Origin` header's host and reject a
mismatch with `403`. Behind a TLS-terminating proxy (nginx, Caddy, a cloud load balancer, Azure App
Service, …) the app receives the request on an internal address, so `HttpContext.Request.Host` is the
**internal** host (e.g. `localhost:8080`) while the browser's `Origin` carries the **public** host
(`app.example.com`). Those don't match, and **every legitimate same-origin WebSocket handshake and
redeem POST is rejected** — auth appears to silently break in production but works locally.

Restore the public host by enabling forwarded headers **before** `UseAuthentication()`/`UseRask()`, so
the rest of the pipeline (including Rask's origin checks) sees the real host and scheme:

```csharp
using Microsoft.AspNetCore.HttpOverrides;

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                       | ForwardedHeaders.XForwardedProto
                       | ForwardedHeaders.XForwardedHost;   // <- the host the origin check needs
    // Trust only your proxy. The defaults clear KnownNetworks/KnownProxies, which drops forwarded
    // headers entirely unless you add the proxy here (or trust a known network range).
    o.KnownProxies.Add(System.Net.IPAddress.Parse("10.0.0.1"));
});

var app = builder.Build();

app.UseForwardedHeaders();   // FIRST — before auth/Rask, so Host/Scheme are corrected upstream
app.UseAuthentication();
app.UseAuthorization();
app.UseRask<App>();
```

> **Make sure the proxy actually sends `X-Forwarded-Host`** (and `X-Forwarded-Proto`). Some proxies
> forward `For`/`Proto` but not `Host` by default. Only trust these headers from a proxy you control —
> an attacker who can reach the app directly could otherwise spoof the host. If you can't use
> `ForwardedHeaders`, host Rask on the same origin the browser sees so the internal host already matches.

---

## Content Security Policy

Rask doesn't emit a `Content-Security-Policy` header — CSP is app- and deployment-specific, so the
host owns it. Rask's runtime is built to work under a **strict** policy: the runtime script and every
scoped asset are served as **external** `<script src>` / `<link rel="stylesheet">` (no inline
`<script>` blocks), and DOM events bind through `data-rask-on-*` attributes + `addEventListener` (no
inline `onclick=` handlers), so you don't need `script-src 'unsafe-inline'`.

Two things do shape the policy:

- **Inline style attributes.** The `Style:` parameter renders `style="…"` attributes, which CSP
  governs via `style-src` — so a strict policy needs `style-src 'self' 'unsafe-inline'`. (`'unsafe-inline'`
  for *style attributes* is far weaker than for scripts; if you avoid `Style:` entirely in favour of
  scoped CSS classes you can drop it.)
- **The WebSocket (Server host).** The live runtime opens a **same-origin** WebSocket to `/rask/ws`,
  covered by `connect-src 'self'`.

A working baseline, set as middleware **before** `UseRask<App>()`:

```csharp
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +          // runtime + scoped scripts are external; no inline JS
        "style-src 'self' 'unsafe-inline'; " + // inline style="" from the Style: parameter
        "img-src 'self' data:; " +
        "connect-src 'self'; " +         // same-origin WebSocket (/rask/ws)
        "base-uri 'self'; " +
        "frame-ancestors 'none'";
    await next();
});
```

**WASM host:** the .NET WebAssembly runtime requires `'wasm-unsafe-eval'` in `script-src`, so use
`script-src 'self' 'wasm-unsafe-eval'`. A standalone (static-file) WASM app sets the same policy via
its host's header config (e.g. `staticwebapp.config.json`, an nginx `add_header`, or a CDN rule)
rather than ASP.NET middleware.

Tighten from there per app: add `upgrade-insecure-requests` on HTTPS, list any third-party CDN/API
origins you actually use, and consider `report-uri`/`report-to` to catch violations before enforcing.

---

## Security checklist

- ☑ Serve auth over **HTTPS** only (`Cookie.SecurePolicy = Always` on `AddCookie`).
- ☑ Cookies: `HttpOnly`, `Secure`, `SameSite=Lax` (or `Strict`).
- ☑ Prefer the **cookie scheme** — the token never reaches JavaScript (immune to XSS token theft).
- ☑ If a token must be in the browser, store it **encrypted** (`ProtectedTokenStore`), never plaintext.
- ☑ Short JWT lifetime + app-driven silent refresh so sessions stay smooth without long-lived tokens.
- ☑ Keep `UseAuthentication()` **before** `UseRask()`.
- ☑ Validate redirect targets — Rask sanitizes the `returnUrl` to local same-origin paths (rejects `//`, `/\`, and backslash/control-char variants).
- ☑ Set a **[Content-Security-Policy](#content-security-policy)** — Rask runs under a strict policy (`script-src 'self'`, plus `'wasm-unsafe-eval'` on WASM); only `style-src` needs `'unsafe-inline'` for `Style:` attributes.
- ☑ Treat the **session id as a bearer secret** — HTTPS only, never logged or placed in URLs that leak via `Referer`.
- ☑ Behind a reverse proxy, wire **ForwardedHeaders** so the host-only same-origin checks (redeem + WS) see the public host.
- ☑ For untrusted-traffic hosts, set `RaskLiveOptions.MaxSessions` and a reverse-proxy rate limit to bound session creation. The receive loop also bounds per-connection inbound-frame size, rate, and handler backlog automatically (see [Hardening reference](#hardening-reference)).
- ☑ Rotate signing keys; manage the Data Protection key ring (persisted, encrypted at rest). `rask new`
  scaffolds the persistence half: the ring is written to `/data/keys`, the volume `rask deploy` mounts,
  because the default ring lives inside the container and **a deploy replaces the container** — new keys,
  and every cookie already issued stops validating, so every user is silently signed out. Override the
  location with `Rask:DataProtection:KeyPath`; encryption at rest is still yours to add.
