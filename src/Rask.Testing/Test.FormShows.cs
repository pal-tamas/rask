using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Testing;

public static partial class Test
{
    private const DynamicallyAccessedMemberTypes FormModelMembers =
        DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor;

    /// <summary>
    ///     Checks that a form over a new <typeparamref name="TModel" /> shows <paramref name="message" /> under
    ///     <paramref name="field" /> once it holds <paramref name="value" />:
    ///     <c>await Test.FormShows&lt;SupplyChangeInput&gt;(m =&gt; m.Parts[0].Quantity, 0m, Litres.TooLittle)</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The model is put in a real <c>Form.Model(model)</c>, so the rules asked are the ones the page asks — the
    ///         attributes, the validators found for the model, and the store's rules (an operation form's dry run) —
    ///         first as the field is left, then, when that showed nothing of the kind, as the form is submitted. A row the
    ///         path names that the list does not have yet is added, and a sub-object nobody filled is made.
    ///     </para>
    ///     <para>
    ///         One line per feature keeps the form and the domain saying the same thing: the rule's own test proves the
    ///         domain refuses, this one proves the reader is told so where they type.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TModel">The form's model.</typeparam>
    /// <param name="field">The field: <c>m =&gt; m.Name</c>, <c>m =&gt; m.Km.End</c>, <c>m =&gt; m.Parts[0].Quantity</c>.</param>
    /// <param name="value">
    ///     What the reader puts in it; a number is converted to the field's type. For a value object the model holds as
    ///     it is — <c>TankCapacity? Size</c>, or a member of one of several values, <c>m =&gt; m.Start!.Quantity</c> — it is
    ///     what the reader types (<c>80</c>), made into the value object through its <c>TryCreate</c> as the page makes it.
    /// </param>
    /// <param name="message">What the form must show under it.</param>
    /// <returns>A task that completes once the form showed it.</returns>
    /// <exception cref="FormAssertionException">The form did not show <paramref name="message" /> under the field.</exception>
    public static Task FormShows<[DynamicallyAccessedMembers(FormModelMembers)] TModel>(
        Expression<Func<TModel, object?>> field, object? value, string message)
        where TModel : class, new() =>
        FormShows(new TModel(), field, value, message);

    /// <summary>
    ///     Checks that a form over a new <typeparamref name="TModel" />, with the other fields set by
    ///     <paramref name="given" />, shows <paramref name="message" /> under <paramref name="field" /> once it holds
    ///     <paramref name="value" />:
    ///     <c>Test.FormShows&lt;DeliveryNoteModel&gt;(m =&gt; m.Km.End, value: 5, given: m =&gt; m.Km.Start = 10, KmRange.EndNotPastStart)</c>.
    /// </summary>
    /// <typeparam name="TModel">The form's model.</typeparam>
    /// <param name="field">The field the message is under.</param>
    /// <param name="value">What the reader puts in it.</param>
    /// <param name="given">Fills the fields a rule over several reads, before <paramref name="value" /> is put in.</param>
    /// <param name="message">What the form must show under the field.</param>
    /// <returns>A task that completes once the form showed it.</returns>
    /// <exception cref="FormAssertionException">The form did not show <paramref name="message" /> under the field.</exception>
    public static Task FormShows<[DynamicallyAccessedMembers(FormModelMembers)] TModel>(
        Expression<Func<TModel, object?>> field, object? value, Action<TModel> given, string message)
        where TModel : class, new()
    {
        ArgumentNullException.ThrowIfNull(given);
        var model = new TModel();
        given(model);
        return FormShows(model, field, value, message);
    }

    /// <summary>
    ///     Checks that a form over <paramref name="model" /> shows <paramref name="message" /> under
    ///     <paramref name="field" /> once it holds <paramref name="value" /> — for a model given as it is, like an operation
    ///     form's, read from the stored aggregate as <c>Form.For</c> reads it: <c>await SupplyChangeInput.Of(id)</c>.
    /// </summary>
    /// <typeparam name="TModel">The form's model.</typeparam>
    /// <param name="model">The model, as the form would start from it.</param>
    /// <param name="field">The field the message is under.</param>
    /// <param name="value">What the reader puts in it.</param>
    /// <param name="message">What the form must show under the field.</param>
    /// <returns>A task that completes once the form showed it.</returns>
    /// <exception cref="FormAssertionException">The form did not show <paramref name="message" /> under the field.</exception>
    public static async Task FormShows<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TModel>(
        TModel model, Expression<Func<TModel, object?>> field, object? value, string message)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(message);

        var reach = FormFieldReach.Of(model, field);
        if (!reach.IsValueObject)
        {
            reach.Set(value);
        }

        using var diagnostics = CapturingDiagnostics.Install();
        var form = FormOver(model);

        // The page draws a control for the field the test names, and only a bound field is shown what the store's
        // rules (an operation's dry run) say about it; a failure no control is bound to goes to the top of the form.
        form.TrackBindingOwner(reach.Field, owner: null);

        // As the reader leaves the field: the value committed, its rules asked. A value object is typed in through the
        // binding a control's goes through, so what it refuses is said under the field as the page says it.
        if (reach.IsValueObject)
        {
            await reach.Type(value, form).ConfigureAwait(false);
        }
        else
        {
            form.NotifyFieldChanged(reach.Field);
        }

        await form.ValidateField(reach.Field).ConfigureAwait(false);
        var left = form.GetValidationMessages(reach.Field).ToArray();
        if (left.Contains(message, StringComparer.Ordinal))
        {
            return;
        }

        // As the form is submitted: every rule, a rule over several fields among them.
        await form.Validate().ConfigureAwait(false);
        var submitted = form.GetValidationMessages(reach.Field).ToArray();
        if (submitted.Contains(message, StringComparer.Ordinal))
        {
            return;
        }

        throw new FormAssertionException(
            $"Expected the form over {typeof(TModel).Name} to show \"{message}\" under {reach.Path} holding {Shown(value)}; "
            + $"it showed {Said(left.Union(submitted, StringComparer.Ordinal).ToArray())}"
            + (form.GetValidationMessages(form.FormSlot) is { Count: > 0 } whole
                ? $", and at the top of the form {Said([.. whole])}."
                : ".")
            + Unchecked(diagnostics, typeof(TModel).Name));
    }

    // What the form could not ask — the store's rules throwing — which is why it showed nothing.
    private static string Unchecked(CapturingDiagnostics diagnostics, string model) =>
        string.Concat(diagnostics.OfCategory("Rask.Forms")
            .Where(d => d.Message.Contains(model, StringComparison.Ordinal))
            .Select(static d => $" {d.Message}: {d.Exception?.Message}"));

    // The form the page draws over the model, its validators registered as a render registers them.
    private static EditContext FormOver<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TModel>(TModel model)
        where TModel : class
    {
        EditContext? form = null;
        Page.Render(() => Markup.Form.Model(model)[EditContextProbe(c => form = c)]);
        return form ?? throw new InvalidOperationException($"The form over {typeof(TModel).Name} drew no fields.");
    }

    private static string Said(string[] messages) =>
        messages.Length == 0 ? "nothing" : string.Join(", ", messages.Select(static m => '"' + m + '"'));

    private static string Shown(object? value) =>
        value switch
        {
            null => "null",
            string text => '"' + text + '"',
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
}
