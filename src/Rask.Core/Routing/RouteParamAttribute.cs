namespace Rask.Core.Routing;

[AttributeUsage(AttributeTargets.Property)]
public sealed class RouteParamAttribute(string? name = null) : Attribute
{
    public string? Name { get; } = name;
}
