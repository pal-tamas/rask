using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     The conditions a <c>template.json</c> entry puts on a whole file.
/// </summary>
/// <remarks>
///     A negated flag is what lets a battery REPLACE a file rather than add one: under <c>--wasm</c> the
///     pages live in <c>Client/</c>, and the server's own copies must not be written at all.
/// </remarks>
public sealed class TemplateOwnerConditionTests
{
    private static readonly HashSet<string> WasmAndCqrs = new(StringComparer.Ordinal) { "wasm", "cqrs" };

    [Theory]
    [InlineData(new[] { "wasm" }, true)]
    [InlineData(new[] { "wasm", "cqrs" }, true)]
    [InlineData(new[] { "wasm", "data" }, false)]
    [InlineData(new[] { "!data" }, true)]
    [InlineData(new[] { "!wasm" }, false)]
    [InlineData(new[] { "cqrs", "!wasm" }, false)]
    [InlineData(new[] { "cqrs", "!data" }, true)]
    public void Every_condition_must_hold(string[] conditions, bool expected) =>
        Assert.Equal(expected, TemplateMaterializer.Satisfied(conditions, WasmAndCqrs));

    [Fact]
    public void No_conditions_always_hold() =>
        Assert.True(TemplateMaterializer.Satisfied([], WasmAndCqrs));
}
