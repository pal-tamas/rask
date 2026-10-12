#pragma warning disable RASK014 // DefaultErrorPage is [SkipFactory]; tests construct it directly

using System.Text.RegularExpressions;
using Rask.Core.Diagnostics;

namespace Rask.Core.Tests.Components;

// What a visitor is shown of an exception outside Development: nothing. Its type, its message and its
// causes go to the Error log with a reference, and the page carries only that reference.
[Collection("ConsoleRedirect")]
public partial class ProductionErrorPageTests
{
    private const string Secret = "Password=hunter2";

    [Fact]
    public void A_production_page_shows_no_type_and_no_message_of_the_exception_or_its_cause()
    {
        var thrown = Leaking();

        var html = new DefaultErrorPage(thrown, isDevelopment: false).ToHtml();

        Assert.DoesNotContain("hunter2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", html, StringComparison.Ordinal);
        Assert.DoesNotContain("db.internal", html, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(Leaking), html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_production_page_says_the_catalog_sentence_and_offers_both_ways_on()
    {
        var recovered = false;

        var html = new DefaultErrorPage(Leaking(), isDevelopment: false, () => recovered = true).ToHtml();

        Assert.Contains("Something went wrong", html, StringComparison.Ordinal);
        Assert.Contains("The problem has been recorded. If it keeps happening, quote this reference: ", html, StringComparison.Ordinal);
        Assert.Contains("Try again", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-reload", html, StringComparison.Ordinal);
        Assert.False(recovered);
    }

    [Fact]
    public void The_reference_on_the_page_is_in_the_error_log_line_beside_the_exception()
    {
        var thrown = Leaking();

        var (html, logged) = Reported(thrown, () => new DefaultErrorPage(thrown, isDevelopment: false).ToHtml());

        var reference = Reference().Match(html).Groups[1].Value;
        var line = Assert.Single(logged);
        Assert.Equal(8, reference.Length);
        Assert.Equal(RaskLogLevel.Error, line.Level);
        Assert.Equal("Rask.Errors", line.Category);
        Assert.EndsWith($"Reference {reference}", line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", line.Message, StringComparison.Ordinal);
        Assert.Same(thrown, line.Exception);
    }

    [Fact]
    public void An_exception_drawn_twice_keeps_its_reference_and_is_logged_once()
    {
        var thrown = Leaking();

        var (html, logged) = Reported(
            thrown,
            () => new DefaultErrorPage(thrown, isDevelopment: false).ToHtml() + new DefaultErrorPage(thrown, isDevelopment: true).ToHtml());

        var references = Reference().Matches(html).Select(match => match.Groups[1].Value).ToList();
        Assert.Equal(2, references.Count);
        Assert.Equal(references[0], references[1]);
        Assert.Single(logged);
    }

    [Fact]
    public void Two_exceptions_with_one_message_get_two_references()
    {
        var first = ErrorReference.Of(new InvalidOperationException(Secret));

        var second = ErrorReference.Of(new InvalidOperationException(Secret));

        Assert.NotEqual(first, second);
        Assert.Matches("^[BCDFGHJKMNPQRSTWXZ2-9]{8}$", first);
    }

    [Fact]
    public void A_boundary_logs_what_it_caught_whatever_its_fallback_shows()
    {
        var thrown = Leaking();
        var boundary = new ErrorBoundary();
        boundary.SetProps([], (_, _) => new Fragment([]));

        var (html, logged) = Reported(thrown, () =>
        {
            boundary.TripInRender(thrown);
            return boundary.ToHtml();
        });

        var line = Assert.Single(logged);
        Assert.Equal(RaskLogLevel.Error, line.Level);
        Assert.Contains("an error boundary from a render. Reference ", line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_development_page_shows_the_type_the_message_the_cause_and_the_stack_as_before()
    {
        var html = new DefaultErrorPage(Leaking(), isDevelopment: true).ToHtml();

        Assert.Contains("System.InvalidOperationException", html, StringComparison.Ordinal);
        Assert.Contains("hunter2", html, StringComparison.Ordinal);
        Assert.Contains("Caused by: System.TimeoutException", html, StringComparison.Ordinal);
        Assert.Contains(nameof(Leaking), html, StringComparison.Ordinal);
        Assert.Matches(Reference(), html);
    }

    [Fact]
    public void A_host_that_never_said_which_environment_it_runs_in_is_not_in_development()
    {
        var unknown = DefaultErrorPage.ResolveIsDevelopment(hostAnswer: null, aspnetEnv: null, dotnetEnv: null);
        var misspelt = DefaultErrorPage.ResolveIsDevelopment(hostAnswer: null, aspnetEnv: "Dev", dotnetEnv: null);

        var html = new DefaultErrorPage(Leaking(), unknown).ToHtml();

        Assert.False(unknown);
        Assert.False(misspelt);
        Assert.DoesNotContain("hunter2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", html, StringComparison.Ordinal);
    }

    // Thrown, so that it has a stack, with the secret in its own message and in its cause's.
    private static Exception Leaking()
    {
        try
        {
            throw new InvalidOperationException(
                $"Login failed for 'sa' with {Secret}", new TimeoutException($"db.internal:1433 {Secret}"));
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    // What was logged for `thrown` while `act` ran. The sink is the process's, so another test's lines pass
    // through it too: only this exception's are kept.
    private static (string Html, List<RaskDiagnosticEvent> Logged) Reported(Exception thrown, Func<string> act)
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = line =>
        {
            lock (captured)
            {
                captured.Add(line);
            }
        };
        try
        {
            var html = act();
            lock (captured)
            {
                return (html, captured.Where(line => ReferenceEquals(line.Exception, thrown)).ToList());
            }
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }

    [GeneratedRegex("quote this reference: ([A-Z0-9]+)<", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Reference();
}
