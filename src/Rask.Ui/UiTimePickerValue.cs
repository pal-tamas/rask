using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     What a <see cref="UiTimePicker{T}" /> is bound to, read and written without reflection: one time
///     (<c>TimeOnly</c>, <c>TimeOnly?</c>) or several (a collection of <c>TimeOnly</c>).
/// </summary>
internal static class UiTimePickerValue
{
    /// <summary>Whether <typeparamref name="T" /> holds several times — Flux's <c>multiple</c>.</summary>
    internal static bool Several<T>() => typeof(T) != typeof(TimeOnly) && typeof(T) != typeof(TimeOnly?);

    /// <summary>Whether <typeparamref name="T" /> can say that nothing is chosen.</summary>
    internal static bool Clears<T>() => typeof(T) != typeof(TimeOnly);

    /// <summary>The chosen times, earliest first.</summary>
    internal static List<TimeOnly> Read<T>(T? value) => value switch
    {
        TimeOnly one => [one],
        IEnumerable<TimeOnly> many => [.. many.Order()],
        _ => [],
    };

    /// <summary>One time as <typeparamref name="T" />, or nothing where <typeparamref name="T" /> has a nothing.</summary>
    internal static T Single<T>(TimeOnly? time) => time is { } chosen ? (T)(object)chosen : default!;

    /// <summary>Several times in the shape <paramref name="declared" /> asks for: an array, a set, or a list.</summary>
    internal static object Shaped(Type declared, List<TimeOnly> picked)
    {
        if (declared == typeof(TimeOnly[]))
        {
            return picked.ToArray();
        }

        return declared == typeof(HashSet<TimeOnly>) || declared == typeof(ISet<TimeOnly>) || declared == typeof(IReadOnlySet<TimeOnly>)
            ? new HashSet<TimeOnly>(picked)
            : picked;
    }

    /// <summary>
    ///     Writes several times into the bound member: assigned where it has a setter, refilled in place where it
    ///     is a get-only collection. False when the member can take neither.
    /// </summary>
    internal static bool TryWrite(ExpressionAccessor.Accessor accessor, List<TimeOnly> picked)
    {
        if (accessor.Property.SetMethod is not null)
        {
            accessor.Setter(Shaped(accessor.PropertyType, picked));
            return true;
        }

        if (accessor.Getter() is not ICollection<TimeOnly> existing || existing.IsReadOnly)
        {
            return false;
        }

        existing.Clear();
        foreach (var time in picked)
        {
            existing.Add(time);
        }

        return true;
    }
}
