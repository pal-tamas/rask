using Rask.Core.Globalization;
using Rask.Core.Live;

namespace Rask.Core.Components;

/// <summary>
///     What stands in the outlet in place of a page the signed-in person may not open: one sentence from the catalog,
///     inside the app's own layout. Someone who is not signed in is sent to sign in instead.
/// </summary>
internal sealed class ForbiddenPage : Component
{
    private const string English = "You do not have permission to view this page.";

    // The kit's callout where the host has the kit on; Rask's own sentence everywhere else (a hand-wired host, a test).
    protected override Component? Render() =>
        RaskDocumentDefaults.Current?.Forbidden?.Invoke()
        ?? P.Class("rask-forbidden").Role(AriaRole.Alert)[RaskStrings.Get(RaskString.PageForbidden, English)];
}
