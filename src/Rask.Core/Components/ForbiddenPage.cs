using Rask.Core.Globalization;

namespace Rask.Core.Components;

/// <summary>
///     What stands in the outlet in place of a page the signed-in person may not open: one sentence from the catalog,
///     inside the app's own layout. Someone who is not signed in is sent to sign in instead.
/// </summary>
internal sealed class ForbiddenPage : Component
{
    private const string English = "You do not have permission to view this page.";

    protected override Component? Render() =>
        P.Class("rask-forbidden").Role(AriaRole.Alert)[RaskStrings.Get(RaskString.PageForbidden, English)];
}
