using Rask.Core.Http;

namespace Rask.Wasm;

/// <summary>
///     WASM's <see cref="IPageResponse" />: every member is a no-op.
/// </summary>
/// <remarks>
///     There is no HTTP response to shape here. The app was delivered as a static bundle long
///     before this page rendered, and routing happens entirely in the browser, so there is no
///     status left to set and no redirect to issue at the protocol level.
///     <para>
///         It is registered rather than omitted so a page that calls it compiles and runs unchanged
///         on both hosts — the same shape <c>Navigator</c> and <c>RouteState</c> take. Silently
///         doing nothing is the honest behaviour: the alternative, throwing, would make a page that
///         is correct on the server crash in the browser for a reason the author cannot act on.
///     </para>
/// </remarks>
internal sealed class WasmPageResponse : IPageResponse
{
    public void SetStatus(int statusCode)
    {
        // No response to carry it.
    }

}
