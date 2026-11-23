namespace Rask.Data;

/// <summary>
///     The base of a value object that is checked as it is made:
///     <c>public sealed partial record Opening(DateOnly? Date, decimal? Quantity) : Value { … }</c>
/// </summary>
/// <remarks>
///     <para>
///         The constructor asks <see cref="Rules" /> and throws a <see cref="Rask.Cqrs.RaskValidationException" />
///         with one failure per <see cref="Problem" /> it yields, so no instance exists that its rules refuse. The
///         generated half adds <c>Opening.Create(…)</c>, <c>TryCreate</c> and <c>CreateOptional</c>; <c>new</c> and
///         <c>with</c> outside the type are RASK127.
///     </para>
///     <para>
///         A value EF Core loads from a row is never checked again: it is what was stored, even when a rule has been
///         tightened since. <see cref="Text" />, <see cref="Number" />, <see cref="Whole" />, <see cref="Day" />, <see cref="Moment" /> and
///         <see cref="Range" /> are the bases that declare their rules as arguments instead.
///     </para>
/// </remarks>
public abstract record Value
{
    /// <summary>Checks <see cref="Rules" />, unless the value is being loaded from the database.</summary>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A rule refuses the value.</exception>
    protected Value()
        : this(checkNow: true)
    {
    }

    // The bases that hold state of their own (Text's text, a range's ends) check once it is set: C# runs this
    // constructor before theirs, but after the derived record's positional properties are assigned.
    private protected Value(bool checkNow)
    {
        if (checkNow)
        {
            Check(null);
        }
    }

    /// <summary>What is wrong with the value, if anything: yield one <see cref="Problem" /> per broken rule.</summary>
    /// <returns>Each rule the value breaks; none when it may be kept.</returns>
    /// <remarks>
    ///     Asked from the constructor, after the record's positional properties are set — so they can be read
    ///     here — and never when the value is loaded from a row.
    /// </remarks>
    protected virtual IEnumerable<Problem> Rules() => [];

    // The base's own problems, then the record's Rules(): refused together, or recorded by the TryCreate in flight.
    private protected void Check(List<Problem>? problems)
    {
        if (Loading.IsOn)
        {
            return;
        }

#pragma warning disable S1699 // asking the derived record's rules as it is built is what a value object is
        foreach (var problem in Rules())
#pragma warning restore S1699
        {
            (problems ??= []).Add(problem);
        }

        if (problems is { Count: > 0 })
        {
            ValueAttempt.Refuse(problems);
        }
    }
}
