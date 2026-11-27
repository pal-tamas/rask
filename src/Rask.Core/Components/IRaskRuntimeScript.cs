namespace Rask.Core.Components;

/// <summary>
///     Service contract a host registers to say which runtime <c>&lt;script&gt;</c> tag the
///     serializer injects as the last child of <c>&lt;body&gt;</c>. Server and WASM hosts mount
///     different runtimes at different URLs (and the WASM bootstrap requires <c>type="module"</c>),
///     so the App component stays runtime-agnostic and lets the host decide.
/// </summary>
public interface IRaskRuntimeScript
{
    Component Render();
}
