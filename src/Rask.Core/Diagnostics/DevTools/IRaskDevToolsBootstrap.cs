using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Diagnostics.DevTools;

/// <summary>
///     What <c>Rask.DevTools</c> implements so a host can attach it without referencing it.
/// </summary>
internal interface IRaskDevToolsBootstrap
{
    /// <summary>Registers the devtools' services. Called once, before the host builds its provider.</summary>
    void Attach(IServiceCollection services);
}
