# IWebAuthn

> Register and sign in with passkeys.

- **Wraps:** Web Authentication (passkeys)
- **MDN:** [Web Authentication API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Authentication_API)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** one-shot
- **Availability:** Web/Server ✅ · PWA/WASM ✅

## You probably want `IAuth` instead

This is the **browser half** on its own: it runs a ceremony and hands back what the authenticator signed, leaving the
challenge and the verification to you. If you are adding passkeys to an app's accounts, Rask.Auth already does both
ends — `auth.AddPasskeyAsync("MacBook")` and `auth.SignInWithPasskeyAsync()` — with the credential stored and the
signature verified server-side. See [Passkeys](../authentication.md#passkeys).

Reach for `IWebAuthn` directly when the relying party is somebody else's server, or when you are verifying the
ceremony yourself.

## See also

- Source: [`IWebAuthn.cs`](../../src/Rask.Core/Browser/IWebAuthn.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
- [Passkeys, end to end](../authentication.md#passkeys)
