using System.Collections;
using System.Runtime.CompilerServices;
using Rask.Core.Components;

namespace Rask.Core;

/// <summary>
///     The return type of <see cref="Component.Render" /> and the <c>Head</c> override.
///     Accepts a single <see cref="Component" /> implicitly, so single-element bodies
///     (<c>Render() =&gt; Div()[...]</c>) and existing <c>Fragment()[...]</c> bodies compile
///     unchanged, AND supports C# collection-expression syntax so authors can write
///     <c>Render() =&gt; [Doctype(), Html(...)]</c> instead of <c>Fragment()[Doctype(), Html(...)]</c>.
///     A collection expression wraps its items in a <see cref="Fragment" /> internally, so the
///     entire downstream render pipeline keeps operating on a single <see cref="Component" />.
///     <para>
///         <c>default(RenderResult)</c> means "render nothing" / "no head contribution" (the base
///         <c>Head</c> default). Because this is a non-nullable value type, bodies use <c>default</c>
///         rather than <c>null</c> for "nothing" — including conditional bodies, which target-type
///         each branch: <c>Head =&gt; cond ? [Title(...)] : default</c>.
///     </para>
/// </summary>
[CollectionBuilder(typeof(RenderResult), nameof(Create))]
public readonly struct RenderResult : IEnumerable<Child>
{
    private readonly Component? _component;

    private RenderResult(Component? component) => _component = component;

    // [a, b] -> wrap the items in a Fragment, mirroring Fragment()[a, b].
    public static RenderResult Create(ReadOnlySpan<Child> items)
    {
        var arr = new Child[items.Length];
        items.CopyTo(arr);
        return new RenderResult(new Fragment { Children = arr });
    }

    // Single-element and existing Fragment()[...] bodies: Component -> RenderResult.
    public static implicit operator RenderResult(Component component) => new(component);

    // Consumed by RenderForLive (the Render path). default(RenderResult) never reaches here
    // because the base Render() returns `this`; the empty-Fragment fallback is defensive.
    public Component ToComponent() => _component ?? new Fragment();

    // Consumed by HeadInternal: default(RenderResult) -> null ("no head contribution").
    public Component? ToComponentOrNull() => _component;

    // Required by the collection-expression rules: a [CollectionBuilder] target needs an
    // iteration type equal to the Create span's element type (Child) — otherwise CS9188.
    // Never actually enumerated at runtime; RenderResult is consumed via ToComponent().
    public IEnumerator<Child> GetEnumerator()
    {
        if (_component is Fragment f && f.Children is { } children)
        {
            foreach (var c in children)
            {
                yield return c;
            }
        }
        else if (_component is not null)
        {
            yield return _component;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
