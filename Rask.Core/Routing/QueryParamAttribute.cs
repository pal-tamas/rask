namespace Rask.Core.Routing;

[AttributeUsage(AttributeTargets.Property)]
public sealed class QueryParamAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}
