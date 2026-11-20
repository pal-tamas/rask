using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators.Shared;

/// <summary>
/// An equatable snapshot of a symbol's source location. Incremental generator models must not hold a
/// <see cref="Location"/> directly — it isn't value-equatable, so caching it would defeat the incremental
/// cache. Capture the coordinates instead and rebuild the <see cref="Location"/> when reporting.
/// </summary>
internal sealed record SymbolLocation(
    string FilePath, int Start, int Length, int StartLine, int StartChar, int EndLine, int EndChar)
{
    public Location ToLocation() => Location.Create(
        FilePath,
        new TextSpan(Start, Length),
        new LinePositionSpan(
            new LinePosition(StartLine, StartChar),
            new LinePosition(EndLine, EndChar)));

    public static SymbolLocation? From(ISymbol symbol) => From(symbol.Locations.FirstOrDefault(l => l.IsInSource));

    /// <summary>The coordinates of <paramref name="location"/>, its line and column with them, or null outside source.</summary>
    public static SymbolLocation? From(Location? location)
    {
        if (location?.SourceTree is null)
        {
            return null;
        }

        var span = location.GetLineSpan();
        return new SymbolLocation(
            location.SourceTree.FilePath,
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            span.StartLinePosition.Line,
            span.StartLinePosition.Character,
            span.EndLinePosition.Line,
            span.EndLinePosition.Character);
    }
}
