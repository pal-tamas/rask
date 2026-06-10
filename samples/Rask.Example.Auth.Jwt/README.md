# Rask.Example.Auth.Jwt

**JWT** authentication, server-side. The login issues a signed JWT (held in protected
browser storage) instead of an auth cookie; a protected `/members` page is gated behind it.

```bash
dotnet run --project samples/Rask.Example.Auth.Jwt
```

Sign in with the demo credentials shown on the login page.

## Key files

- `Program.cs` — `AddAuthentication().AddJwtBearer(...)` + `AddRask()` / `UseRask<App>()`.
- `JwtBootstrap.cs` — token issuance / signing key setup.
- `Auth/CredentialStore.cs` — the demo user store (replace with your own).
- `Pages/LoginPage.cs`, `Pages/MembersPage.cs` — the sign-in form and the gated page.

For cookie auth instead, see [`Rask.Example.Auth`](../Rask.Example.Auth); for the
browser-WASM JWT flow, see `Rask.Example.Auth.WasmJwt.Host`. Full walkthrough in the
[authentication guide](../../docs/authentication.md). **Demo credentials only — do not ship.**
