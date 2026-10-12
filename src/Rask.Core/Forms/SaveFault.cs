using Rask.Core.Globalization;
using Rask.Core.Messaging;

namespace Rask.Core.Forms;

// What the person who pressed Save is told when the save threw: one sentence, said once. Where the form
// draws its own messages it goes there, otherwise into a toast. What it carries of the exception is
// FaultSentence's to decide, as for a handler outside a form (HandlerFault).
internal static class SaveFault
{
    private const string English = "The save failed. Please try again.";

    /// <summary>Tells the reader of <paramref name="form" /> that its save failed.</summary>
    internal static void Tell(EditContext form, Exception thrown) => Say(form, Sentence(thrown));

    /// <summary>Says <paramref name="sentence" /> in the form's own messages, or in a toast when it draws none.</summary>
    // With neither — no session, or a host with no toaster — the log line is all there is.
    internal static void Say(EditContext form, string sentence)
    {
        if (form.ShowsFormMessages)
        {
            form.AddFailure(sentence, [form.FormSlot], []);
            return;
        }

        Toast.TryError(sentence);
    }

    private static string Sentence(Exception thrown) =>
        FaultSentence.Of(RaskStrings.Get(RaskString.FormSaveFailed, English), thrown);
}
