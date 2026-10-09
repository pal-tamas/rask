namespace Rask.Wire;

/// <summary>
///     Carried by an exception that names the fields it is about, so a form can show each message under
///     its field in place of failing the submit.
/// </summary>
/// <remarks>
///     A form whose submit handler throws one stays on the page, with every failure shown where the
///     reader can correct it. The handler writes no <c>try</c>/<c>catch</c>.
/// </remarks>
public interface IFieldFailures
{
    /// <summary>Every failure, in the order they were found.</summary>
    IReadOnlyList<FieldFailure> Failures { get; }
}
