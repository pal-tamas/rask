using Rask.Cqrs;

namespace Rask.Background;

/// <summary>
///     Asks the server for the rows of the work its sender enqueued — all of it, or one <paramref name="Lane" />'s: what the
///     browser half of an app reads behind <c>Ui.WorkNotices</c> and <c>Ui.WorkList</c> (#1348).
/// </summary>
/// <remarks>
///     Like <see cref="WatchOwnWork" /> it carries no user: the server answers it from the <see cref="IWorkBoard" /> of the
///     request's own scope, which reads who is signed in there — so a browser asks for its user's work and gets nothing else.
/// </remarks>
/// <param name="Lane">The lane to read, or null for every lane.</param>
internal sealed record ReadOwnWork(string? Lane) : IQuery<OwnWorkRead>;
