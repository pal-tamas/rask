using Rask.Core.Diagnostics;
using Rask.Core.Globalization;

namespace Rask.Core.Messaging;

// A handler outside a form — a click, a change — that threw, with no boundary of the app's own around it:
// one error toast, and the page stays as the handler left it. The toast carries the reference the log line
// does, as the error page would have; a form's save has its own sentence (SaveFault).
internal static class HandlerFault
{
    private const string English = "That did not work. Please try again.";

    /// <summary>Says that the handler failed, in an error toast, and whether a session drawing toasts took it.</summary>
    // Asked first: the reference is logged as it is made, and a fault no toast takes is the boundary's to log.
    internal static bool Say(Exception thrown) =>
        Toast.IsDrawn && Toast.TryError(FaultSentence.Of(
            $"{RaskStrings.Get(RaskString.HandlerFailed, English)} {ErrorReference.Sentence(thrown, Reached)}", thrown));

    private const string Reached = "a toast from an event handler";
}
