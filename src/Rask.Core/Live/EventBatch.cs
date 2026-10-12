using System.Text.Json;
using Rask.Core.Forms;

namespace Rask.Core.Live;

/// <summary>
///     What both hosts read off a <c>batch</c> frame — the events one browser task produced, sent together
///     (<c>rask-batch.ts</c>).
/// </summary>
internal static class EventBatch
{
    /// <summary>The most events one batch carries: what the client sends at most, the rest going in a frame of their own.</summary>
    internal const int MaxEvents = 256;

    /// <summary>The handler an entry of a batch names, or null for anything that is not an event for one.</summary>
    internal static string? HandlerIdOf(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;

    /// <summary>How many of a batch's entries are events for a handler.</summary>
    internal static int CountEvents(JsonElement events) =>
        events.EnumerateArray().Count(static entry => HandlerIdOf(entry) is not null);

    /// <summary>
    ///     Tells each form whose submit is among <paramref name="events" /> that it is coming, so the changes
    ///     ahead of it in the batch leave the store's rules to the submit, which asks them of the whole model.
    /// </summary>
    /// <remarks>
    ///     A form is matched by the handler its own last render registered for its submit, so a change of one
    ///     form is never held back for the submit of another: a form with no submit here is told nothing and
    ///     asks as it always did. The submit clears the mark as it starts, and <see cref="Withdraw" /> clears
    ///     whatever is left when the batch is over.
    /// </remarks>
    /// <param name="view">The session's root, which holds its forms and its handlers.</param>
    /// <param name="events">The batch.</param>
    /// <returns>The forms that were told, or null when none was.</returns>
    internal static List<EditContext>? AnnounceSubmits(Component view, JsonElement events)
    {
        if (view.PersistedForms is not { Count: > 0 } forms || events.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<EditContext>? told = null;
        foreach (var entry in events.EnumerateArray())
        {
            if (!IsSubmit(entry) || HandlerIdOf(entry) is not { } id || view.HandlerOf(id) is not { } handler)
            {
                continue;
            }

            foreach (var form in forms.Values)
            {
                if (ReferenceEquals(form.SubmitHandler, handler) && !form.SubmitFollows)
                {
                    form.SubmitFollows = true;
                    (told ??= []).Add(form);
                }
            }
        }

        return told;
    }

    /// <summary>Takes back what <see cref="AnnounceSubmits" /> said, for a submit that never ran.</summary>
    /// <param name="told">What it returned.</param>
    internal static void Withdraw(List<EditContext>? told)
    {
        foreach (var form in told ?? [])
        {
            form.SubmitFollows = false;
        }
    }

    private static bool IsSubmit(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty("type", out var type) && type.ValueEquals("submit"u8);
}
