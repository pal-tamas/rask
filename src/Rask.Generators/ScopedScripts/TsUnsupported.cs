namespace Rask.Generators.ScopedScripts;

internal sealed class TsUnsupported(string description) : ITsType
{
    public string Description { get; } = description;
}
