namespace Rask.Site.DataDemo;

/// <summary>A row of a note's checklist form; <paramref name="Id" /> names a stored item, none a new one.</summary>
/// <param name="Id">The item it stands for, or null for a new one.</param>
/// <param name="Text">What the item says.</param>
public sealed record NoteItemSpec(Guid? Id, string Text);
