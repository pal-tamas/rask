namespace Rask.Core.Forms;

/// <summary>One value a <see cref="FieldKind.Choice" /> field may take, and what the reader is shown for it.</summary>
/// <param name="Value">The value: an enum member, boxed.</param>
/// <param name="Label">Its label: the member's <c>[Display(Name = …)]</c>, else its name.</param>
public sealed record FieldOption(object Value, string Label);
