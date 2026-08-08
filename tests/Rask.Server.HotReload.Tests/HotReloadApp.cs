using Rask.Core;

namespace Rask.Server.HotReload.Tests;

/// <summary>
///     A page whose rendered text comes from a mutable static, so a test can change what the component
///     renders without changing any IL — the honest stand-in for "the runtime applied an update".
/// </summary>
/// <remarks>
///     Edit-and-Continue itself is out of scope for an in-process test: only a real <c>dotnet watch</c>
///     session can produce a metadata delta, which is what the watch E2E covers. Everything downstream of
///     the delta — the coordinator's phases, the repaint, the announcement, the wire — is the real
///     shipping code, driven here by invoking the metadata-update handler directly.
/// </remarks>
public sealed partial class HotReloadApp : Component
{
    internal const string Original = "before-the-edit";

    internal static string Heading { get; set; } = Original;

    protected override Component? Render() => Div(Id: "heading")[Heading];
}
