using System.Diagnostics.CodeAnalysis;
using Rask.Core.Globalization;

namespace Rask.Core;

public static partial class BuilderRuntime
{
    /// <summary>
    ///     Asks before the reader leaves the page with this form changed and not yet saved, in the framework's own
    ///     words: <c>Form.Model(m).OnSubmit(Save).ConfirmLeave()[ … ]</c> asks "Leave without saving?".
    /// </summary>
    /// <remarks>
    ///     The question is <see cref="RaskString.ConfirmLeaveQuestion" />, so it reads the app's <c>RaskStrings</c>
    ///     catalog, then the kit's own Hungarian ("Biztos elhagyja mentés nélkül az oldalt?"), then the English.
    ///     <c>.ConfirmLeave("…")</c> asks a question of the form's own instead; everything else is the same.
    /// </remarks>
    /// <param name="form">The form that asks.</param>
    /// <typeparam name="TModel">The form's model.</typeparam>
    /// <returns><paramref name="form" />.</returns>
    public static HTMLFormElement<TModel> ConfirmLeave<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TModel>(
        this HTMLFormElement<TModel> form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.ConfirmLeave = RaskStrings.Get(RaskString.ConfirmLeaveQuestion, "Leave without saving?");
        return form;
    }
}
