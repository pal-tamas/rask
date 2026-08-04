namespace Rask.Cqrs.Generators.Tests;

/// <summary>
/// Runs <see cref="CqrsDispatchGenerator"/> over a source string. The driver plumbing lives in the shared
/// <see cref="GeneratorHarness"/>, linked into every <c>*.Generators.Tests</c> project.
/// </summary>
internal static class CqrsGeneratorFixture
{
    // The source under test implements Rask.Cqrs interfaces, and the generated code calls into
    // Rask.Cqrs + Microsoft.Extensions.DependencyInjection — pull both in so GeneratedCompileErrors
    // can validate that the emitted code actually compiles.
    public static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source,
            new CqrsDispatchGenerator(),
            "Rask.Cqrs",
            "Microsoft.Extensions.DependencyInjection.Abstractions");
}
