using System.Reflection;
using Microsoft.AspNetCore.Http;
using Rask.Cqrs;
using Rask.Cqrs.Server;

namespace Rask.Server.Tests.Errors;

// The one table from an exception to the status a request that is not a page answers with: Rask's own exceptions,
// and below them every other exception is a fault.
public sealed class ErrorStatusesTests
{
    public static TheoryData<Exception, int> RasksOwn => new()
    {
        { new RefusedException("The invoice is already closed."), 400 },
        { new RaskValidationException([new RequestValidationError("Name", "Required.")]), 400 },
        { new ForbiddenException("Admins only.", isAuthenticated: true), 403 },
        { new ForbiddenException("Sign in first.", isAuthenticated: false), 401 },
        { new BadRequestException(StatusCodes.Status413PayloadTooLarge, "Upload too large", detail: null), 413 },
        { new BadHttpRequestException("Malformed.", StatusCodes.Status400BadRequest), 400 },
        { new InvalidOperationException("Server=db;Password=hunter2"), 500 },
        { new TargetInvocationException(new RefusedException()), 400 },
        { new AggregateException(new ForbiddenException("Admins only.", isAuthenticated: true)), 403 },
    };

    [Theory]
    [MemberData(nameof(RasksOwn))]
    public void Rasks_own_exceptions_answer_their_status_and_anything_else_is_a_500(Exception thrown, int expected)
    {
        var statuses = new ErrorStatuses();

        var status = statuses.Of(thrown);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void What_the_app_answers_wins_over_rasks_own_and_reaches_every_type_derived_from_it()
    {
        var options = new RaskServerOptions().Answer<RefusedException>(StatusCodes.Status422UnprocessableEntity);

        var derived = options.Errors.Of(new Closed());
        var itself = options.Errors.Of(new RefusedException());

        Assert.Equal(422, derived);
        Assert.Equal(422, itself);
    }

    [Fact]
    public void The_closest_type_the_app_answers_wins()
    {
        var options = new RaskServerOptions()
            .Answer<RefusedException>(StatusCodes.Status422UnprocessableEntity)
            .Answer<Closed>(StatusCodes.Status409Conflict);

        var status = options.Errors.Of(new Closed());

        Assert.Equal(409, status);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(600)]
    public void A_status_that_is_not_an_error_is_refused_where_it_is_answered(int status)
    {
        var options = new RaskServerOptions();

        var answer = () => options.Answer<TimeoutException>(status);

        Assert.Throws<ArgumentOutOfRangeException>(answer);
    }

    private sealed class Closed() : RefusedException("The invoice is already closed.");
}
