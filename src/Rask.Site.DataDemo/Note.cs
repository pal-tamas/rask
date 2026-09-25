using System.ComponentModel.DataAnnotations;
using Rask.Data;

namespace Rask.Site.DataDemo;

/// <summary>
///     The one aggregate: a note with a title and a body. That is the whole declaration — the generator writes
///     <c>NoteModel</c> for the form, <c>Note.Create(model)</c> to save it and <c>Note.Read</c> to query it.
/// </summary>
public sealed class Note : Aggregate<Guid>
{
    /// <summary>What the list shows first, and what a search ranks highest.</summary>
    [Required, MaxLength(120)]
    public string Title { get; private set; } = "";

    /// <summary>The text a search snippet is cut from.</summary>
    [MaxLength(2000)]
    public string Body { get; private set; } = "";

    /// <summary>A note written by code rather than by the form — the seed rows.</summary>
    public static Note Write(string title, string body) =>
        new() { Id = Guid.CreateVersion7(), Title = title, Body = body };
}
