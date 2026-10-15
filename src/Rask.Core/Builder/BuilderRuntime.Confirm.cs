namespace Rask.Core;

public static partial class BuilderRuntime
{
    /// <summary>
    ///     Asks the reader before this element's click or submit is sent, in the browser's own dialog:
    ///     <c>Ui.Button.OnClick(() => Delete(row)).Confirm("Delete the partner?")</c>. Cancelled, nothing is sent — not
    ///     the event, and not the fields still waiting to go with it.
    /// </summary>
    /// <remarks>
    ///     Livewire's <c>wire:confirm</c>. On a form it asks before the submit; on a submit button, before the submit it
    ///     makes. Rendered as <c>data-rask-confirm</c>, which the runtime reads before the event leaves the browser.
    /// </remarks>
    /// <param name="element">The element whose action asks first.</param>
    /// <param name="text">The question.</param>
    /// <typeparam name="T">The element, handed back so the chain keeps its type.</typeparam>
    /// <returns><paramref name="element" />.</returns>
    public static T Confirm<T>(this T element, string text)
        where T : Element
    {
        ArgumentNullException.ThrowIfNull(element);
        element.Ask(text, expect: null);
        return element;
    }

    /// <summary>
    ///     Asks the reader to type <paramref name="expect" /> before this element's click or submit is sent:
    ///     <c>.Confirm("Type the name to delete it", expect: row.Name)</c>. Only an exact match goes, case and all.
    /// </summary>
    /// <remarks>
    ///     Livewire's <c>wire:confirm.prompt</c>. The answer is written into the page (<c>data-rask-confirm-expect</c>),
    ///     so it is a guard against a slip, never a secret.
    /// </remarks>
    /// <param name="element">The element whose action asks first.</param>
    /// <param name="text">The question.</param>
    /// <param name="expect">What the reader must type.</param>
    /// <typeparam name="T">The element, handed back so the chain keeps its type.</typeparam>
    /// <returns><paramref name="element" />.</returns>
    public static T Confirm<T>(this T element, string text, string expect)
        where T : Element
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(expect);
        element.Ask(text, expect);
        return element;
    }
}
