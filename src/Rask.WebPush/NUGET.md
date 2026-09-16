# Rask.WebPush

**Server-side Web Push sender.** Signs [VAPID](https://www.rfc-editor.org/rfc/rfc8292) and encrypts
([RFC 8291](https://www.rfc-editor.org/rfc/rfc8291), `aes128gcm`) push messages, then POSTs them to
a browser `PushSubscription` endpoint. It is the **send half** that pairs with `Rask.Wasm`'s
`IWebPush` client — but it is **transport-neutral**: usable from a Rask Server app or any ASP.NET
backend behind a WASM PWA.

No external dependencies beyond `Microsoft.Extensions.*` — all crypto is in-box
`System.Security.Cryptography`.

## Install

```bash
dotnet add package Rask.WebPush
```

## Use

The key pair and the contact come from the `Rask:WebPush` configuration section. A `rask new` app already
has a development pair in its (gitignored) `appsettings.Development.json`. Otherwise mint one with
`VapidKeys.Generate()` and keep it out of source — the private key signs every push you send:

```jsonc
// appsettings.Development.json — gitignored
{
  "Rask": { "WebPush": { "VapidKeys": { "PublicKey": "…", "PrivateKey": "…" } } }
}
```

Deployed, both keys come from the environment instead: `Rask__WebPush__VapidKeys__PublicKey` and
`Rask__WebPush__VapidKeys__PrivateKey`.

```jsonc
// appsettings.json
{
  "Rask": {
    "WebPush": {
      "Subject": "mailto:admin@example.com"    // a contact the push service can reach
    }
  }
}
```

```csharp
builder.Services.AddRaskWebPush();   // reads Rask:WebPush; a callback here would run after it and win

// ... inject IWebPush, then given a PushSubscription the browser sent you:
var result = await sender.SendAsync(
    subscription,
    new WebPushMessage { Title = "Hello", Body = "from the server" },
    ct);

// Inspect the result to prune dead subscriptions:
if (result.ShouldDelete) await store.RemoveAsync(subscription);
```

## Notes

- **Standards-based** — VAPID (RFC 8292) auth + RFC 8291 `aes128gcm` payload encryption; interops
  with any standard browser Push service (FCM, Mozilla, WNS).
- **Zero external deps** — ECDH, HKDF and AES-GCM come from `System.Security.Cryptography`.
- Generate a VAPID key pair once with `VapidKeys.Generate()` and keep the private key server-side;
  hand the public key to the browser subscription.
- `SendAsync` returns a `WebPushResult` whose `ShouldDelete` / `ShouldRetry` flags map the push
  service's response to the action to take on the subscription.

Full documentation: <https://github.com/pal-tamas/rask/blob/main/docs/pwa.md>
