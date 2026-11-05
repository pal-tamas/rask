namespace Rask;

/// <summary>
///     The delete <see cref="UiFormButtons" /> and <see cref="UiRowActions" /> share: a button that asks first, in the
///     layout's <see cref="UiConfirm" />, and runs the page's delete only on yes.
/// </summary>
internal static class UiRemoval
{
    /// <summary>
    ///     <paramref name="button" /> running <paramref name="onRemove" /> once the reader says yes to
    ///     <paramref name="question" /> under <paramref name="heading" />: the catalog's words where either is unset.
    /// </summary>
    internal static UiButton Asking(UiButton button, Callback onRemove, string? heading, string? question, string word) =>
        button.OnClick(onRemove).Confirm(
            question ?? RaskStrings.Get(RaskString.RemoveQuestion, "Are you sure you want to delete it?"),
            heading: heading ?? word);
}
