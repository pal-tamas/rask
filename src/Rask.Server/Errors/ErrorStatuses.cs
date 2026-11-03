using System.Reflection;
using Microsoft.AspNetCore.Http;
using Rask.Cqrs;
using Rask.Cqrs.Server;
using Rask.Wire;

namespace Rask.Server;

// The status a request that is not a page answers with when it throws: one table, from the exception to the status,
// that the app adds to through RaskServerOptions.Answer. What the app answers wins, matched on the exception's own
// type and then on each type it derives from; below that, Rask's own. Anything else is a fault, and answers 500.
internal sealed class ErrorStatuses
{
    private readonly Dictionary<Type, int> _answers = [];

    /// <summary>Answers <paramref name="thrown" />, and every exception derived from it, with <paramref name="status" />.</summary>
    internal void Answer(Type thrown, int status)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(status, StatusCodes.Status400BadRequest);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(status, 599);
        _answers[thrown] = status;
    }

    /// <summary>The status <paramref name="thrown" /> answers with.</summary>
    internal int Of(Exception thrown)
    {
        for (var type = thrown.GetType(); type is not null; type = type.BaseType)
        {
            if (_answers.TryGetValue(type, out var status))
            {
                return status;
            }
        }

        return thrown switch
        {
            // What a call wraps an exception in says nothing of its own; the exception inside does.
            TargetInvocationException { InnerException: { } inner } => Of(inner),
            AggregateException { InnerExceptions: [var only] } => Of(only),
            BadRequestException rejected => rejected.Status,
            BadHttpRequestException malformed => malformed.StatusCode,
            ForbiddenException { IsAuthenticated: true } => StatusCodes.Status403Forbidden,
            ForbiddenException => StatusCodes.Status401Unauthorized,
            IFieldFailures { Failures.Count: > 0 } => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError,
        };
    }
}
