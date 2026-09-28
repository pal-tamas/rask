namespace Rask.Generators.ScopedScripts;

internal sealed class TsArray(ITsType element) : ITsType
{
    public ITsType Element { get; } = element;
}
