using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

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

public sealed class RaskRuntimeScript : Component
{
    protected override RenderResult Render()
    {
        var services = LiveRenderContext.Current?.Services;
        var provider = services?.GetService<IRaskRuntimeScript>();
        return provider?.Render() ?? new Raw(string.Empty);
    }
}
