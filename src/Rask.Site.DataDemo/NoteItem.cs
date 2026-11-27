using System.ComponentModel.DataAnnotations;
using Rask.Data;

namespace Rask.Site.DataDemo;

/// <summary>One item of a note's checklist: a child of the <see cref="Note" />, saved with it.</summary>
public sealed class NoteItem : Entity<Guid>
{
    private NoteItem() { } // EF materialization

    /// <summary>What the item says.</summary>
    [MaxLength(200)]
    public string Text { get; private set; } = "";

    internal static NoteItem For(NoteItemSpec spec)
    {
        var item = new NoteItem { Id = OrderedGuid.New() };
        item.Take(spec);
        return item;
    }

    internal void Take(NoteItemSpec spec) => Text = spec.Text.Trim();
}
