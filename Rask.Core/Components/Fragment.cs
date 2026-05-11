namespace Rask.Core.Components;

public sealed class Fragment : Component
{
    public Fragment(IEnumerable<Child>? children = null) => Children = children ?? [];
    public Fragment(params Child[] children) : this((IEnumerable<Child>)children) { }

    internal IEnumerable<Child> Children { get; }

    public override Component Render() => this;
}
