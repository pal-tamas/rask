namespace Rask.Core.Components;

/// <summary>
///     Service contract a host registers to teach <see cref="RaskRuntimeScript" /> which
///     <c>&lt;script&gt;</c> tag to emit. Server and WASM hosts mount different runtimes at
///     different URLs (and the WASM bootstrap requires <c>type="module"</c>), so the App
///     component stays runtime-agnostic and lets the host decide.
/// </summary>
public interface IRaskRuntimeScript
{
    Component Render();
}
