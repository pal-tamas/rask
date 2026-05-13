namespace Rask.Core.Components;

public sealed class Fragment : Component
{
    // The generator emits `Fragment(params IEnumerable<Child> Children)` because Children
    // is a public mutable property inherited from Component. These extra constructors stay
    // so legacy `new Fragment(children)` call sites keep working — they pre-date the
    // factory-based pattern.
    public Fragment() => Children = [];
    public Fragment(IEnumerable<Child>? children) => Children = children ?? [];
    public Fragment(params Child[] children) : this((IEnumerable<Child>)children) { }
}
