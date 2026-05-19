using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.ScopedJs;

namespace Rask.Core.Components;

/// <summary>
///     Service contract a host registers to teach <see cref="RaskScopedScripts" /> how to
///     emit the scoped-js bundle reference. Server hosts return a <c>&lt;script&gt;</c>
///     pointing at the <c>/_rask/scoped.js</c> endpoint they map; WASM hosts skip
///     registration entirely and let the runtime apply the bundle inline through
///     <c>&lt;script id="rask-scoped-js"&gt;</c>.
/// </summary>
public interface IRaskScopedScripts
{
    Component Render(string hash);
}

public sealed class RaskScopedScripts : Component
{
    protected override Component Render()
    {
        var hash = ScopedJsRegistry.CurrentHash;
        if (hash is null)
        {
            return new Raw(string.Empty);
        }

        var services = LiveRenderContext.Current?.Services;
        var provider = services?.GetService<IRaskScopedScripts>();
        return provider?.Render(hash) ?? new Raw(string.Empty);
    }
}
