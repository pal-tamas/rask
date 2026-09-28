namespace Rask.Generators.Translations;

// A hard problem with a catalog: it cannot generate code, or the code it generated would throw.
internal sealed class CatalogDefect(string reason, int line, int column)
{
    public string Reason { get; } = reason;
    public int Line { get; } = line;
    public int Column { get; } = column;
}
