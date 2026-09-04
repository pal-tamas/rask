# Rask.Example.Auth

**Cookie** authentication, server-side — on the accounts battery, so `Program.cs` contains no auth
code at all. Naming a database is what wires it: registration, sign-in and sign-out are routed by the
framework, the cookie scheme is registered for you, and a protected `/members` page is gated behind it.

```bash
dotnet run --project samples/Rask.Example.Auth
```

Sign in as `ada@example.com` (admin) or `bob@example.com` (user), both with `Password1`.

## Key files

- `Program.cs` — `RaskApp.Create` plus one `AddDbContextFactory`. **No auth code**, which is the point;
  `app.Configure(c => c.Auth.Off())` is how an app declines it.
- `Shared/AppDbContext.cs` — the app's context (`modelBuilder.AddRaskAuth()` maps the account tables),
  and `AuthSeed`: the two demo accounts, so the sample runs the moment you clone it. A real app
  seeds nobody: the first person to register becomes the administrator.
- `Features/Login/LoginPage.cs` — **optional.** Rask already routes `/login`; this file shows that
  declaring a page at the same route replaces the framework's, and that `IAuth.SignInAsync` is the same
  call either way.
- `Features/Members/MembersPage.cs` — the gated page, with `[Authorize]` and `Authorize.Roles`.

## Trying the reset flow

The "Forgotten your password?" link goes to `/forgot-password`, which is the framework's — replacing
`/login` replaces one page, not the flow. The reset email goes out through the mail battery, and with
no SMTP configured that means it is written to `./mail-pickup` as an `.eml`. Open the newest one and
follow the link to land on `/reset-password`.

Other auth flavours: [`Rask.Example.Auth.Jwt`](../Rask.Example.Auth.Jwt) (server JWT), and the WASM
pairs under `Rask.Example.Auth.Wasm*`. See the
[authentication guide](../../docs/authentication.md). **Demo credentials only — do not ship.**
