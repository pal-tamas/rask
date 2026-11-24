using System.ComponentModel.DataAnnotations;
using Rask.Data;
using Rask.Values;

namespace Rask.Site.DataDemo;

/// <summary>
///     The one aggregate: a note with a title and a body. That is the whole declaration — the generator writes
///     <c>NoteModel</c> for the form, <c>Note.Create(model)</c> to save it and <c>Note.Where(…)</c> to query it.
/// </summary>
public sealed partial class Note : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
{
    /// <summary>What the list shows first, and what a search ranks highest.</summary>
    [MaxLength(120)]
    public string Title { get; private set; } = "";

    /// <summary>The text a search snippet is cut from.</summary>
    [MaxLength(2000)]
    public string? Body { get; private set; }

    /// <summary>A note written by code rather than by the form — the seed rows.</summary>
    internal static Note Write(string title, string body) =>
        new() { Id = Guid.CreateVersion7(), Title = title, Body = body };

    /// <summary>
    ///     An operation: its generated entry <c>Note.Rename(id, version, title)</c> saves it, and
    ///     <c>Form.For(Note.Rename, id)</c> is a form over it, checked against these rules as it is typed in.
    /// </summary>
    public void Rename(string title)
    {
        Refuse.When(string.IsNullOrWhiteSpace(title), () => Title, "A note needs a title.");
        Title = title.Trim();
    }

    /// <summary>The welcome note stays: what its generated <c>Note.Delete(id, version)</c>, and the editor's Delete before it asks, refuse.</summary>
#pragma warning disable S1144 // found by convention: the generated domain rules reach it, as every delete asks it
    private IEnumerable<Problem> ValidateDelete()
    {
        if (string.Equals(Title, Welcome, StringComparison.Ordinal))
        {
            yield return Problem.Whole("The welcome note stays.");
        }
    }
#pragma warning restore S1144

    /// <summary>The seeded note the demo keeps.</summary>
    internal const string Welcome = "Welcome to Rask";
}
