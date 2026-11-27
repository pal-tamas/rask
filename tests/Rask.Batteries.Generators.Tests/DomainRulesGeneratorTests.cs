namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// What a save asks of an aggregate, as the generator registers it: the attributes on its properties, its
/// <c>Validate()</c> and <c>ValidateDelete()</c> found by convention, and nothing for an entity that declares none.
/// </summary>
public class DomainRulesGeneratorTests
{
    [Fact]
    public void A_private_Validate_and_an_async_ValidateDelete_are_reached_through_accessors_that_compile()
    {
        var run = Run("""
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using Rask.Data;
            using Rask.Values;

            namespace Shop
            {
                public sealed class Order : Aggregate<System.Guid>, IDeletable
                {
                    [Required, StringLength(200)] public string Title { get; private set; } = "";

                    private IEnumerable<Problem> Validate() { yield break; }

                    private async IAsyncEnumerable<Problem> ValidateDelete() { await System.Threading.Tasks.Task.Yield(); yield break; }
                }
            }
            """);

        var generated = run.GeneratedSource("__RaskDomainRules");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("new global::Rask.Data.CheckedProperty(\"Title\", static () => \"Title\", null, new global::System.ComponentModel.DataAnnotations.RequiredAttribute(), new global::System.ComponentModel.DataAnnotations.StringLengthAttribute(200))", generated, StringComparison.Ordinal);
        Assert.Contains("UnsafeAccessorKind.Method, Name = \"Validate\")]", generated, StringComparison.Ordinal);
        Assert.Contains("global::System.Linq.AsyncEnumerable.ToAsyncEnumerable(Validate_0(entity))", generated, StringComparison.Ordinal);
        Assert.Contains("static (global::Shop.Order entity) => ValidateDelete_0(entity)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Validate_that_yields_no_problems_is_not_the_convention()
    {
        var run = Run("""
            using System.Collections.Generic;
            using Rask.Data;
            using Rask.Values;

            namespace Shop
            {
                public sealed class Order : Aggregate<System.Guid>
                {
                    public string Title { get; private set; } = "";

                    public bool Validate() => true;
                }
            }
            """);

        var generated = run.RunResult.Results.SelectMany(static r => r.GeneratedSources);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain(generated, static s => s.HintName.Contains("__RaskDomainRules", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_object_of_one_value_is_checked_by_the_value_it_holds()
    {
        var run = Run("""
            using System.ComponentModel.DataAnnotations;
            using Rask.Data;
            using Rask.Values;

            namespace Shop
            {
                public sealed record Code(string Value);

                public sealed class Order : Aggregate<System.Guid>
                {
                    [MaxLength(5)] public Code Code { get; private set; } = new("A");
                }
            }
            """);

        var generated = run.GeneratedSource("__RaskDomainRules");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("static held => held is global::Shop.Code value ? (object?)value.Value : null", generated, StringComparison.Ordinal);
    }

    private static GeneratorRun Run(string source) =>
        GeneratorHarness.Run(
            source,
            [new DomainRulesGenerator()],
            "Rask.Data", "Rask.Wire", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");
}
