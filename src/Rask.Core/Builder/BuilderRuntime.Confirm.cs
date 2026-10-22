namespace Rask.Core;

public static partial class BuilderRuntime
{
    /// <summary>
    ///     Asks the reader before this element's click or submit is sent:
    ///     <c>Ui.Button.OnClick(() => Delete(row)).Confirm("Delete the partner?")</c>. Cancelled, nothing is sent — not
    ///     the event, and not the fields still waiting to go with it.
    /// </summary>
    /// <remarks>
    ///     Livewire's <c>wire:confirm</c>, and with <paramref name="expect" /> its <c>wire:confirm.prompt</c>:
    ///     <c>.Confirm("Type the name to delete it", expect: row.Name)</c> lets only an exact match go, case and all. The
    ///     answer is written into the page (<c>data-rask-confirm-expect</c>), so it is a guard against a slip, never a
    ///     secret. On a form it asks before the submit; on a submit button, before the submit it makes. Rendered as
    ///     <c>data-rask-confirm</c>, which the runtime reads before the event leaves the browser. Asked in the browser's
    ///     own dialog, or in <c>Ui.Confirm</c> where the layout placed one, whose title <paramref name="heading" />
    ///     replaces for this question: <c>.Confirm("Delete the partner?", heading: "Delete partner")</c>.
    /// </remarks>
    /// <param name="element">The element whose action asks first.</param>
    /// <param name="text">The question.</param>
    /// <param name="expect">What the reader must type; null asks yes or no.</param>
    /// <param name="heading">The title over the question in <c>Ui.Confirm</c>; null keeps the layout's.</param>
    /// <typeparam name="T">The element, handed back so the chain keeps its type.</typeparam>
    /// <returns><paramref name="element" />.</returns>
    public static T Confirm<T>(this T element, string text, string? expect = null, string? heading = null)
        where T : Element
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(text);
        element.Ask(new ConfirmAsk(text, expect, heading));
        return element;
    }
}
