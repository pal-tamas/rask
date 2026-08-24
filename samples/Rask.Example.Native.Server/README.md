# Rask.Example.Native.Server (Native + Server)

The **native shell over a remote server** showcase — the mobile peer of `Rask.Example.Server`. A thin
native app that loads a **remote** `Rask.Example.Server` in a platform WebView: the C# app runs on the
server, this app just hosts the WebView and **bridges the OS share sheet** to the loaded page (the
"server superpower") via `Rask.Native`'s `NativeCapabilities`. There is no in-process session and no
bundled assets — the server renders and serves everything. Its sibling
[`Rask.Example.Native`](../Rask.Example.Native) runs the component code **in-process**. See
[`docs/native.md`](../../docs/native.md).

The platform heads (`Platforms/{Android/ServerActivity,iOS/ServerAppDelegate}.cs`) point their WebView at
the dev server, inject `NativeCapabilities.BridgeScript` **only for the trusted origin** per navigation,
and route the WebView's script-message handler to `NativeCapabilities.TryHandleAsync` with a native
`NativeShare`. Off-origin links open in the system browser, so the bridge is never exposed to another page.

## Running it

Multi-targets `net10.0-ios;net10.0-android` (mobile workloads, outside `Rask.slnx`). Publish + run the
server first, then launch the shell:

```bash
# Terminal 1 — the remote server, bound so the emulator/simulator can reach it (0.0.0.0:5080)
dotnet run --project samples/Rask.Example.Server --urls http://0.0.0.0:5080

# Terminal 2 — the native shell (Android emulator reaches the host at 10.0.2.2; iOS simulator at localhost)
dotnet workload install ios android
dotnet build samples/Rask.Example.Native.Server/Rask.Example.Native.Server.csproj "-t:Build;Run" -f net10.0-android
dotnet build samples/Rask.Example.Native.Server/Rask.Example.Native.Server.csproj "-t:Build;Run" -f net10.0-ios
```

The heads target `http://10.0.2.2:5080` (Android) / `http://localhost:5080` (iOS); http cleartext is enabled
for local development only (`usesCleartextTraffic` / `NSAllowsArbitraryLoads`) — a real deployment is https.
