namespace Rask.Cqrs;

// The non-generic shape, for the one caller that cannot use the generic one.
//
// A client replaces the generated invoker with a remote one (AddRaskCqrsClient), so the pipeline — and
// with it ValidationBehavior — is bypassed for anything that travels. The remote transport sees the
// message as `object`, and resolving IRequestValidator<TRequest> from that would mean MakeGenericType:
// reflection, in the one package whose whole point is that it has none and publishes trim-clean.
/// <summary>
///     Validates a request that is about to be sent to a server, before it is sent.
///     <para>
///         Registered for you. This is a convenience, not a control — the server validates again, and
///         that run is the one that decides.
///     </para>
/// </summary>
public interface IRemoteRequestValidator
{
    /// <summary>Checks a request about to leave for the server.</summary>
    /// <param name="request">The query or command being sent.</param>
    /// <returns>Every failure found; empty when the request is valid.</returns>
    ValueTask<IReadOnlyList<RequestValidationError>> Validate(object request);
}
