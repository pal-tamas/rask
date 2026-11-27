namespace Rask.Data;

/// <summary>
///     What one <c>b.NoOverlap(…)</c> said, by property name, until the model is finalized and
///     <see cref="NoOverlapConvention" /> resolves it to columns (<see cref="NoOverlapRule" />).
/// </summary>
/// <param name="Keys">The properties two rows must share to conflict.</param>
/// <param name="Range">The <c>Range</c> value object holding both ends, or null when <paramref name="From" /> and <paramref name="To" /> name them.</param>
/// <param name="From">The property of the inclusive start, or null for a <paramref name="Range" />.</param>
/// <param name="To">The property of the exclusive end — null in a row is open — or null for a <paramref name="Range" />.</param>
/// <param name="Message">What a refused row is told, under the range's field.</param>
internal sealed record NoOverlapDeclaration(IReadOnlyList<string> Keys, string? Range, string? From, string? To, string Message)
{
    /// <summary>The entity-type annotation the declarations are kept under while the model is built.</summary>
    internal const string Annotation = "Rask:NoOverlap:Declared";

    /// <summary>The rows the rule holds, or null for every row.</summary>
    internal DeclaredExpression? Rows { get; set; }
}
