using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

/// <summary>
///     Every diagnostic here announces a failure the developer cannot yet see: a build that will not
///     compile, or — worse, because you can miss a Warning — a registration that is silently skipped so the
///     thing throws in production. #275 gave the route diagnostics a fix clause and #608 finished the job;
///     these pin it, because "the message stopped saying what to do" is a regression no other test notices.
/// </summary>
/// <remarks>
///     Each case asserts the em-dash separator (the house shape: problem — remedy) plus a substring of the
///     remedy itself. Asserting only the dash would pass on an empty clause; asserting the whole sentence
///     would fail on any rewording. The substring is the imperative verb, which is the part that has to
///     survive.
/// </remarks>
public class ActionableDiagnosticMessageTests
{
    [Theory]
    // Each reason TryParseTemplate can produce, and the correct template it should show you.
    [InlineData("/users//{id}", "empty segment", "/users/{id:int}")]
    [InlineData("/files/{**rest}", "catch-all", "/files/{folder}/{name}")]
    [InlineData("/users/{}", "no name", "/users/{id}")]
    [InlineData("/order-{id}", "mixed literal/param", "its own segment")]
    public void Rask003_ShowsACorrectTemplate(string template, string problem, string remedy)
    {
        var message = MessageFor("RASK003", $$"""
                                              using Rask.Core;
                                              using Rask.Core.Routing;
                                              namespace Demo;
                                              [Route("{{template}}")]
                                              public sealed class P : Component
                                              {
                                                  public string? Id { get; set; }
                                                  public override Component? Render() => this;
                                              }
                                              """);

        Assert.Contains(problem, message, StringComparison.Ordinal);
        Assert.Contains(" — ", message, StringComparison.Ordinal);
        Assert.Contains(remedy, message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rask011_NamesTheWayOut_NotJustTheConstraint()
    {
        var message = MessageFor("RASK011", """
                                            using Rask.Core;
                                            using Rask.Core.Routing;
                                            namespace Demo;
                                            public sealed class NotParsable { }
                                            [Route("/thing")]
                                            public sealed class P : Component
                                            {
                                                [QueryParam] public NotParsable? Value { get; set; }
                                                public override Component? Render() => this;
                                            }
                                            """);

        Assert.Contains(" — ", message, StringComparison.Ordinal);
        // Both escapes, because which one applies depends on whether the type is yours to change.
        Assert.Contains("use a parsable type", message, StringComparison.Ordinal);
        Assert.Contains("convert inside the page", message, StringComparison.Ordinal);
    }

    // The opt-out is the point: an orphan stylesheet is very often a deliberate global one, and until now
    // the only way to learn that the escape hatch existed was to read docs/diagnostics.md.
    [Fact]
    public void Rask015_MentionsTheAutoIncludeOptOut()
    {
        var run = GeneratorDriverFixture.Run(
            [("/proj/Widgets/Unrelated.cs", "namespace Demo; public sealed class Unrelated { }")],
            new ComponentScopedCssGenerator(),
            [("/proj/Widgets/Orphan.css", ".a{color:red}")]);

        var message = run.Diagnostics.First(d => d.Id == "RASK015").GetMessage();
        Assert.Contains(" — ", message, StringComparison.Ordinal);
        Assert.Contains("RaskScopedCssAutoInclude", message, StringComparison.Ordinal);
    }

    /// <inheritdoc cref="Rask015_MentionsTheAutoIncludeOptOut" />
    [Fact]
    public void Rask017_MentionsTheAutoIncludeOptOut()
    {
        var run = GeneratorDriverFixture.RunScoped(
            [("/proj/Widgets/Unrelated.cs", "namespace Demo; public sealed class Unrelated { }")],
            [("/proj/Widgets/Orphan.ts", "export function go() {}")]);

        var message = run.Diagnostics.First(d => d.Id == "RASK017").GetMessage();
        Assert.Contains(" — ", message, StringComparison.Ordinal);
        Assert.Contains("RaskScopedTsAutoInclude", message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     RASK055 names the file, the component, and the exact rename that fixes it.
    /// </summary>
    /// <remarks>
    ///     The remedy is the whole point of this one. "Scoped JavaScript is no longer supported"
    ///     leaves someone with a working file and no idea what to do with it, and the honest answer
    ///     is unusually cheap — TypeScript is a superset, so the rename IS the migration. A message
    ///     that did not say so would send people looking for a conversion tool.
    /// </remarks>
    [Fact]
    public void Rask054_NamesTheRenameThatFixesIt()
    {
        var run = GeneratorDriverFixture.RunScoped(
            [("/proj/Widgets/Counter.cs",
                """
                namespace Demo;
                public sealed class Counter : Rask.Core.Component
                {
                    protected override Rask.Core.Component? Render() => this;
                }
                """)],
            [],
            strayJs: ["/proj/Widgets/Counter.js"]);

        var message = run.Diagnostics.First(d => d.Id == "RASK055").GetMessage();
        Assert.Contains(" — ", message, StringComparison.Ordinal);
        Assert.Contains("Counter.js", message, StringComparison.Ordinal);
        Assert.Contains("'Counter.ts'", message, StringComparison.Ordinal);
    }

    private static string MessageFor(string id, string source)
    {
        var run = GeneratorDriverFixture.RunRoutes(source);
        var diagnostic = run.Diagnostics.FirstOrDefault(d => d.Id == id);
        Assert.True(diagnostic is not null,
            $"expected {id}, got: {string.Join(", ", run.Diagnostics.Select(d => d.Id).Distinct())}");
        return diagnostic!.GetMessage();
    }
}
