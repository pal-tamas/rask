# Rask.Example.Native (Native + Local)

The **native, in-process** showcase — the mobile peer of `Rask.Example.Wasm`. It mounts the **same**
`Rask.Example.Shared.App` every other host mounts, but onto a [`NativeAppHost`](../../src/Rask.Native)
that runs the component code **in-process on the device** behind an `INativeWebView` bridge (the
WebView-hybrid model: C# runs native, the view is a platform WebView). Its sibling
[`Rask.Example.Native.Server`](../Rask.Example.Native.Server) is the thin shell over a remote
`Rask.Example.Server`. See [`docs/native.md`](../../docs/native.md).

The thin platform heads live under `Platforms/{iOS,Android}` — each boots a `NativeAppHost`, registers
the showcase services (`AddExampleServices`), mounts `App` with `RunLocalAsync`, and serves the boot
shell + client + scoped CSS/JS + Bootstrap + `global.css` + `data/*.json` from the app's **bundled
assets** through `Rask.Native`'s [`NativeOriginAssets`](../../src/Rask.Native/NativeOriginAssets.cs)
(and the in-process demo `HttpClient` through `NativeAssetHttpHandler`, so data-driven pages work
offline). The `wwwroot/` here (global.css, `img/`, `data/`) is bundled into the app.

## Running it

Multi-targets `net10.0-ios;net10.0-android`, so it needs the mobile workloads and sits **outside
`Rask.slnx`**:

```bash
dotnet workload install ios android
dotnet build samples/Rask.Example.Native/Rask.Example.Native.csproj "-t:Build;Run" -f net10.0-android   # emulator
dotnet build samples/Rask.Example.Native/Rask.Example.Native.csproj "-t:Build;Run" -f net10.0-ios       # simulator (macOS)
```

## How it's tested

`tests/Rask.Native.Appium.Tests` drives the **real app on a device**: [Appium](https://appium.io)
installs this APK on an Android emulator, switches into its WebView, and asserts the showcase rendered
with its scoped CSS + Bootstrap (served through `NativeOriginAssets`). CI runs it in the macOS
`native-appium` job (which boots the emulator and starts an Appium server); the same test also drives the
iOS simulator locally. A separate macOS `native` job compiles this example and its `.Server` sibling for
both TFMs. To run the Android test locally: boot an emulator, `appium
--allow-insecure=uiautomator2:chromedriver_autodownload &`, build the APK with
`-p:EmbedAssembliesIntoApk=true`, then
`RASK_APPIUM_SERVER=http://127.0.0.1:4723 RASK_APPIUM_ANDROID_APP=<apk> dotnet test tests/Rask.Native.Appium.Tests`.
