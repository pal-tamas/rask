namespace Rask.Cqrs;

/// <summary>Which of the four message shapes a remote contract describes.</summary>
public enum RemoteMessageKind
{
    /// <summary>An <see cref="IQuery{TResult}" /> — safe and idempotent, so it travels as a GET.</summary>
    Query,

    /// <summary>An <see cref="ICommand" /> — travels as a POST and answers with no value.</summary>
    VoidCommand,

    /// <summary>An <see cref="ICommand{TResult}" /> — travels as a POST and answers with a value.</summary>
    ResultCommand,

    /// <summary>An <see cref="INotification" /> — travels as a POST and is accepted, not answered.</summary>
    Notification,

    /// <summary>
    ///     An <see cref="ISubscription{TNotification}" /> — travels as a GET to the event stream and is answered with
    ///     every notification it matches, for as long as it stays open.
    /// </summary>
    Subscription,
}
