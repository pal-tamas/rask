using Microsoft.EntityFrameworkCore;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     The one sentence for "somebody else saved this since you read it": a form opened on version 3 of a row that
///     is at version 4 now, refused as a failure about the whole form rather than as EF Core's
///     <see cref="DbUpdateConcurrencyException" />.
/// </summary>
/// <remarks>
///     Said by <c>Find(id, version)</c> before anything is changed, and by every generated write that saves through
///     a context of its own — <c>Update</c>, <c>Delete</c>, <c>Save()</c> — when the commit finds the version moved. A
///     write STAGED on a caller's context (<c>db:</c>) leaves the caller's own <c>SaveChangesAsync</c> to throw EF's
///     exception, since it is theirs to catch.
/// </remarks>
internal static class StaleWrite
{
    /// <summary>What the refusal says: the strings catalog's English.</summary>
    internal const string Message = "Someone else changed this in the meantime. Reload it and try again.";

    /// <summary>
    ///     The sentence in the reader's language — <c>RaskString.ValidationChangedSince</c> through the strings catalog,
    ///     which the host puts here (Rask.Data's server build does not reference Rask.Core); the English without one.
    /// </summary>
    internal static Func<string> Sentence { get; set; } = static () => Message;

    /// <summary>The refusal, carrying what actually failed for the log.</summary>
    internal static RaskValidationException Refusal(Exception? cause = null) =>
        new([new FieldFailure(Sentence(), [])], cause);

    /// <summary>Saves <paramref name="context" />, refusing a stale save with the sentence instead of EF's exception.</summary>
    internal static async Task SaveAsync(DbContext context, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException stale)
        {
            throw Refusal(stale);
        }
    }
}
