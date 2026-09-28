namespace Rask.Generators.ScopedScripts;

internal sealed class TsProperty(string name, ITsType type, bool optional)
{
    public string Name { get; } = name;
    public ITsType Type { get; } = type;
    public bool Optional { get; } = optional;
}
