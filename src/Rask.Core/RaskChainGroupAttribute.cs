using System.ComponentModel;

namespace Rask.Core;

/// <summary>
/// Puts this component's chain entry on a GROUP class instead of the markup surface: <c>Ui.Button</c> rather than
/// <c>UiButton</c>.
/// </summary>
/// <remarks>
/// <para>
/// A component library groups its components under one name, so a call site reads by family — <c>Ui.Button</c>,
/// <c>Ui.Card</c>, <c>Trigger.Fullscreen</c> — and typing the group lists everything in it. The grouped component
/// gets no bare entry at all, so its name never shadows an HTML tag (<c>Button</c> stays the <c>&lt;button&gt;</c>).
/// </para>
/// <para>
/// The member is named after the component with the group's name taken off the front or the back —
/// <c>UiButton</c> in <c>Ui</c> is <c>Ui.Button</c>, <c>FullscreenTrigger</c> in <c>Trigger</c> is
/// <c>Trigger.Fullscreen</c> — or as <paramref name="member" /> says when that does not fit.
/// </para>
/// <para>
/// Inherited, so a library states it once on its base class. It applies only where the group class is declared in
/// the component's own assembly: the group is re-opened as a <c>partial</c> to add the member, which cannot be done
/// to another assembly's type — so an app's component deriving from a library's base keeps its own bare entry.
/// </para>
/// <para>
/// For component libraries; an application's own components are reached by their bare names.
/// </para>
/// </remarks>
/// <param name="group">The <c>partial</c> class the entry is added to.</param>
/// <param name="member">The entry's name in the group, or <c>null</c> to derive it from the component's name.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class RaskChainGroupAttribute(Type group, string? member = null) : Attribute
{
    /// <summary>The class the entry is added to.</summary>
    public Type Group { get; } = group;

    /// <summary>The entry's name in <see cref="Group" />, or <c>null</c> to derive it from the component's name.</summary>
    public string? Member { get; } = member;
}
