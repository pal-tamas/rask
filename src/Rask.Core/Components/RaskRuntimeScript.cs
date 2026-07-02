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

/// <summary>
///     Deprecated, no-op marker. The Rask runtime <c>&lt;script&gt;</c> is now injected
///     automatically as the last child of <c>&lt;body&gt;</c> by the serializer (see
///     <see cref="HtmlSerializer" />), so apps no longer need to place this component.
///     <para>
///         Retained for source compatibility: existing trees that still contain
///         <c>RaskRuntimeScript()</c> render nothing here and pick up the single
///         framework-injected script, so there is no double emission. New apps should omit it.
///     </para>
/// </summary>
public sealed class RaskRuntimeScript : Component
{
    protected override Component? Render() => new Raw(string.Empty);
}
