namespace Rask.Core.Components;

// Tagless container: renders its Children with no wrapping element. Internal — authors express
// multi-root / grouped content with a `[a, b]` collection expression (built here via
// Component.Create), not by naming Fragment. The constructors serve `new Fragment(children)` call
// sites inside the framework (ErrorBoundary, ValidationMessage, the collection builder, etc.).
internal sealed class Fragment : Component
{
    public Fragment() => Children = [];
    public Fragment(IEnumerable<Component?>? children) => Children = children ?? [];
    public Fragment(params Component?[] children) : this((IEnumerable<Component?>)children) { }
}
