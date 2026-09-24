using Rask.Core.Components;
using Rask.Core.Live;

#pragma warning disable RASK014 // DefaultErrorPage is [SkipFactory]; tests construct it directly

namespace Rask.Core.Tests.Components;

// The framework's default error page shows a rich, developer-friendly view in Development (parsed stack
// frames, source excerpts, the full inner-exception chain) and a deliberately minimal page in Production.
// The production behaviour is security-critical: no stack, no source, no file paths, no inner exceptions
// may leak. These pin both branches, plus the source-excerpt reader and HTML-encoding safety.
public class DefaultErrorPageTests
{
    // A genuinely-thrown exception, so StackTrace(ex, true) has captured frames.
    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static string Render(Exception ex, bool isDevelopment) =>
        new DefaultErrorPage(ex, isDevelopment).ToHtml();

    // #605: the parameterless ctor — the one RootErrorBoundary actually uses in production — decided
    // Development by reading ASPNETCORE_ENVIRONMENT / DOTNET_ENVIRONMENT and nothing else. Every other
    // way of selecting it (dotnet run --environment, appsettings.json, assigning EnvironmentName, an IDE
    // profile that sets configuration rather than the process environment) therefore produced the
    // production page while developing: no stack trace, no source excerpt, and no hint why.
    //
    // Driven as a pure function rather than through LiveOptions.IsDevelopment. That flag is
    // process-global and read by every render reaching RootErrorBoundary, so a test that flipped it
    // could change what a concurrently-running test's error page contains — which is the class of
    // flake this branch is otherwise busy removing.

    [Fact]
    public void The_host_answer_selects_development_with_no_environment_variable_set()
    {
        Assert.True(DefaultErrorPage.ResolveIsDevelopment(true, aspnetEnv: null, dotnetEnv: null));
    }

    [Fact]
    public void The_host_answer_wins_over_the_environment_variables()
    {
        // The variables are a fallback, not a vote. A host reporting Production is not overridden by a
        // stale variable left in someone's shell, and a host reporting Development does not need one.
        Assert.False(DefaultErrorPage.ResolveIsDevelopment(false, "Development", "Development"));
        Assert.True(DefaultErrorPage.ResolveIsDevelopment(true, "Production", "Production"));
    }

    [Fact]
    public void With_no_host_answer_the_environment_variables_still_decide()
    {
        // Unchanged behaviour for a standalone host, or a component rendered outside one.
        Assert.True(DefaultErrorPage.ResolveIsDevelopment(null, "Development", null));
        Assert.True(DefaultErrorPage.ResolveIsDevelopment(null, null, "Development"));
        Assert.True(DefaultErrorPage.ResolveIsDevelopment(null, "development", null));  // case-insensitive
        Assert.False(DefaultErrorPage.ResolveIsDevelopment(null, "Production", null));
        Assert.False(DefaultErrorPage.ResolveIsDevelopment(null, null, null));
    }

    [Fact]
    public void The_ASPNETCORE_ENVIRONMENT_variable_wins_over_the_DOTNET_one()
    {
        Assert.False(DefaultErrorPage.ResolveIsDevelopment(null, "Production", "Development"));
    }

    [Fact]
    public void Always_shows_heading_type_and_message()
    {
        var html = Render(Thrown("boom-msg"), isDevelopment: false);

        Assert.Contains("Something went wrong", html);
        Assert.Contains("System.InvalidOperationException", html);
        Assert.Contains("boom-msg", html);
    }

    [Fact]
    public void Always_offers_a_reload_recovery_button()
    {
        // A user stranded on the fault needs an in-app way back — the runtime wires data-rask-reload to
        // location.reload(). Present in production too (the primary recovery when no stack is shown).
        var html = Render(Thrown("boom"), isDevelopment: false);
        Assert.Contains("data-rask-reload", html);
        Assert.Contains("Reload this page", html);
        Assert.Contains("<button", html);
    }

    [Fact]
    public void Development_renders_parsed_stack_frames()
    {
        var html = Render(Thrown("dev-boom"), isDevelopment: true);

        // The throwing frame's method appears (method name is PDB-independent, unlike file/line).
        Assert.Contains(".Thrown", html);
        Assert.Contains("at ", html);
    }

    [Fact]
    public void Production_leaks_no_stack_or_file_paths()
    {
        var html = Render(Thrown("prod-boom"), isDevelopment: false);

        // The stack is never parsed in production: no frame lines, no method names, no source paths.
        Assert.DoesNotContain(".Thrown", html);
        Assert.DoesNotContain("at ", html);
        Assert.DoesNotContain(".cs:line", html);
        Assert.DoesNotContain(".cs | ", html); // no source-excerpt gutter
    }

    [Fact]
    public void Development_renders_inner_exception_chain()
    {
        var ex = new InvalidOperationException("outer-boom", new Exception("inner-secret"));

        var html = Render(ex, isDevelopment: true);

        Assert.Contains("outer-boom", html);
        Assert.Contains("Caused by:", html);
        Assert.Contains("inner-secret", html);
    }

    [Fact]
    public void Production_hides_inner_exception_chain()
    {
        var ex = new InvalidOperationException("outer-boom", new Exception("inner-secret"));

        var html = Render(ex, isDevelopment: false);

        Assert.Contains("outer-boom", html);            // outermost message still shown
        Assert.DoesNotContain("Caused by:", html);      // but no chain
        Assert.DoesNotContain("inner-secret", html);    // and no inner detail leaks
    }

    [Fact]
    public void Exception_message_is_html_encoded_no_injection()
    {
        var html = Render(new Exception("<script>alert(1)</script>"), isDevelopment: true);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Unwind_flattens_aggregate_exception()
    {
        var agg = new AggregateException(new Exception("agg-a"), new Exception("agg-b"));

        var chain = DefaultErrorPage.Unwind(agg);

        Assert.Contains(chain, e => e.Message == "agg-a");
        Assert.Contains(chain, e => e.Message == "agg-b");
    }

    [Fact]
    public void Unwind_is_depth_bounded_on_deeply_nested_chain()
    {
        // A pathologically deep chain must not blow the stack or render thousands of blocks.
        Exception ex = new("leaf");
        for (var i = 0; i < 50; i++)
        {
            ex = new Exception($"level-{i}", ex);
        }

        var chain = DefaultErrorPage.Unwind(ex);

        Assert.True(chain.Count <= 21); // MaxChainDepth (20) + the root
    }

    [Fact]
    public void Read_source_excerpt_returns_window_with_throwing_line_marked()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, ["one", "two", "three", "four", "five"]);

            var excerpt = DefaultErrorPage.ReadSourceExcerpt(path, line: 3, radius: 1)!;

            Assert.Contains("→", excerpt);          // the throwing line is marked
            Assert.Contains("three", excerpt);      // and included
            Assert.Contains("two", excerpt);        // ±radius neighbours included
            Assert.Contains("four", excerpt);
            Assert.DoesNotContain("one", excerpt);  // outside the radius window
            Assert.DoesNotContain("five", excerpt);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("/no/such/file/anywhere.cs", 3)]
    [InlineData("", 3)]
    [InlineData(null, 3)]
    public void Read_source_excerpt_missing_or_invalid_file_returns_null(string? file, int line)
    {
        Assert.Null(DefaultErrorPage.ReadSourceExcerpt(file, line, radius: 5));
    }

    [Fact]
    public void Read_source_excerpt_line_out_of_range_returns_null()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path, ["only", "two", "lines"]);
            Assert.Null(DefaultErrorPage.ReadSourceExcerpt(path, line: 99, radius: 5));
            Assert.Null(DefaultErrorPage.ReadSourceExcerpt(path, line: 0, radius: 5));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
