using System.Collections.Concurrent;
using System.Globalization;
using Rask.Core.Globalization;

namespace Rask;

/// <summary>
///     The order a language puts text in, for a list already in memory: <c>names.Order(TextOrder.Current)</c>,
///     <c>rows.OrderBy(r =&gt; r.Name, TextOrder.In("hu"))</c>. Hungarian puts <c>Á</c> with <c>A</c>, <c>Cs</c> after
///     <c>Cu</c>, and <c>Ö</c> after every <c>O</c> — where an ordinal sort puts every accented letter after <c>Z</c>.
/// </summary>
/// <remarks>
///     <para>
///         Case does not change the order. A query orders in the database instead, with <c>x.Name.InTextOrder()</c>,
///         by the collation the model names once with <c>c.OrderTextBy("Hungarian_100_CI_AS")</c> in an
///         <c>IRaskModel</c>'s <c>Conventions</c>.
///     </para>
///     <para>
///         It needs the runtime's culture data: a browser app ships it once <c>Rask:Cultures</c> names a language that is
///         not English, and without it every language orders as the invariant culture does.
///     </para>
/// </remarks>
public static class TextOrder
{
    private static readonly ConcurrentDictionary<string, StringComparer> Comparers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     The visitor's language's order — and a declaration, as <c>Culture</c> is, that the component being rendered
    ///     depends on their culture.
    /// </summary>
    public static StringComparer Current => For(RaskCulture.Current);

    /// <summary>The order of the language <paramref name="culture" /> names: <c>TextOrder.In("hu")</c>.</summary>
    /// <param name="culture">A BCP&#160;47 tag — <c>"hu"</c>, <c>"hu-HU"</c>.</param>
    /// <returns>A comparer that orders text as that language does, whatever the case.</returns>
    public static StringComparer In(string culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(culture);
        return For(CultureInfo.GetCultureInfo(culture));
    }

    private static StringComparer For(CultureInfo culture) =>
        Comparers.GetOrAdd(culture.Name, static (_, c) => StringComparer.Create(c, ignoreCase: true), culture);
}
