using System.ComponentModel;

namespace Rask.Values;

/// <summary>
///     A value object that can be asked again, as stored, what its rules say against it: what <c>rask audit</c> asks of
///     every value a stored row holds, because a value loaded from a row is never checked as it is loaded. Implemented
///     by the generated half of every value object declared from <see cref="Value" /> or one of its bases; not an API
///     to write against.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IStoredValue
{
    /// <summary>Makes the value again from what it holds, its rules asked.</summary>
    /// <param name="kept">
    ///     Whether the value made again is the value held: <see langword="false" /> for one its rules would keep
    ///     differently — a text stored with the blanks <c>Trim</c> cuts off, or in the small letters <c>Upper</c> raises.
    /// </param>
    /// <returns>What its rules say against it; empty when they accept it.</returns>
    IReadOnlyList<Problem> Recheck(out bool kept);
}
