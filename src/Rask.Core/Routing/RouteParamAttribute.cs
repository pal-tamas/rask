namespace Rask.Core.Routing;

/// <summary>
///     Fills this property from a <c>{…}</c> path segment of the page's <see cref="RouteAttribute" />
///     template. For a query-string value use <c>[QueryParam]</c> instead.
/// </summary>
/// <remarks>
///     The property name is matched against the template segment, so <c>{id}</c> binds to <c>Id</c>. Pass
///     <paramref name="name" /> when the two should differ. The property must be a <see langword="string" />
///     or implement <see cref="IParsable{TSelf}" /> — which covers <see cref="int" />, <see cref="Guid" />,
///     enums and your own types. RASK008 reports a <c>[RouteParam]</c> with no matching segment, and
///     RASK005 a CLR type that disagrees with the template's constraint.
///     <para>
///         A route parameter is user input, whatever its declared type: <c>{id:int}</c> proves the segment
///         is an integer, never that it is one this visitor may see. Authorize on the way to the data, not
///         on the shape of the URL.
///     </para>
/// </remarks>
/// <param name="name">The template segment to read, when it differs from the property name.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RouteParamAttribute(string? name = null) : Attribute
{
    /// <summary>
    ///     The template segment to read, or <see langword="null" /> to use the property name.
    /// </summary>
    public string? Name { get; } = name;
}
