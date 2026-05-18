using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.ScopedCss;

namespace Rask.Core.Components;

/// <summary>
///     Service contract a host registers to teach <see cref="RaskScopedStyles" /> how to emit
///     the scoped-css bundle reference. Server hosts return a <c>&lt;link&gt;</c> pointing at the
///     <c>/_rask/scoped.css</c> endpoint they map; WASM hosts skip registration entirely and
///     let the runtime apply the bundle inline through <c>&lt;style id="rask-scoped"&gt;</c>.
/// </summary>
public interface IRaskScopedStyles
{
    Component Render(string hash);
}

public sealed class RaskScopedStyles : Component
{
    protected override Component Render()
    {
        var hash = ScopedCssRegistry.CurrentHash;
        if (hash is null)
        {
            return new Raw(string.Empty);
        }

        var services = LiveRenderContext.Current?.Services;
        var provider = services?.GetService<IRaskScopedStyles>();
        return provider?.Render(hash) ?? new Raw(string.Empty);
    }
}
