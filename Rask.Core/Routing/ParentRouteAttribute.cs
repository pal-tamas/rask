namespace Rask.Core.Routing;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ParentRouteAttribute(Type parent) : Attribute
{
    public Type Parent { get; } = parent;
}
