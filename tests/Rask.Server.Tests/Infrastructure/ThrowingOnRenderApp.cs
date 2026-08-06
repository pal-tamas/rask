using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     Faults during <em>render</em>, unlike <see cref="ThrowingApp" /> which faults in a click handler.
///     That difference is the point: a render fault happens during the initial GET, so it is the case
///     where the HTTP status of the response is decided (#607).
/// </summary>
public sealed class ThrowingOnRenderApp : Component
{
    protected override Component? Render() =>
        throw new InvalidOperationException("render-boom");
}
