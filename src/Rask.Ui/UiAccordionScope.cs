namespace Rask;

/// <summary>What a <see cref="UiAccordion" /> and its <see cref="UiAccordionItem" /> tell the parts inside them.</summary>
/// <remarks>
///     The parts are written by the caller, inside the accordion's children, so there is no call site at which
///     to hand them down.
/// </remarks>
/// <param name="Group">The name the items of an exclusive accordion share, or <c>null</c>.</param>
/// <param name="Reverse">The chevron goes before the heading.</param>
/// <param name="Transition">Items open and close over a quarter of a second.</param>
/// <param name="Disabled">The item around this part cannot be opened or closed.</param>
internal sealed record UiAccordionScope(string? Group, bool Reverse, bool Transition, bool Disabled)
{
    /// <summary>What a part outside any accordion is drawn with.</summary>
    internal static UiAccordionScope None { get; } = new(null, false, false, false);
}
