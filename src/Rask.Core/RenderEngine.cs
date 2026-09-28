namespace Rask.Core;

// How the component tree is rendered and transported. Its own enum rather than a bool so a third engine
// can arrive without reshaping the two that exist, and so a component branching on it reads as a fact
// about the host rather than a negation of the other one.
//
// CONSTANT for a session's lifetime, so reading it from a component's Render() never needs the
// render-cache ambient-state opt-out (unlike Context.Get / EditContext reads).

/// <summary>
///     How the component tree is rendered and transported. Read via <c>Component.HostEngine</c> /
///     <c>Component.IsServer</c> / <c>Component.IsWasm</c>.
/// </summary>
public enum RenderEngine
{
    /// <summary>Rendered server-side and streamed to the client over a live connection.</summary>
    Server,

    /// <summary>Rendered in the browser's WebAssembly runtime, in-process.</summary>
    Wasm,
}
