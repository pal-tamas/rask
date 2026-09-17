using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     The conditions a <c>template.json</c> entry puts on a whole file.
/// </summary>
/// <remarks>
///     A negated flag is what lets a battery REPLACE a file rather than add one: with no accounts to sign
///     into, the home page's nav links to the docs instead, and the copy that links to a sign-in page must
///     not be written at all.
/// </remarks>
public sealed class TemplateOwnerConditionTests
{
    // Two flags that are on, so every case below reads as "against this set". They are real flag names on
    // purpose: a condition naming something `rask new` does not understand can never hold, which would
    // make a passing assertion here say nothing.
    private static readonly HashSet<string> PwaAndCqrs = new(StringComparer.Ordinal) { "pwa", "cqrs" };

    [Theory]
    [InlineData(new[] { "pwa" }, true)]
    [InlineData(new[] { "pwa", "cqrs" }, true)]
    [InlineData(new[] { "pwa", "data" }, false)]
    [InlineData(new[] { "!data" }, true)]
    [InlineData(new[] { "!pwa" }, false)]
    [InlineData(new[] { "cqrs", "!pwa" }, false)]
    [InlineData(new[] { "cqrs", "!data" }, true)]
    public void Every_condition_must_hold(string[] conditions, bool expected) =>
        Assert.Equal(expected, TemplateMaterializer.Satisfied(conditions, PwaAndCqrs));

    [Fact]
    public void No_conditions_always_hold() =>
        Assert.True(TemplateMaterializer.Satisfied([], PwaAndCqrs));
}
