namespace Rask.Generators.ScopedScripts;

internal sealed class TsParam(string name, ITsType type, bool optional, bool rest)
{
    public string Name { get; } = name;
    public ITsType Type { get; } = type;
    public bool Optional { get; } = optional;
    public bool Rest { get; } = rest;
}
