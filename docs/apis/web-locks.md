# IWebLocks

> Serialise work across an origin's tabs and workers.

- **Wraps:** Web Locks API (`navigator.locks`)
- **Home:** `Rask.Core.Browser` (all hosts)
- **Shape:** callback-scoped (holds a named lock for the lifetime of your callback)
- **Availability:** Web/Server ✅ · PWA/WASM ✅ · Native ✅
- **Native backend:** — (WebView JS)

`RequestAsync(name, work)` waits for the named lock, runs `work` while holding it, then releases — even
if `work` throws. `TryRequestAsync(name, work)` uses `ifAvailable`: it returns `false` immediately
(without running `work`) when the lock is already held, which makes a natural "leader tab" election.
`QueryAsync()` snapshots the locks the origin holds and is waiting on. Pass `LockMode.Shared` for a
reader/writer split. No user gesture is required, so it works on the Server transport too.

## See also

- Source: [`IWebLocks.cs`](../../src/Rask.Core/Browser/IWebLocks.cs)
- [Capability matrix](../browser-capabilities.md)
- [Browser APIs — the narrative map](../browser-apis.md)
