using Rask.Core.Globalization;
using Rask.Core.Live;

namespace Rask.Core.Forms;

// What the person who pressed Save is told when the save threw: one sentence, said once. Where the form
// draws its own messages it goes there, otherwise into a toast. The sentence never carries the exception's
// text outside Development, since that text is written for the log and may name a server or a password.
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

    private static string Sentence(Exception thrown)
    {
        var sentence = RaskStrings.Get(RaskString.FormSaveFailed, English);
        return LiveOptions.IsDevelopment == true
            ? $"{sentence} {thrown.GetType().Name}: {thrown.Message}"
            : sentence;
    }
}
