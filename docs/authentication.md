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
inside an island. Any other client reaches the same flows through `/api/auth`.

## Two packages, one battery

| Package | Use it when | What it adds |
| --- | --- | --- |
| `Rask.Auth.Api` | The host renders **no** Rask components — a plain ASP.NET app or API | Accounts and sessions, the `/api/auth` endpoints, the cookie, bearer tokens, the account lifecycle |
| `Rask.Auth` | The app **is** a Rask app — the `server` and `wasm` templates | All of the above, plus `IAuth` for components and email bodies written as components; `rask new` adds the pages |

Reference one or the other, never both: `Rask.Auth` already contains `Rask.Auth.Api`. Both put their
types in the `Rask.Auth` namespace and both call the battery `AddRaskAuth` / `MapRaskAuth`, so moving an
app from one lane to the other changes a `PackageReference` and nothing else.

The split exists because `Rask` — the core, with the renderer — is a dependency of the hosts that render
components (`Rask.Server`, `Rask.Wasm`) and of nothing else, so a plain ASP.NET host
ships no copy of it. Until #1069 the accounts battery reached for Core on every host, which meant a host
without it could not start at all: the assembly was simply absent and the process aborted before `Main`,
after a build that succeeded. `Rask.Auth.Api` is the battery with that
dependency removed; it speaks the wire contract in `Rask.Wire` — the `/api/auth` paths, the request and
response shapes, `AuthResult` — which the browser-side `Rask.Auth.Client` also takes, so both halves
agree without either one carrying the renderer.

What you give up on `Rask.Auth.Api` is exactly what needs a renderer: no `IAuth` (there are no components to
inject it into) — the front end owns the sign-in UI, which it does on every lane anyway. The
endpoints, the options, the roles and the emails are the same code.

```csharp
public sealed partial class SignIn(IAuth auth) : Component
{
    private async Task Submit(Credentials c) =>
        await auth.SignIn(c.Email, c.Password, returnUrl: "/");
}

public sealed partial class Header(IUserProvider users) : Component
{
    protected override Component? Render() =>
        Authorize
            .NotAuthorized(NavLink.Href(Routes.LoginPage())["Sign in"])
            .Authorized(user => Span[$"Hi, {user.Identity?.Name}"]);
}
```

With nothing injected, the static `Auth` does the same from any handler, render or request:
`await Auth.SignIn(c.Email, c.Password)`, `await Auth.SignOut()`. Outside any work in progress (a constructor, a
background thread) it throws and says to inject `IAuth` there instead.

To do without it, write `app.Configure(c => c.Auth.Off())` in `Program.cs` (a hand-wired host drops its
`AddRaskAuth` line instead). Bringing your own store or an external provider
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
rask deploy --env "Rask:Auth:BearerSigningKey=…"                           # deployed
```

The battery reads `Rask:Auth` itself. A callback — `app.Configure(c => c.Auth.Configure(o => …))`, or
`AddRaskAuth<AppDbContext>(o => …)` on a hand-wired host — runs after the section and wins.

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
- [What ties a live page to its user](#what-ties-a-live-page-to-its-user)
- [Passwords and throttling](#passwords-and-throttling)
- [Passkeys](#passkeys)
- [Concepts](#concepts)
- [The first account is the administrator](#the-first-account-is-the-administrator)
- [Accounts and tenants](#accounts-and-tenants)
- [Confirming an address, and resetting a password](#confirming-an-address-and-resetting-a-password)
- [Configuration](#configuration)
- [Declarative gating — the `Authorize` component](#declarative-gating)
- [`[Authorize]` on an event handler](#authorize-on-an-event-handler)
- [A page the user may not open](#a-page-the-user-may-not-open)
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
`rask db add AddUserColumns` — the app applies it when it starts.

**It is an [aggregate](data.md) like any other.** `Authenticatable` derives from
`Aggregate<Guid, Timestamps, Versioned, Model, Deletable>`, so a `User` has the reads, the writes, a generated
`UserModel` for a profile form (never with the credentials on it), a `Version` and domain events. It does **not**
soft-delete: soft delete is [a trait it does not name](data.md#choosing-what-a-table-carries),
and a deleted account has to free its address for the person to sign up again, so deleting a user removes the row.
`Session` is the one Rask.Auth table that opts in (below):

```csharp
var me = users.Current.UserId() is { } id
    ? await User.Where(u => u.Id == id).First(CancellationToken)
    : null;
await User.Update(id, u => u.Rename(name));
await User.Update(id, u => u.GrantRole("editor"));
```

Other aggregates refer to a user by id — `public Guid OwnerId { get; private set; }` — rather than holding one.

**Set your own columns while registering.** `IAuth.Register` takes a lambda that runs on the new user
before it is saved, in the same insert; the scaffolded register page uses it for the display name:

```csharp
await auth.Register(model.Email, model.Password, (User user) => user.Rename(model.DisplayName), ReturnUrl);
```

**Give it claims of its own.** The principal carries the id, the name, the address, the roles, the session and
the tenant. Anything else your pages or policies read off the signed-in user, the user says itself:

```csharp
public sealed class User : Authenticatable
{
    public int LegacyId { get; private set; }

    protected override IEnumerable<Claim> Claims() =>
        [new("legacy_id", LegacyId.ToString(CultureInfo.InvariantCulture))];
}
```

They are on the principal a sign-in issues, in a bearer token, and on a session each time it is loaded again —
so a live page still has them after a reconnect, and a change to the row reaches a signed-in user within half
a minute. A claim the account issues itself is not yours to give: returning a role, the id or the tenant
throws, naming it. Roles go through `GrantRole`.

**Nothing has to name it.** A source generator finds the one `Authenticatable` in your project, so
`AddRaskAuth()` and `modelBuilder.AddRaskAuth()` take no type argument. Two user types is
[RASK074](diagnostics.md#rask074). Declare none and you have no accounts, and auth is not wired. An app that
would rather be explicit can be: `AddRaskAuth<AppDbContext, User>()` and `modelBuilder.AddRaskAuth<User>()`.

## Sessions and devices

**Each signed-in device is a row.** Signing in starts a `Session` (the user, the address and browser it came from,
when it was last seen, when it expires, whether it is remembered) and the cookie carries only that session's id,
sealed. Every request loads the session and rebuilds the user's claims from it, so a role granted or removed shows
up without signing in again. Signing out ends the row — `Session` names `SoftDelete` on its base, so an ended
session is stamped rather than removed, and the device list can still say "signed out yesterday". A sweep removes
the rows of sessions that ended or expired more than a day ago, once an hour and when the host starts.

```csharp
var devices = await Session.Where(s => s.UserId == me)
                                .OrderByDescending(s => s.LastSeenAt);

await auth.SignOutOtherDevices();   // every session but this one
await auth.SignOutEverywhere();     // this one too
```

The scaffolded `/devices` page lists them and has the button. An ended session stops working on that device's
**next request** — and on a live page, at its **next event**: the Server host asks about a page's session before
every handler and every navigation (`ISessionRevalidator`), so a page left open for hours goes to the sign-in page
at its next action rather than staying signed in until its socket reconnects. The row is read each time and
nothing is cached between two reads, on any replica — see
[what ties a live page to its user](#what-ties-a-live-page-to-its-user).

**Remember me** makes the cookie outlive the browser; without it the cookie is a session cookie. The session lasts
`ExpireTimeSpan` (14 days) from its last use either way, sliding while it is in use. Bearer tokens carry the session
too, so ending it ends the token.

## What ties a live page to its user

A page on the Server host is a session that outlives the request that opened it. An app that renders a page per
request gets "who is this, now?" from the cookie on every request. A socket carries the cookie once. So Rask asks
again where a request would have: **every event of a live page acts for the user as they are at that moment** —
their rights, their tenant — or does not act at all.

**Who it belongs to.** The session's user is `HttpContext.User` of the page's `GET`, and that is all Rask knows
about them. Every later connection — the socket, the HTTP fallback's stream and each of its posts, an upload, a
download — must carry the same user (the same `ClaimTypes.NameIdentifier`, or `ClaimTypes.Name` when there is
none) or is answered as if the session did not exist. A signed-in user with neither claim owns nothing and is
refused every time. A session opened by nobody is driven by whoever holds its id, signed in or not. One whose
user was signed out is its former user's or nobody's: another signed-in user is refused.

**What is asked, and when.** Register an `ISessionRevalidator` and Rask asks it who the session's user is now:

| When | What happens |
|---|---|
| a connection attaches (a socket's `hello`, a reconnect, an HTTP stream), and a post of the HTTP fallback brings another principal | asked about the principal the connection carries **before** the session is given it: the session only ever holds the answer, and nothing rendered or dispatched sees what the cookie merely claims. Given to it at once, or [behind the handler that is running](#what-ties-a-live-page-to-its-user) |
| before every handler (an event, a submit, a `files` event) and before every navigation | asked, with no interval in which an event is taken on trust. The events of one frame — what one task of the browser produced — ask once between them, and again when the frame has run for more than a second |
| before a call a page's script makes into the server (`DotNet.invokeMethodAsync`, a `dotNetInvoke` frame) | asked unless something asked within the last second, and not made at all for a session whose sign-in ended |
| before a render the server starts itself (a timer, a followed service, a data change) | asked unless something asked within the last **second**. This second is the one time bound there is, and it is here because such a render is not a reader's action: a page that repaints twenty times a second would otherwise read the user's row twenty times a second |

The answer to a call the server made into the page (`jsResult`) is an answer, not an act, and is taken as it
comes. An upload and a download are requests of their own, checked against the principal that request carries.

**A handler finishes as the user it started as.** A credential that arrives while a handler of that page runs —
a reconnect, a post of the HTTP fallback — is asked about at once and adopted when what runs has finished, ahead
of every event read after it. So a handler reads and writes as one user in one tenant from its first line to its
last, and nothing is rendered for the new principal before the session has adopted it; the handler's own render
is still the old one's. The connection itself is attached without waiting — the handler may be waiting for that
very connection, for the answer to a call into the page's script. When two arrive during one handler the later
is the one adopted, and one that was let in against a user the session no longer holds by then is dropped. With
nothing running the session adopts it as the connection attaches.

It is handed the principal the session holds and answers one of three ways:

| It returns | The session |
|---|---|
| the same principal — the instance it was handed, or one with the same claims | goes on. Handing the instance back costs the host nothing: no allocation, no comparison |
| a principal with other claims — a role removed, another tenant | takes it. The event runs under it, and the **whole page renders again**, so an `Authorize` gate, a route's guard and anything that reads `IUserProvider.Current` as it renders show the truth |
| `null` — or a principal for **somebody else** | has no user any more, and [goes to the sign-in page](#when-the-user-is-gone). A session never changes hands through a revalidator; an answer that names another user is logged once as a warning |

One that throws refuses the event: nothing runs, the failure is logged on `Rask.Live`, and the reader's next
action asks again. A connection that cannot be asked about is closed with nothing adopted from it, and the tab's
reconnect asks again. An answer that arrives after the session has taken another principal — a connection
attached meanwhile — is about somebody it no longer acts for, and changes nothing.

**A sliding sign-in is kept alive by its user, not by its page.** `Rask.Auth` moves a session's expiry when a
request or a reader's action resumes it. A question asked for a render the server started itself reads the row
and moves nothing, so a page with a clock on it, left open in an empty room, expires when its sign-in does.

**A token issued again is taken quietly.** When the browser's credential is replaced by one that says the same
— a refreshed token with a later `exp` — the session holds the new one from the next connection, post or
[question the tab asks](#when-the-browsers-cookie-changes), and tells nobody: nothing changed. So a revalidator
that reads `exp`, like the one below, reads the new date, and a user whose token was refreshed in time is not
signed out when the first one lapses.

What counts as "other claims": every claim except the ones a token carries about itself — `exp`, `iat`, `nbf`,
`jti`, `auth_time`, `nonce`, `at_hash`, `c_hash`. A token issued again for the same user with the same rights
changes nothing; a flipped flag, another tenant or another sign-in's id does.

`Rask.Auth` registers a revalidator that reads the session's row and its user's — every time, with nothing
cached between two reads, so `SignOutEverywhere()`, a removed role or a deleted user is what the next event on
every replica sees. A host with its own sign-in registers its own:

```csharp
builder.Services.AddSingleton<ISessionRevalidator, TokenRevalidator>();

internal sealed class TokenRevalidator(IAccounts accounts, TimeProvider clock) : ISessionRevalidator
{
    public async ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        // The token the socket was opened with has lapsed: a request would have been refused by now.
        if (!long.TryParse(principal.FindFirstValue("exp"), out var expires)
            || DateTimeOffset.FromUnixTimeSeconds(expires) <= clock.GetUtcNow())
        {
            return null;
        }

        var account = await accounts.Find(principal.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken);
        if (account is not { Active: true })
        {
            return null;
        }

        // Unchanged: hand back what was handed in. Changed: the principal as the store has it now.
        return account.IsAdmin == principal.HasClaim("isAdmin", "true") ? principal : account.ToPrincipal(principal);
    }
}
```

It runs on the event's own path, so it costs what you make it cost: one indexed read is the usual price, and the
same one a request through the app's authentication would pay. An app that signs users in and registers none is
told so once as it starts, as a warning on `Rask.Live`: its open pages cannot learn that a sign-in ended.

**No remount.** Taking another principal renders the page; it mounts nothing. A component keeps what it holds
— a field half typed, a list it loaded, a flag it worked out in `OnMount`. So read `IUserProvider.Current` where
the decision is made — in the handler that saves, in `Render` — and gate a page with `[Authorize]`, a policy or
`Authorize`, which Rask re-evaluates, rather than with a check in `OnMount`. What the session's
[queries](query.md) had read for the user before is dropped and read again.

**The tenant is the user's current one too.** A tenant read from the principal's `rask:tenant` claim follows the
principal. One named by an `AddRaskTenant` resolver is asked again at each event, and as the page's address moves
— see [the resolver](multi-tenancy.md#from-the-request-a-resolver). A tab opened before its user chose another
tenant works in the chosen one from its next action. A row the page still holds from before does not come along:
it [belongs to the tenant it was loaded in](multi-tenancy.md#writes-stamp-it-and-it-never-moves), and saving it
in the new one is refused — load it again.

### When the browser's cookie changes

A revalidator sees what your store knows. It cannot see what only the browser holds: that another tab signed
out, or that some page of the site — yours, or an older application in the same host — issued the token again
with another tenant in it. An open socket never sees that cookie.

So the tab asks. A request under the app's path base, `/_rask/auth/who/{session}`, is authenticated like any
other — your own cookie-reading event runs for it — and answers only whether the principal it carries is the one
the tab's connection opened with. No claim is in the answer, a session that does not exist and one that is not
the caller's answer alike, it is refused cross-site and `no-store`. When the answer is anything but "the same",
the tab drops its connection and opens it again **before it sends anything**, so the upgrade carries the cookie
as it is now:

| The new cookie names | The tab |
|---|---|
| the same user, with other claims | goes on, under them: the session adopts the principal — once a handler still running has finished — the page renders again, and what the reader typed or pressed meanwhile is sent afterwards |
| another user | loads the page again, as them |
| nobody | [goes to the sign-in page](#when-the-user-is-gone) |

It asks when the tab becomes the reader's again — it turns visible, its window takes the focus, it comes back
from the back/forward cache — and when another tab of the browser says the credential changed, which a tab does
after a sign-in or a sign-out through Rask and after finding a change itself (a tab that was told does not tell
on, so tabs do not wake each other in a circle; one that asks too often is told to ask later, holds what it has
to send, and asks again). A reader can only start something
in a tab they are looking at, and "changed it over there, came back here" always passes through one of those.
What they cannot see is bounded: an answer is trusted for **30 seconds** in the window that has the focus and
**2 seconds** in one that does not, and the first thing sent after that waits for a new one. Those are the longest
a reader's action can run under a cookie the browser has since replaced, and only when it was replaced without
the window ever losing the focus — a script in another frame, say.

Nothing in your app takes part in this. A cookie set by a plain endpoint of your own is found the same way as
one set by `auth.SignIn(…)`; going through Rask's sign-in only makes the other tabs ask a moment sooner. The
HTTP fallback needs none of it: each post carries the cookie, and the session adopts the principal it brings.

### When the user is gone

One behaviour on every page, guarded or not. A session that **had** a user and has none any more — the
revalidator answered `null`, the cookie expired, they signed out elsewhere — goes to the sign-in page with the
way back: `/login?returnUrl=…`, the address the route guard has always used. No message is shown; the page says
what is needed. An open page is navigated there in place. A tab whose connection was refused loads its page again
and is redirected there by that request: the tab marks the reload with a cookie that lasts thirty seconds and
names the app it is for — `__Host-rask-ended` over https, which a browser takes from this host alone, so a
neighbouring subdomain cannot plant it — and the server believes it once, for an anonymous request to that app,
and removes it.

The sign-in page need not be the app's. With a page of its own at `/login` the app shows it in place — at
`/uj/login` under `MapRask<App>(pathBase: "/uj")` — and the way back is the page's address inside the app
(`returnUrl=%2Fdestinations…`). Without one, Rask goes where the host's authentication says its sign-in page is:
the `LoginPath` of the default challenge scheme, when that is a cookie scheme (`.AddCookie(o => o.LoginPath =
"/login")`). Its router has nothing there to show, so the page is *loaded* from there instead of navigated in
place, the reloading tab is redirected there just the same, and the way back is the page's full address under the
scheme's `ReturnUrlParameter`: `/login?ReturnUrl=%2Fuj%2Fdestinations%3Fpage%3D2`, which the host's sign-in can send
the reader back to as it is. A host that signs in with a JWT bearer token read from a cookie has no scheme that names a
login page — it says where it is with `RaskServerOptions.SignInPath` (`Rask:Server:SignInPath`), the case of an old
front end that serves `/login` beside the mounted app:

```csharp
builder.Services.AddRask(configureServer: o => o.SignInPath = "/login");
```

The way back there is `/login?returnUrl=%2Fuj%2Fdestinations%3Fpage%3D2`. The order is fixed: the app's own page at
`/login` first, then the cookie scheme's `LoginPath` — which wins over `SignInPath` when both are set — then
`SignInPath`. An app with none of them has nowhere to send them: Rask cannot know who serves an address
outside its routes, so it does not guess — the open page is drawn again for nobody under its own guard, and the
reloading tab is served the page it asked for.

A visitor who never signed in is not part of this: nothing is asked about them, and a public page goes on
serving them.

### On a host you wire yourself

All of it rests on one thing: `HttpContext.User` must be the user on the page's `GET`, on the socket's upgrade
*and* on `/_rask/auth/who/…`, which is the same pipeline — `UseAuthentication()` with a default scheme, or a
middleware that sets `HttpContext.User`, ahead of `MapRask`. A user the app works out some other way — its own
scoped service filled from the request, a second scheme authenticated by hand inside a page — is one Rask never
sees: the session then belongs to nobody, and its id is the only thing guarding it.

A page's own connections are requests like any other, and they have to be signed in the same way as the page.
`MapRask<App>(pathBase: "/app")` puts them under that prefix as ordinary route patterns — it does not move
anything into `Request.PathBase`, so `Request.Path` is the whole address:

| Request | Path under `pathBase: "/app"` |
|---|---|
| The socket | `/app/rask/ws` |
| The HTTP fallback | `/app/_rask/stream/{session}`, `/app/_rask/send/{session}`, `/app/_rask/leave/{session}` |
| Files | `/app/_rask/upload/{session}`, `/app/_rask/download/{session}/{token}` |
| The sign-in relay, and the question a tab asks | `/app/_rask/auth/redeem`, `/app/_rask/auth/who/{session}` |

So a token read from a cookie is read for the prefix, not for "a page":

```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        if (context.Request.Path.StartsWithSegments("/app") && context.Request.Cookies.TryGetValue("token", out var token))
        {
            context.Token = token;
        }

        return Task.CompletedTask;
    },
};
```

and the cookie's `Path` is that prefix or wider. An event that reads the cookie only for a `GET` of a page, or a
cookie scoped to the page's own address, signs the page in and leaves its socket anonymous: the session is its
user's, the socket is nobody's, and the socket is refused and the tab reloads. A principal also needs a
`ClaimTypes.NameIdentifier` (or `ClaimTypes.Name`) on those requests, not only on the page's.

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
your email" is only said after the right password — so signing in and recovering an account tell nobody which
addresses have one.

**Registering does, unless `RequireConfirmedEmail` is on.** By default a new account is signed in on the spot, so a
taken address has to answer differently: `AuthError.DuplicateAccount`, "an account with that email already exists".
That is the friendlier form and the throttle slows a list being walked through it, but it does not stop one. With
`Rask:Auth:RequireConfirmedEmail` on, a taken address answers exactly as a new one does — "confirm your email" —
the owner gets a "you already have an account" mail (`AlreadyRegisteredSubject`, body from `IAuthEmailBodies`)
with a link to sign in, and registration attempts are throttled per address and client, taken or not. Turn it on
where who has an account is itself sensitive; it needs a working mailer and `PublicOrigin`.

That makes one request say nothing; it does not make an address unknowable. Somebody who registers an
address and then signs in with the password they just chose is told "confirm your email" if the
registration created the account and "wrong email or password" if it already existed. Each such probe
costs a real account and a confirmation mail to the address, and is throttled per address — a list
cannot be walked quietly — but an app for which even that is too much should not offer open
registration at all.

## Passkeys

**A passkey is another way in, not a replacement for the password.** A signed-in person adds one on `/devices`;
`/login` then offers "Sign in with a passkey", with no email and no password typed. Everything else is unchanged:
the sign-in starts the same `Session` row, shows up on the same device list, and the account keeps its password.

```csharp
await auth.AddPasskey("MacBook");                          // signed in; runs the browser ceremony
await auth.SignInWithPasskey(remember: true, returnUrl);   // discoverable — nothing is typed
await auth.RemovePasskey(passkeyId);

var keys = await Passkey.Where(p => p.UserId == me);   // list them like sessions
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

## Concepts

| Piece | What it is |
|---|---|
| `Passkey` | One registered credential: the account, the credential id, the public key, what the person called it, and when it was last used. Rask adds and removes them; read them like sessions. |
| `IAuth` | The flows: `Register` / `SignIn` / `SignOut`, `SignOutOtherDevices` / `SignOutEverywhere`, `AddPasskey` / `SignInWithPasskey` / `RemovePasskey`, plus `SendPasswordReset` / `ResetPassword` / `ConfirmEmail`. The same injected type on every host, or the static `Auth` facade with nothing injected (`await Auth.SignIn(email, password)`) — the server implementation validates against the account store and drives the handshake below; the browser one posts to `/api/auth`. |
| `IUserProvider` | Scoped source of the current `ClaimsPrincipal` (`Current`), a `Changed` event, `EnsureLoaded`/`Refresh`, and `IsLoading`. Server: `SessionUserProvider` (seeded from `HttpContext.User`). WASM: `HttpUserProvider`, from `AddRaskAuthClient()`. |
| Injecting `IUserProvider` | Inject it via the constructor and read `.Current` — the never-null `ClaimsPrincipal` for the active render scope. Gate in `Render()` on `provider.Current.Identity?.IsAuthenticated` / `provider.Current.IsInRole(...)`. |
| `Current` (Rask.Data) | The signed-in user with nothing injected — `Current.UserId` / `RequiredUserId` / `Principal` — for code with no constructor to inject into, like a `Product.Create(…)` factory. Set for a live session, every HTTP request and a background job (which runs for the user who enqueued it). See [data.md](data.md#the-current-user--current). |
| `Authorize` component | Headless declarative gate with `Authorized` / `NotAuthorized` / `Authorizing` slots (see below). |
| `ClaimsPrincipal.UserId()` / `SessionId()` | The signed-in user's id (to load the row: `User.Where(u => u.Id == id)`) and the session's id (to mark "this device"). |
| `IAuthSignIn` | Event-handler-only `SignIn(principal, returnUrl, persistent)` / `SignOut(returnUrl)`. Server drives the cookie handshake; WASM signs out via `/auth/logout`. |
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
`IAuthSignIn.SignIn(principal)` (in an event handler) → the framework issues a single-use,
session-bound ticket → the browser `POST`s it to `/_rask/auth/redeem` → the endpoint calls
`HttpContext.SignInAsync` (sets the cookie) → the WS reconnects and re-seeds `SessionUserProvider` from the
now-authenticated `HttpContext.User`. You never touch this directly — just call `SignIn`.

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

**A reset takes back every way in.** Besides ending every session, a completed reset removes every passkey on the
account — one added by whoever registered the address first, or knew the old password, would otherwise still sign
them in. For the same reason a passkey can only be added once the address is confirmed; before that the attempt
answers `AuthError.EmailNotConfirmed`. With `RequireConfirmedEmail` on, registering creates the account but signs
nobody in until the emailed link is followed.

**Set `PublicOrigin` in production.** An emailed link has to be absolute, and Rask builds it from `PublicOrigin`
(`Rask:Auth:PublicOrigin`). Only in Development does it fall back to the current request's origin: anywhere else the
request's `Host` header is whatever the sender typed, and a reset link built from it would mail the victim a working
token on the attacker's domain. So in production, with `PublicOrigin` unset, no confirm or reset email is sent and
the log says why. `rask deploy --domain` sets it to `https://<domain>` for you.

```csharp
app.Configure(c => c.Auth.Configure(o =>
{
    o.PublicOrigin = "https://app.example.com";   // required outside Development
    o.RequireConfirmedEmail = true;
    o.TokenLifetime = 1.Hour;      // what the email promises AND what the token honours
}));
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
| Initial HTTP GET challenge | the cookie scheme's `LoginPath` (`AuthOptions.LoginPath`) |
| Client-side route-guard redirect (an in-app nav to a protected route, nobody signed in) | `/login` — name your login route `/login` to match |
| A signed-in user the route forbids | stays on the address: [the page says so inside its layout](#a-page-the-user-may-not-open), answered 403 |
| Sign-in/out redeem ticket lifetime | 30 seconds |

The first row's path is an `AuthOptions` value, shown here with the others at their defaults. `AccessDeniedPath`
is where the cookie scheme sends a forbidden request to an endpoint that is not a Rask page (a controller, a
minimal API); a Rask page answers that request itself, in its layout:

```csharp
app.Configure(c => c.Auth.Configure(o =>
{
    o.LoginPath = "/login";          // ← where unauthenticated users are challenged (HTTP GET)
    o.AccessDeniedPath = "/forbidden";  // ← a forbidden non-page endpoint
    o.CookieName = "rask.auth";      // Secure + HttpOnly + SameSite=Lax are not knobs
}));
```

---

## Declarative gating

The headless `Authorize` component renders exactly one of three slots — no markup of its own — off
the current user (`IUserProvider`):

```csharp
// Shorthand: children are the "authorized" branch (static content, no principal needed).
Authorize.Role("admin")[ AdminPanel() ]

// Full three-slot form. `Authorized` is a delegate handed the current principal (Blazor's
// @context.User), so a greeting reads the name with no injected IUserProvider and no subscription.
Authorize.Roles("admin", "editor")// ANY-of; omit for "any authenticated user"
.Authorized(user => Div.Class("panel")[ $"Welcome, {user.Identity!.Name}" ]).NotAuthorized(A.Href("/login")[ "Please sign in" ]).Authorizing(Spinner())                     // shown while the principal/policy resolves
```

- **`Authorized`** is `Func<ClaimsPrincipal, Component>` — it receives the signed-in principal and re-runs
  whenever the gate re-renders (i.e. on `IUserProvider.Changed`), so user-dependent markup stays fresh
  on its own. For static authorized content that ignores the user, use the children-indexer shorthand
  `Authorize(...)[ content ]`.
- **`Roles`** and the authenticated check are synchronous → no flicker.
- **`Policy`** (e.g. `Authorize.Policy("over-18")`) resolves via `IAuthorizationService` in the background;
  the `Authorizing` slot shows until it lands.
- **`Authorizing`** also covers the WASM bootstrap window: while a provider's `EnsureLoaded`/`Refresh`
  is in flight (`IUserProvider.IsLoading == true`), the slot bridges the anonymous→authenticated flash.

Use `Authorize` for *content* gating; use `[Authorize]` on a page for *route* gating; inject `IUserProvider`
and read `.Current` directly when you need imperative logic.

The imperative form, live — gate in `Render()` on the current user (sign in / out to flip the branch):

<!-- demo:auth-user-gate -->

And the declarative `Authorize` component, live — sign in as *user* or *admin* to switch between the
`NotAuthorized`, `Authorized`, and role-gated slots:

<!-- demo:auth-authorize -->

---

## `[Authorize]` on an event handler

Put ASP.NET's own `[Authorize]` on the component method an event runs. Someone who does not meet it is told
so in a toast, the method does not run, and nothing on the page changes:

```csharp
Ui.Button.Danger.OnClick(Remove)["Remove"]

[Authorize(Roles = "admin")]
private async Task Remove() => await Tank.Remove(TankId);
```

- **The rule belongs to the method, however it is reached.** A method group (`.OnClick(Remove)`) is checked
  when the event arrives; a call inside a lambda (`.OnClick(() => Remove(row.Id))`) and every other direct
  call in your project is checked by an interceptor the build writes. A call the build cannot intercept — a
  generic method, a call through `base` — is [RASK109](diagnostics.md#rask109).
- **Checked at the event, against the session's principal** — the live session asks who it is on every
  event, so a role taken away a moment ago is already gone. A handler id posted by hand runs the same check:
  the browser never decides.
- **What the person sees** is the catalog's `HandlerForbidden` sentence (*You do not have permission to do
  that.*, Hungarian *Ehhez nincs jogosultsága.*) in an error toast. A form's submit handler says the same and
  saves nothing.
- **What is logged** is one Information line naming the method — `TanksPage.Remove was refused: the signed-in
  user does not meet its [Authorize].` — and nothing of the person.
- `[Authorize]` bare asks for a signed-in user, `Roles = "a,b"` for any of them, `Policy = "…"` for a policy;
  several attributes must all pass, and `[AllowAnonymous]` on the method lifts it. A method that returns no
  `Task` is checked without waiting, so give it a `Task` if its policy answers asynchronously.

Hide what the person cannot use with [the `Authorize` component](#declarative-gating); the attribute is what
makes it safe.

## A page the user may not open

A page whose `[Authorize]` refuses a **signed-in** user is not left: the address stays, the layouts above it
render as usual, and its outlet says *You do not have permission to view this page.* (`PageForbidden`) — on
the first request (answered 403), on an in-app navigation, and on a page whose role was taken away while it
was open. The layouts' own buttons keep working. Someone **not signed in** is still sent to sign in, as before.

```csharp
[Route("/admin")] [Authorize] public sealed partial class AdminLayout : Component { … Outlet … }

[Route("users")] [ParentRoute(typeof(AdminLayout))] [Authorize(Roles = "admin")]
public sealed partial class UsersPage : Component { … }   // a signed-in non-admin: AdminLayout around the sentence
```

The sentence stands in for the first page of the chain that refuses the user, so a layout that itself refuses
them is replaced too.

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
