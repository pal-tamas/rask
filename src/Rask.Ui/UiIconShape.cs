namespace Rask;

/// <summary>One shape of an icon, as <see cref="UiIconPaths" /> holds it.</summary>
/// <param name="Mark">
///     <see cref="Plain" /> for path data drawn the variant's ordinary way, or one of the marks beside it.
/// </param>
/// <param name="Data">The path data, or for <see cref="Rectangle" /> its <c>x y width height rx</c>.</param>
internal readonly record struct UiIconShape(byte Mark, string Data)
{
    /// <summary>No mark: the first byte is already path data.</summary>
    internal const byte Plain = 0;

    /// <summary>A path filled by the even-odd rule.</summary>
    internal const byte EvenOdd = (byte)'E';

    /// <summary>An outline path whose stroke has a round join and no round cap.</summary>
    internal const byte RoundJoin = (byte)'J';

    /// <summary>A rounded rectangle rather than a path.</summary>
    internal const byte Rectangle = (byte)'R';
}
