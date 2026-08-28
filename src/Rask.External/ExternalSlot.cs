using Rask.Core;

namespace Rask.External;

/// <summary>
///     Assigns a group of Rask-rendered children to a named slot of the island they sit inside.
/// </summary>
/// <remarks>
///     <para>
///         Named <c>ExternalSlot</c> rather than <c>Slot</c> because <c>Slot</c> is already the HTML
///         <c>&lt;slot&gt;</c> element in <c>Rask.Html.Components</c>, and a component whose name
///         collides with a tag entry does not compile (CS0108, fatal under <c>-warnaserror</c>) — the
///         same trap an island prop named <c>Title</c> falls into.
///     </para>
///     <para>
///         This never reaches the serializer. The island reads the name and the children off it and
///         emits them into a slot template; an <c>ExternalSlot</c> outside an island renders nothing,
///         because there is nothing for it to be a slot of.
///     </para>
///     <example>
///         <code>
///         Panel.Heading("Sales")[
///             ExternalSlot.Named("footer")[ BsButton["Save"] ],
///             Table.Rows(_rows),                              // the default slot
///         ]
///         </code>
///     </example>
/// </remarks>
public sealed partial class ExternalSlot : Component
{
    /// <summary>
    ///     The slot this content belongs to, as the front-end component names it.
    /// </summary>
    /// <remarks>
    ///     Called <c>Named</c> rather than <c>Name</c> because <c>Name</c> is already a chain step on
    ///     other components (<c>Button.Name</c>, among others) and the entry would be ambiguous at the
    ///     call site — the third naming collision this feature hit, after <c>Slot</c> and <c>Title</c>.
    /// </remarks>
    public required string Named { get; set; }

    /// <summary>Renders nothing on its own — see the remarks.</summary>
    protected override Component? Render() => null;
}
