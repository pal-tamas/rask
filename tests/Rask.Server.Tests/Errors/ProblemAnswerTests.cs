using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;
using Rask.Server.Tests.App;
using Rask.Wire;

namespace Rask.Server.Tests.Errors;

// Errors by default: a request that is not a page — a minimal endpoint here — answers a problem document when it
// throws, on a host wired by hand with AddRask and MapRask and on RaskApp. A fault says nothing of itself and
// carries the reference the error page would show for it; a refusal says its own sentence.
[Collection(RaskAppCollection.Name)]
public sealed class ProblemAnswerTests
{
    private const string Secret = "Server=db.internal;Password=hunter2";
    private const string Closed = "The invoice is already closed.";

    [Fact]
    public async Task A_fault_on_an_endpoint_is_a_500_problem_with_no_word_of_the_exception_and_the_error_pages_reference()
    {
        var thrown = new InvalidOperationException(Secret);
        await using var app = await HandWired(thrown);

        var answer = await Send(app, HttpMethod.Post, "/api/throws");

        Assert.Equal(HttpStatusCode.InternalServerError, answer.Status);
        Assert.Equal("application/problem+json", answer.MediaType);
        Assert.DoesNotContain("hunter2", answer.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", answer.Body, StringComparison.Ordinal);
        Assert.False(answer.Json.TryGetProperty("detail", out _));
        Assert.Equal(500, answer.Json.GetProperty("status").GetInt32());
        Assert.Equal(ErrorReference.Of(thrown), answer.Json.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task A_refusal_on_an_endpoint_is_a_400_problem_whose_detail_is_its_message()
    {
        await using var app = await HandWired(new RefusedException(Closed));

        var answer = await Send(app, HttpMethod.Post, "/api/throws");

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal(Closed, answer.Json.GetProperty("detail").GetString());
        Assert.False(answer.Json.TryGetProperty("reference", out _));
        Assert.False(answer.Json.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task A_refusal_that_names_its_fields_carries_them_in_errors()
    {
        await using var app = await HandWired(new FieldsRefused(new FieldFailure("That number is taken.", ["Year", "Number"])));

        var answer = await Send(app, HttpMethod.Post, "/api/throws");

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal("That number is taken.", answer.Json.GetProperty("detail").GetString());
        var errors = answer.Json.GetProperty("errors");
        Assert.Equal("That number is taken.", errors.GetProperty("Year")[0].GetString());
        Assert.Equal("That number is taken.", errors.GetProperty("Number")[0].GetString());
    }

    [Fact]
    public async Task An_exception_the_app_answers_takes_its_status_and_a_refusal_derived_from_it_keeps_its_message()
    {
        await using var app = await HandWired(
            new InvoiceClosed(), server => server.Answer<InvoiceClosed>(StatusCodes.Status409Conflict));

        var answer = await Send(app, HttpMethod.Post, "/api/throws");

        Assert.Equal(HttpStatusCode.Conflict, answer.Status);
        Assert.Equal(Closed, answer.Json.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_exception_the_app_answers_with_a_server_status_still_says_nothing_of_itself()
    {
        await using var app = await HandWired(
            new TimeoutException(Secret), server => server.Answer<TimeoutException>(StatusCodes.Status503ServiceUnavailable));

        var answer = await Send(app, HttpMethod.Get, "/api/throws");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.Status);
        Assert.DoesNotContain("hunter2", answer.Body, StringComparison.Ordinal);
        Assert.Matches("^[BCDFGHJKMNPQRSTWXZ2-9]{8}$", answer.Json.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task In_development_the_host_leaves_the_exception_to_the_developer_exception_page()
    {
        await using var app = await HandWired(new RefusedException(Closed), environment: "Development");

        var answer = await Send(app, HttpMethod.Post, "/api/throws");

        Assert.Equal(HttpStatusCode.InternalServerError, answer.Status);
        Assert.Contains(Closed, answer.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_RaskApp_an_endpoints_refusal_is_a_400_problem_too()
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"))
            .MapEndpoints(e => e.MapPost("/api/throws", () => Throw(new RefusedException(Closed))))
            .Build<MinimalApp>();

        var answer = await Kestrel(app, HttpMethod.Post, accept: "application/json");

        Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
        Assert.Equal(Closed, answer.Json.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task On_RaskApp_a_browsers_navigation_that_throws_is_still_answered_by_the_error_page()
    {
        var app = RaskApp.Create([], b => b.WebHost.UseSetting("urls", "http://127.0.0.1:0"))
            .MapEndpoints(e => e.MapGet("/api/throws", () => Throw(new InvalidOperationException(Secret))))
            .Build<MinimalApp>();

        var answer = await Kestrel(app, HttpMethod.Get, accept: "text/html,application/xhtml+xml,*/*;q=0.8");

        Assert.Equal("text/html", answer.MediaType);
        Assert.DoesNotContain("hunter2", answer.Body, StringComparison.Ordinal);
    }

    private static IResult Throw(Exception thrown) => throw thrown;

    // A host wired by hand, the way an app adopting Rask has one: AddRask, an endpoint of its own, MapRask.
    private static async Task<WebApplication> HandWired(
        Exception thrown, Action<RaskServerOptions>? configureServer = null, string environment = "Production")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Services.AddRask(configureServer: configureServer);
        var app = builder.Build();
        app.MapPost("/api/throws", () => Throw(thrown));
        app.MapGet("/api/throws", () => Throw(thrown));
        app.MapRask<MinimalApp>();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<Answer> Send(WebApplication app, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.ParseAdd("application/json");
        if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using var response = await app.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);
        return await Answer.Read(response);
    }

    private static async Task<Answer> Kestrel(WebApplication app, HttpMethod method, string accept)
    {
        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            using var request = new HttpRequestMessage(method, "/api/throws");
            request.Headers.Accept.ParseAdd(accept);
            if (method == HttpMethod.Post)
            {
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            return await Answer.Read(response);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    private sealed record Answer(HttpStatusCode Status, string? MediaType, string Body, JsonElement Json)
    {
        internal static async Task<Answer> Read(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var json = mediaType is "application/problem+json" or "application/json"
                ? JsonDocument.Parse(body).RootElement.Clone()
                : default;
            return new Answer(response.StatusCode, mediaType, body, json);
        }
    }

    private sealed class InvoiceClosed() : RefusedException(Closed);

    private sealed class FieldsRefused(params FieldFailure[] failures) : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures => failures;
    }
}
