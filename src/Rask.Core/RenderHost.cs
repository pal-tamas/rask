namespace Rask.Core;

// Host-awareness is modelled as THREE orthogonal axes, each its own enum, so a component can branch
// its render on any one without the others collapsing into it. A single mutually-exclusive enum would
// be wrong: the presentation shell, the render engine, and the device OS co-occur — e.g. Native+Server
// on iOS is Shell.Native + Engine.Server + Platform.IOS simultaneously. Keeping them separate also makes
// each independently extensible (a future desktop shell, a new engine, another platform) without
// reshaping the others, and keeps illegal combinations from being a single-enum concern.
//
// All three are CONSTANT for a session's lifetime, so reading them from a component's Render() never
// needs the render-cache ambient-state opt-out (unlike Context.Get / EditContext reads).

/// <summary>
///     Where the UI is presented — a browser page (<see cref="Web" />) or inside a native app shell
///     (<see cref="Native" />). Read via <c>Component.HostShell</c> / <c>Component.IsNative</c>. Independent of
///     <see cref="RenderEngine" /> and <see cref="RenderPlatform" />: a native shell may host either a
///     server-rendered or an in-process app.
/// </summary>
public enum RenderShell
{
    /// <summary>The UI is presented in a web browser page (the default web host).</summary>
    Web,

    /// <summary>The UI is presented inside a native mobile app shell (the <c>Rask.Native</c> host).</summary>
    Native,
}

/// <summary>
///     How the component tree is rendered and transported. Read via <c>Component.HostEngine</c> /
///     <c>Component.IsServer</c> / <c>Component.IsWasm</c>. Independent of <see cref="RenderShell" />: a
///     native shell can run an in-process engine (Native+Local) or drive a remote server (Native+Server).
/// </summary>
public enum RenderEngine
{
    /// <summary>Rendered server-side and streamed to the client over a live connection.</summary>
    Server,

    /// <summary>Rendered in the browser's WebAssembly runtime, in-process.</summary>
    Wasm,

    /// <summary>Rendered in-process on the device (the native host's local engine).</summary>
    InProcess,
}

/// <summary>
///     Which device operating system the app is running on. <see cref="None" /> whenever
///     <see cref="RenderShell.Web" /> — platform identity is only meaningful inside a native shell. Read via
///     <c>Component.HostPlatform</c> / <c>Component.IsIOS</c> / <c>Component.IsAndroid</c>.
/// </summary>
public enum RenderPlatform
{
    /// <summary>No device platform — the app is running as a web page (<see cref="RenderShell.Web" />).</summary>
    None,

    /// <summary>Running on iOS.</summary>
    IOS,

    /// <summary>Running on Android.</summary>
    Android,
}
