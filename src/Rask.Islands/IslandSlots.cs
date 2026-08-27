using System.Text;
using Rask.Core;

namespace Rask.Islands;

/// <summary>
///     Groups an island's children into the slot templates its adapter mounts them from.
/// </summary>
/// <remarks>
///     <para>
///         Called from the generated <c>RenderChildren</c> override, not by you.
///     </para>
///     <para>
///         Children go into a <c>&lt;template&gt;</c> rather than straight into the host element for
///         two reasons. A template's content is inert — the browser parses it but does not render it,
///         so slot content cannot flash on screen in the window between first paint and the island
///         mounting. And it is the client runtime, not the server, that decides where those nodes end
///         up, because only the adapter knows where its framework wants them.
///     </para>
/// </remarks>
[RaskMarkup]
public static partial class IslandSlots
{
    /// <summary>The slot an unassigned child belongs to.</summary>
    public const string DefaultSlot = "default";

    /// <summary>The attribute the client runtime finds a slot by.</summary>
    public const string SlotAttribute = "data-rask-slot";

    /// <summary>
    ///     Rewrites an island's children as one template per slot, in declaration order.
    /// </summary>
    /// <remarks>
    ///     Returns the children unchanged when there are none, so an island used as a leaf — which is
    ///     every island until someone passes it content — emits exactly what it did before slots
    ///     existed, and pays nothing for them.
    /// </remarks>
    public static IEnumerable<Component?> Wrap(IEnumerable<Component?>? children)
    {
        if (children is null)
        {
            yield break;
        }

        List<Component?>? unassigned = null;
        List<(string Name, List<Component?> Children)>? named = null;

        foreach (var child in children)
        {
            if (child is null)
            {
                continue;
            }

            if (child is IslandSlot slot)
            {
                named ??= [];
                named.Add((slot.Named, [.. slot.Children ?? []]));
                continue;
            }

            unassigned ??= [];
            unassigned.Add(child);
        }

        if (unassigned is not null)
        {
            yield return IslandSlotTemplate.SlotName(DefaultSlot)[unassigned];
        }

        if (named is null)
        {
            yield break;
        }

        foreach (var (name, content) in named)
        {
            yield return IslandSlotTemplate.SlotName(name)[content];
        }
    }
}

/// <summary>
///     One slot's content, as the inert <c>&lt;template data-rask-slot="…"&gt;</c> the client lifts.
/// </summary>
/// <remarks>
///     A purpose-built component rather than <c>Template.Data(…)</c>: <see cref="Element.Data" /> is an
///     <c>IReadOnlyDictionary</c>, so that form allocates a dictionary per slot per render, on the
///     render path, to carry one constant-keyed string.
/// </remarks>
internal sealed partial class IslandSlotTemplate : Component
{
    /// <summary>The slot name, as the front-end component knows it.</summary>
    public string SlotName { get; set; } = IslandSlots.DefaultSlot;

    /// <inheritdoc />
    protected override string? TagName => "template";

    /// <inheritdoc />
    protected override void WriteAttributes(StringBuilder sb) =>
        // AppendAttr, not sb.Append: it registers the Attribute frame too, which is what lets the diff
        // see a slot at all.
        AppendAttr(sb, IslandSlots.SlotAttribute, SlotName);
}
