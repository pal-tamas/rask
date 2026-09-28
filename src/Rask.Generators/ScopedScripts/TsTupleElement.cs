namespace Rask.Generators.ScopedScripts;

internal sealed class TsTupleElement(string? label, ITsType type)
{
    public string? Label { get; } = label;
    public ITsType Type { get; } = type;
}
