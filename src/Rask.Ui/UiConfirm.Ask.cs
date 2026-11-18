using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Rask;

public sealed partial class UiConfirm
{
    /// <summary>
    ///     Asks <paramref name="question" /> in the page's <c>Ui.Confirm</c> dialog from code — an event handler, after a
    ///     check on the server — and completes with the answer: true for Yes; false for No, Escape, the close button and
    ///     a press outside.
    /// </summary>
    /// <remarks>
    ///     <code>
    ///     if (!await Ui.Confirm.Ask("The period overlaps another. Save it anyway?")) return;
    ///     if (await Ui.Confirm.Heading("Km").No("Nem").Yes("Igen").Primary().Ask("Rögzíti a km-t?")) …
    ///     </code>
    ///     <para>
    ///     The words and the tone set on this chain are this question's alone; what it leaves unset is the layout's
    ///     dialog's. Asked while the dialog is already asking, the question waits its turn and opens once the one
    ///     before it is answered. Focus moves into the dialog, and back to where it was once it closes.
    ///     </para>
    ///     <para>
    ///     Completes false, unanswered, once the work that asked is cancelled — its component leaves the page, the
    ///     session ends, a configured handler timeout runs out — or once the page is gone.
    ///     </para>
    /// </remarks>
    /// <param name="question">The sentence asked beneath the heading.</param>
    /// <returns>Whether the reader said yes.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Called outside an event handler or a lifecycle hook, or the page has no <c>Ui.Confirm</c> to ask in.
    /// </exception>
    public async Task<bool> Ask(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var js = Ambient.Services?.GetService<IJSRuntime>() ?? throw new InvalidOperationException(
            "Ui.Confirm.Ask was called outside an event handler or a lifecycle hook, so there is no page to ask on. "
            + "Ask from the handler of the press that needs the answer.");
        bool? answer;
        try
        {
            answer = await js.InvokeAsync<bool?>(
                "__raskConfirm.ask", Ambient.CancellationToken, question, Heading, No, Yes, Tone).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or JSDisconnectedException)
        {
            // The work that asked is over, or the page is: nobody is left to answer.
            return false;
        }

        return answer ?? throw new InvalidOperationException(
            "Ui.Confirm.Ask found no Ui.Confirm on the page to ask in. Place Ui.Confirm in the layout, beside its "
            + "Outlet (Ui.AppFrame places one already).");
    }

    // The tone this question asks Yes in, or null for the dialog's own.
    private string? Tone => Primary switch
    {
        true => PrimaryTone,
        false => DangerTone,
        null => null,
    };
}
