using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Rask.Core.Diagnostics;
using Rask.Core.Messaging;
using Rask.Cqrs.Server;
using Rask.Wire;

namespace Rask.Server.Errors;

// What a request that is not a page is told when it throws: a problem document (RFC 9457) with the status the
// ErrorStatuses table gives. A fault says nothing of itself, only the reference its Rask.Errors line carries — the
// one the error page would show for it; a refusal says its own sentence, which was written for the caller.
//
// A page never gets here: Rask draws its own error page and answers for it. What does is everything else the host
// serves — a minimal endpoint, a controller, a sign-in endpoint.
internal sealed class ProblemAnswer(IProblemDetailsService problems, RaskServerOptions options) : IExceptionHandler
{
    private const string Reached = "a problem response";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (LeftToTheErrorPage(httpContext))
        {
            return false;
        }

        var status = options.Errors.Of(exception);
        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = Problem(exception, status),
            Exception = exception,
        }).ConfigureAwait(false);
    }

    // A browser navigating, on a host that re-executes an error page (RaskApp's /error): that page answers it, as
    // it did before. Measured on .NET 10: under ExceptionHandlingPath, Request.Path is already the re-executed path
    // by the time a handler runs, and the path that threw is the feature's. Only a GET is sent on, since a POST
    // re-executed onto a page that answers GET alone comes back 405.
    internal static bool LeftToTheErrorPage(HttpContext context)
    {
        var request = context.Request;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        if (context.Features.Get<IExceptionHandlerPathFeature>() is not { } thrown || thrown.Path == request.Path)
        {
            return false;
        }

        return request.GetTypedHeaders().Accept.Any(static accepted => accepted.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase));
    }

    internal static ProblemDetails Problem(Exception exception, int status)
    {
        var problem = new ProblemDetails { Status = status };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            problem.Extensions["reference"] = ErrorReference.Of(exception, Reached);
            return problem;
        }

        if (Refusal.Find(exception) is { } refusal)
        {
            ErrorReference.Record(exception, Reached);
            problem.Detail = Refusal.Sentence(refusal);
            if (refusal.Any(static failure => failure.Fields.Count > 0))
            {
                problem.Extensions["errors"] = ByField(refusal);
            }

            return problem;
        }

        RaskDiagnostics.Report(
            RaskLogLevel.Information, ErrorReference.Category,
            $"A request was answered {status} for {exception.GetType().Name}.");

        // Written for the caller where it was thrown: a rejection from a decoder.
        if (exception is BadRequestException rejected)
        {
            problem.Title = rejected.Title;
            problem.Detail = rejected.Detail;
        }

        return problem;
    }

    // ASP.NET's own `errors` shape (ValidationProblemDetails): each message under every field its failure names,
    // and under the empty key what is about the request as a whole.
    private static Dictionary<string, string[]> ByField(IReadOnlyList<FieldFailure> failures) =>
        failures
            .SelectMany(static failure => (failure.Fields.Count > 0 ? failure.Fields : [string.Empty])
                .Select(field => (Field: field, failure.Message)))
            .GroupBy(static pair => pair.Field, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static pair => pair.Message).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
}
