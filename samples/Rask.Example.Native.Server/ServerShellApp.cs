using Rask.Core;
using Rask.Native.Components;

namespace Rask.Example.Native.Server;

/// <summary>The remote app this shell points at. Registered by the platform head, which knows the origin.</summary>
/// <param name="Url">
///     The absolute address of the Rask server. The two heads differ here and nowhere else: the iOS
///     simulator reaches the host machine at <c>localhost</c>, the Android emulator at <c>10.0.2.2</c>.
/// </param>
public sealed record ServerOrigin(Uri Url);

/// <summary>
///     Native + Server, written as markup. The UI comes from a remote <c>Rask.Example.Server</c>; the bars
///     around it are this app's, declared here and projected onto a real <c>UINavigationBar</c> /
///     Android top bar.
///     <para>
///         The whole app is one <c>NativeWebView.Url(…)</c>. That is the point of the mode: where the UI
///         comes from is the only thing that differs from the in-process showcase, and it is one step in one
///         component rather than a separate platform class.
///     </para>
/// </summary>
/// <remarks>
///     The origin arrives through the constructor rather than a settable property: a non-nullable property
///     with no initializer would become a required chain step, and this component is built by the host, not
///     by a chain.
/// </remarks>
public sealed partial class ServerShellApp(ServerOrigin origin) : Component
{
    protected override Component? Render() =>
    [
        NativeHeaderBar.Title("Rask (Server)"),

        // The remote server renders every page, so this component composes no children-hosting
        // NativeWebView — one component renders one kind of page (RASK050).
        NativeWebView.Url(origin.Url),
    ];
}
