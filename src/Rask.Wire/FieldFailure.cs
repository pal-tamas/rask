namespace Rask.Wire;

/// <summary>One thing wrong with what was submitted: what to say, and the fields it is about.</summary>
/// <remarks>
///     <para>
///         A field is named as the form's model names it: <c>Name</c>, <c>Price.Amount</c>,
///         <c>Lines[2].ValidFrom</c>.
///     </para>
///     <code>
///     // A unique index over two columns: the message under both.
///     new FieldFailure("That invoice number is taken.", ["Year", "Number"]);
///
///     // A range that overlaps another: the message under the field the rule is about, the two bounds marked.
///     new FieldFailure("This driver is already booked then.", ["DriverId"], ["ValidFrom", "ValidTo"]);
///     </code>
/// </remarks>
/// <param name="Message">The message, written for the person filling in the form.</param>
/// <param name="Fields">
///     The fields the message is shown under. Empty for a failure about the submission as a whole.
/// </param>
/// <param name="Marked">The fields that are marked invalid and carry no message of their own.</param>
/// <param name="Source">What found the failure, for the log: the name of an index or of a rule.</param>
public sealed record FieldFailure(
    string Message,
    IReadOnlyList<string> Fields,
    IReadOnlyList<string>? Marked = null,
    string? Source = null);
