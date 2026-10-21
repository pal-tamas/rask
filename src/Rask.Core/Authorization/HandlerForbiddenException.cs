using System.Reflection;

namespace Rask.Core.Authorization;

// A handler's [Authorize] turned the person away before its body ran. Thrown rather than returned so whatever
// called the handler stops too — a lambda that would toast "Removed" after it, a form that would mark itself saved.
// The dispatch and the form catch it and say HandlerForbidden in a toast; nothing else changed.
#pragma warning disable S3871 // Rask's own: the dispatch and the form catch it, and no app code is meant to.
internal sealed class HandlerForbiddenException(string handler)
#pragma warning restore S3871
    : Exception($"{handler} was refused: the signed-in user does not meet its [Authorize].")
{
    public string Handler { get; } = handler;

    /// <summary>The refusal <paramref name="thrown" /> is, looking through the wrappers a call adds; null for anything else.</summary>
    internal static HandlerForbiddenException? Find(Exception thrown) => thrown switch
    {
        HandlerForbiddenException refused => refused,
        TargetInvocationException { InnerException: { } inner } => Find(inner),
        AggregateException many => many.Flatten().InnerExceptions.Select(Find).FirstOrDefault(found => found is not null),
        _ => null,
    };
}
