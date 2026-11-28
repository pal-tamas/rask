namespace Rask.Generators.Shared;

/// <summary>
///     How a value object of one value is written by System.Text.Json and the wires that agree with it: as the value it
///     holds, bare (<see cref="ValueObjectJson" />).
/// </summary>
/// <param name="Member">The member that holds the value — <c>Value</c> for a record declared from a base that holds it.</param>
/// <param name="HeldType">The held value's type, fully qualified.</param>
/// <param name="Read">
///     The expression that reads the held value off <c>reader</c>, naming <c>property</c> in its error; empty for a
///     moment, which <c>MomentJsonConverter</c> reads.
/// </param>
/// <param name="Write">The statement that writes it, with <c>{0}</c> the held value; empty for a moment.</param>
/// <param name="Moment">Whether it is a moment, written as its UTC instant rather than its held value.</param>
internal sealed record BareJson(string Member, string HeldType, string Read, string Write, bool Moment);
