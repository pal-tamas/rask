namespace Rask;

/// <summary>One entry a dropdown's keyboard cursor can land on, in render order.</summary>
/// <param name="Ordinal">Its position among every entry in the dropdown, submenus included.</param>
/// <param name="Parent">The ordinal of the submenu it sits in, or -1 for the top level.</param>
/// <param name="Text">What type-ahead matches against.</param>
/// <param name="Disabled">Skipped by the cursor.</param>
/// <param name="IsSub">A submenu trigger: ArrowRight opens it.</param>
internal readonly record struct UiMenuEntry(int Ordinal, int Parent, string Text, bool Disabled, bool IsSub);
