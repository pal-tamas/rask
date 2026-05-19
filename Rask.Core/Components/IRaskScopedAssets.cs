namespace Rask.Core.Components;

/// <summary>
///     Host strategy for emitting the scoped-css bundle reference inside the
///     framework-managed <c>&lt;head&gt;</c>. Server hosts return a <c>&lt;link&gt;</c>
///     pointing at the <c>/_rask/scoped.css</c> endpoint they map; WASM hosts skip
///     registration and let the runtime apply the bundle inline through
///     <c>&lt;style id="rask-scoped"&gt;</c>. The framework looks this up via DI
///     during <c>&lt;head&gt;</c> emission — call sites have no need to reference
///     either name directly.
/// </summary>
public interface IRaskScopedStyles
{
    Component Render(string hash);
}

/// <summary>
///     Host strategy for emitting the scoped-js bundle reference inside the
///     framework-managed <c>&lt;head&gt;</c>. Same shape as
///     <see cref="IRaskScopedStyles"/> for the scoped-js bundle.
/// </summary>
public interface IRaskScopedScripts
{
    Component Render(string hash);
}
