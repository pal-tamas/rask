namespace Rask.Core.Components;

// Tagless container: renders its Children with no wrapping element. Use `Fragment()[a, b]`
// to attach children. The legacy constructors below keep working for `new Fragment(children)`
// call sites inside the framework (ErrorBoundary, ValidationMessage, etc.).
public sealed class Fragment : Component
{
    public Fragment() => Children = [];
    public Fragment(IEnumerable<Child>? children) => Children = children ?? [];
    public Fragment(params Child[] children) : this((IEnumerable<Child>)children) { }
}
