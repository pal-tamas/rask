using Rask.Core.Globalization;

namespace Rask.Core.Messaging;

// A handler whose [Authorize] turned the person away (HandlerAccess): one error toast, and the page as it was.
// The refusal was logged where it was decided, so a session that draws no toasts loses nothing but the sentence.
internal static class ForbiddenAction
{
    private const string English = "You do not have permission to do that.";

    internal static void Say() => Toast.TryError(RaskStrings.Get(RaskString.HandlerForbidden, English));
}
