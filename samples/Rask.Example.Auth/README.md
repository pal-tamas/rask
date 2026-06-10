# Rask.Example.Auth

**Cookie** authentication, server-side. A `/login` form signs the user in with an ASP.NET
auth cookie; a protected `/members` page is gated behind it.

```bash
dotnet run --project samples/Rask.Example.Auth
```

Sign in with the demo credentials shown on the login page.

## Key files

- `Program.cs` — `AddAuthentication().AddCookie(...)` + `AddRask()` / `UseRask<App>()`.
  Rask has no auth options object; the cookie scheme is configured on ASP.NET directly.
- `CredentialStore.cs` — the demo user store (replace with your own).
- `Pages/LoginPage.cs`, `Pages/MembersPage.cs` — the sign-in form and the gated page.

Other auth flavours: [`Rask.Example.Auth.Jwt`](../Rask.Example.Auth.Jwt) (server JWT),
and the WASM pairs under `Rask.Example.Auth.Wasm*`. See the
[authentication guide](../../docs/authentication.md). **Demo credentials only — do not ship.**
