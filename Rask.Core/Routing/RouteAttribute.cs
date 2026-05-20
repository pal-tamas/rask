namespace Rask.Core.Routing;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class RouteAttribute(string template) : Attribute
{
    public string Template { get; } = template;
}
