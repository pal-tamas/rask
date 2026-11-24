using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Stops an operation with a sentence for the person, shown under the field it is about — or over the whole form:
/// </summary>
/// <remarks>
///     <code>
///     Refuse.When(locked.Contains(part.Id), () =&gt; Parts[i].Quantity, Texts.LockedChanged);
///     Refuse.Unless(author.HasPlace, Texts.NoPlace);
///     </code>
///     <para>
///         Each throws a <see cref="RaskValidationException" />, which a form shows the way it shows any failed rule: the
///         message under the field, or above the form when no field is named. The field is read from the lambda as the
///         form names it — <c>() =&gt; Km.Start</c> is <c>Km.Start</c>, <c>() =&gt; Parts[i].Quantity</c> is
///         <c>Parts[2].Quantity</c> (the index is evaluated), a captured local or parameter takes its form field's name
///         (<c>() =&gt; parts[i].Quantity</c> is <c>Parts[2].Quantity</c>) and <c>this.</c> is dropped.
///     </para>
///     <para>
///         The lambda is read only when the operation is refused: an operation that goes through pays for the expression
///         the compiler builds at the call site, and nothing else.
///     </para>
/// </remarks>
public static class Refuse
{
    /// <summary>Refuses the operation as a whole when <paramref name="condition" /> holds.</summary>
    /// <param name="condition">What makes the operation impossible.</param>
    /// <param name="message">What the person is told, above the form.</param>
    /// <exception cref="RaskValidationException">When <paramref name="condition" /> holds.</exception>
    public static void When([DoesNotReturnIf(true)] bool condition, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (condition)
        {
            throw new RaskValidationException(message);
        }
    }

    /// <summary>Refuses the operation at <paramref name="field" /> when <paramref name="condition" /> holds.</summary>
    /// <typeparam name="T">The field's type.</typeparam>
    /// <param name="condition">What makes the operation impossible.</param>
    /// <param name="field">The field the message is shown under: <c>() =&gt; Km.Start</c>, <c>() =&gt; Parts[i].Quantity</c>.</param>
    /// <param name="message">What the person is told.</param>
    /// <exception cref="RaskValidationException">When <paramref name="condition" /> holds.</exception>
    public static void When<T>([DoesNotReturnIf(true)] bool condition, Expression<Func<T>> field, string message)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(message);
        if (condition)
        {
            At(field, message);
        }
    }

    /// <summary>Refuses the operation as a whole unless <paramref name="condition" /> holds.</summary>
    /// <param name="condition">What the operation needs.</param>
    /// <param name="message">What the person is told, above the form.</param>
    /// <exception cref="RaskValidationException">When <paramref name="condition" /> does not hold.</exception>
    public static void Unless([DoesNotReturnIf(false)] bool condition, string message) => When(!condition, message);

    /// <summary>Refuses the operation at <paramref name="field" /> unless <paramref name="condition" /> holds.</summary>
    /// <typeparam name="T">The field's type.</typeparam>
    /// <param name="condition">What the operation needs.</param>
    /// <param name="field">The field the message is shown under: <c>() =&gt; Km.Start</c>, <c>() =&gt; Parts[i].Quantity</c>.</param>
    /// <param name="message">What the person is told.</param>
    /// <exception cref="RaskValidationException">When <paramref name="condition" /> does not hold.</exception>
    public static void Unless<T>([DoesNotReturnIf(false)] bool condition, Expression<Func<T>> field, string message) =>
        When(!condition, field, message);

    /// <summary>Refuses the operation at <paramref name="field" />, always — the branch that already knows.</summary>
    /// <typeparam name="T">The field's type.</typeparam>
    /// <param name="field">The field the message is shown under: <c>() =&gt; Km.Start</c>, <c>() =&gt; Parts[i].Quantity</c>.</param>
    /// <param name="message">What the person is told.</param>
    /// <exception cref="RaskValidationException">Always.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="field" /> is not a chain of members and indexers, such as <c>() =&gt; Total()</c>.
    /// </exception>
    [DoesNotReturn]
    public static void At<T>(Expression<Func<T>> field, string message)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(message);
        throw new RaskValidationException([new FieldFailure(message, [RefusedField.PathOf(field)])]);
    }
}
