namespace Rask.Core.Components;

// [FactoryPositionalChildren] tells the generator to emit the single positional
// `Fragment(params IEnumerable<Child> Children)` factory shape — no leading universal
// attribute params (Id/Class/Style/Data have no effect on a tagless container anyway).
// The legacy constructors below keep working for `new Fragment(children)` call sites
// inside the framework (ErrorBoundary, ValidationMessage, etc.).
[FactoryPositionalChildren]
public sealed class Fragment : Component
{
    public Fragment() => Children = [];
    public Fragment(IEnumerable<Child>? children) => Children = children ?? [];
    public Fragment(params Child[] children) : this((IEnumerable<Child>)children) { }
}
