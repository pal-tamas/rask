using System.Text;

namespace Rask.Ui;

/// <summary>
/// The base of every kit component that IS one HTML element — a button, a badge, a table, a list.
/// </summary>
/// <remarks>
/// <para>
/// Deriving from <see cref="Element" /> rather than wrapping one is what gives a kit component every step an
/// element takes — <c>Id</c>, <c>Class</c>, <c>Style</c>, <c>Data</c>, <c>Role</c>, <c>TabIndex</c>,
/// <c>Aria</c>, <c>Attributes</c> and the whole event surface — with nothing redeclared, so none of it can
/// drift out of step with Core or be forgotten on one component and present on the next.
/// </para>
/// <para>
/// A kit component adds its own classes through <see cref="Element.ResolveClass" /> and its own ARIA through
/// <see cref="ResolveAria" />. Both compose with what the call site set instead of replacing it: a
/// <c>.Class("mt-2")</c> is added to the kit's classes, and an <c>.Aria("label", …)</c> the call site
/// writes wins over one the kit would have derived.
/// </para>
/// <para>
/// One tag. An element's children are serialized straight from the indexer's array, so an element-derived
/// component has nowhere to put markup of its own around or between them — anything it shows is either an
/// attribute or a child the call site passes.
/// </para>
/// </remarks>
public abstract partial class UiElement : Element
{
    /// <summary>
    ///     The <c>aria-*</c> attributes this element renders — by default exactly <see cref="Element.Aria" />.
    ///     The twin of <see cref="Element.ResolveClass" />: override it to add what the kit can derive from
    ///     the component's own props, such as the accessible name of a button that shows only an icon.
    /// </summary>
    /// <returns>
    ///     The bag to write, or <see langword="null" /> for none. An override starts from <see cref="Element.Aria" />
    ///     and lets the call site's entries win, because a label someone wrote by hand is the one they meant.
    /// </returns>
    protected virtual IReadOnlyDictionary<string, string?>? ResolveAria() => Aria;

    /// <inheritdoc />
    /// <remarks>
    ///     Core writes <c>aria-*</c> from <see cref="Element.Aria" /> in the middle of its attribute walk — after
    ///     role and tabindex, before <see cref="Element.Attributes" /> and any tag-specific attribute — and that
    ///     order is part of its contract. Appending the resolved entries afterwards would break it, and would
    ///     write a key twice when the call site had set it too. So the resolved bag stands in for the call
    ///     site's for the length of the walk, and the call site's is put back before this returns, including
    ///     when the walk throws. An element that resolves nothing new takes the walk untouched.
    /// </remarks>
    protected override void WriteAttributes(StringBuilder sb)
    {
        var own = Aria;
        var resolved = ResolveAria();
        if (ReferenceEquals(resolved, own))
        {
            base.WriteAttributes(sb);
            return;
        }

        Aria = resolved;
        try
        {
            base.WriteAttributes(sb);
        }
        finally
        {
            Aria = own;
        }
    }
}
