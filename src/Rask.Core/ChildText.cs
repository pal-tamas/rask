using System.Globalization;

namespace Rask.Core;

/// <summary>
///     How a value renders as an island's child text — the same spelling <see cref="Component" />'s own child conversions
///     give it.
/// </summary>
/// <remarks>
///     Invariant, so the props JSON an island child travels in stays byte-stable across cultures, as the HTML a Rask child
///     renders does. <see cref="Component" />'s conversions keep their own spelling on the render hot path rather than
///     calling through here; <c>IslandChildTextTests</c> pins the two to the same text under a culture where they would
///     otherwise differ ("3,5" against "3.5").
/// </remarks>
internal static class ChildText
{
    internal static string Of<T>(T value)
        where T : IFormattable =>
        value.ToString(null, CultureInfo.InvariantCulture);

    internal static string Of(bool value) => value ? "True" : "False";

    internal static string Of(char value) => value.ToString();

    internal static string Of(Guid value) => value.ToString();
}
