using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Rask.Cqrs.Server.Tests;

// IncludeExceptionDetail is a Development-only switch. Whatever the option says, a host outside
// Development answers a handler's exception with the generic problem and a reference, and the exception
// itself goes to the log under that reference. The handler's message carries a password on purpose.
public sealed class ExceptionDetailTests
{
    private const string Explodes = "Rask.Cqrs.Server.Tests.Explodes";

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData(null)]
    public async Task Outside_development_the_500_says_nothing_of_the_exception_with_the_option_on(string? environment)
    {
        var problem = await Fail(environment, includeDetail: true);

        Assert.Equal(HttpStatusCode.InternalServerError, problem.Status);
        Assert.DoesNotContain("hunter2", problem.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", problem.Body, StringComparison.Ordinal);
        Assert.False(problem.Json.TryGetProperty("detail", out _));
        Assert.Equal("Handler failed", problem.Json.GetProperty("title").GetString());
    }

    [Fact]
    public async Task In_production_with_the_option_off_the_500_is_the_same_generic_problem()
    {
        var problem = await Fail("Production", includeDetail: false);

        Assert.DoesNotContain("hunter2", problem.Body, StringComparison.Ordinal);
        Assert.False(problem.Json.TryGetProperty("detail", out _));
        Assert.Matches("^[A-Z0-9]{8}$", problem.Json.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task In_development_with_the_option_on_the_500_carries_the_exception_and_the_reference()
    {
        var problem = await Fail("Development", includeDetail: true);

        var detail = problem.Json.GetProperty("detail").GetString();
        Assert.Contains("System.InvalidOperationException: connection string is Server=db;Password=hunter2", detail, StringComparison.Ordinal);
        Assert.Matches("^[A-Z0-9]{8}$", problem.Json.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task In_development_with_the_option_off_the_500_says_nothing_of_the_exception()
    {
        var problem = await Fail("Development", includeDetail: false);

        Assert.DoesNotContain("hunter2", problem.Body, StringComparison.Ordinal);
        Assert.False(problem.Json.TryGetProperty("detail", out _));
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    public async Task The_exception_is_logged_once_at_error_under_the_reference_the_500_carries(string environment, bool includeDetail)
    {
        var problem = await Fail(environment, includeDetail);

        var line = Assert.Single(problem.Logged, static line => line.Level == LogLevel.Error);
        Assert.Equal("Rask.Errors", line.Category);
        Assert.Contains("hunter2", Assert.IsType<InvalidOperationException>(line.Exception).Message, StringComparison.Ordinal);
        Assert.EndsWith($"Reference {problem.Json.GetProperty("reference").GetString()}", line.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData(null)]
    public async Task Setting_the_option_outside_development_is_warned_about_once_as_the_endpoints_are_mapped(string? environment)
    {
        var logs = new Recorded();
        using var host = EndpointSecurityTests.Host(static o => o.IncludeExceptionDetail = true, environment, logs);
        using var client = host.CreateClient();

        using var first = await client.SendAsync(Request(), TestContext.Current.CancellationToken);
        using var second = await client.SendAsync(Request(), TestContext.Current.CancellationToken);

        var warning = Assert.Single(logs.Lines, static line => line.Level == LogLevel.Warning);
        Assert.Contains("Rask:Cqrs:Server:IncludeExceptionDetail is set and is ignored", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public async Task No_warning_is_logged_in_development_or_with_the_option_off(string environment, bool includeDetail)
    {
        var problem = await Fail(environment, includeDetail);

        Assert.DoesNotContain(problem.Logged, static line => line.Level == LogLevel.Warning);
    }

    private static HttpRequestMessage Request()
    {
        var request = EndpointSecurityTests.Request(HttpMethod.Get, Explodes, authenticated: true);
        request.RequestUri = new Uri(EndpointSecurityTests.Url(Explodes, "{}"), UriKind.Relative);
        return request;
    }

    // The answer to a query whose handler throws, and what the host logged about it.
    private static async Task<Problem> Fail(string? environment, bool includeDetail)
    {
        var logs = new Recorded();
        using var host = EndpointSecurityTests.Host(o => o.IncludeExceptionDetail = includeDetail, environment, logs);
        using var client = host.CreateClient();
        using var request = Request();

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        return new Problem(response.StatusCode, body, json.RootElement.Clone(), [.. logs.Lines]);
    }

    private sealed record Problem(HttpStatusCode Status, string Body, JsonElement Json, IReadOnlyList<Line> Logged);

    private sealed record Line(string Category, LogLevel Level, string Message, Exception? Exception);

    // Everything Rask's own categories logged at Warning or above.
    private sealed class Recorded : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Line> _lines = new();

        public IReadOnlyCollection<Line> Lines => _lines;

        public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, _lines);

        public void Dispose()
        {
        }

        private sealed class Recorder(string category, System.Collections.Concurrent.ConcurrentQueue<Line> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning && category.StartsWith("Rask.", StringComparison.Ordinal))
                {
                    lines.Enqueue(new Line(category, logLevel, formatter(state, exception), exception));
                }
            }
        }
    }
}
