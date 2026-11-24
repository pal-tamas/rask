using Rask.Generators.Validation;

namespace Rask.Generators.Tests;

/// <summary>
/// A type's own <c>static void Rules(InlineValidator&lt;T&gt; rules)</c>, registered by the validator registry beside the
/// <c>AbstractValidator&lt;T&gt;</c> classes it already finds. FluentValidation and the Rask integration are stubbed
/// by name, as <see cref="GeneratedModelReferenceTests" /> stubs them.
/// </summary>
public class InlineRulesGeneratorTests
{
    private const string Stubs = """
        namespace FluentValidation
        {
            public abstract class AbstractValidator<T> { }
            public class InlineValidator<T> : AbstractValidator<T> { }
        }
        namespace Rask { public static class RaskValidators { } }
        namespace Rask.Values
        {
            public readonly record struct Problem(string? Field, string Message);
            public abstract record Value { protected virtual System.Collections.Generic.IEnumerable<Problem> Rules() => []; }
        }
        """;

    [Fact]
    public void A_private_Rules_method_is_registered_through_an_accessor()
    {
        var run = Run("""
            namespace Shop
            {
                public sealed class Supplier
                {
                    static void Rules(FluentValidation.InlineValidator<Supplier> rules) { }
                }
            }
            """);

        var registry = run.GeneratedSource("__RaskValidatorRegistry");

        Assert.Contains("global::Rask.RaskValidators.Inline<global::Shop.Supplier>(static __rules => Rules_0(null, __rules)),", registry, StringComparison.Ordinal);
        Assert.Contains("UnsafeAccessorKind.StaticMethod, Name = \"Rules\"", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void An_internal_Rules_method_is_called_by_name()
    {
        var run = Run("""
            namespace Shop
            {
                public sealed class Supplier
                {
                    internal static void Rules(FluentValidation.InlineValidator<Supplier> rules) { }
                }
            }
            """);

        var registry = run.GeneratedSource("__RaskValidatorRegistry");

        Assert.Contains("Inline<global::Shop.Supplier>(static __rules => global::Shop.Supplier.Rules(__rules)),", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("UnsafeAccessor", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_and_a_validator_class_of_one_type_are_both_registered()
    {
        var run = Run("""
            namespace Shop
            {
                public sealed class Supplier
                {
                    internal static void Rules(FluentValidation.InlineValidator<Supplier> rules) { }
                }

                public sealed class SupplierValidator : FluentValidation.AbstractValidator<Supplier> { }
            }
            """);

        var registry = run.GeneratedSource("__RaskValidatorRegistry");

        Assert.Contains("Inline<global::Shop.Supplier>", registry, StringComparison.Ordinal);
        Assert.Contains("Entry<global::Shop.Supplier>(static __sp => new global::Shop.SupplierValidator()),", registry, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Diagnostics, static d => d.Id == "RASKVAL001");
    }

    [Fact]
    public void A_Rules_method_of_another_shape_is_not_registered()
    {
        var run = Run("""
            namespace Shop
            {
                public sealed class Supplier
                {
                    void Rules(FluentValidation.InlineValidator<Supplier> rules) { }
                }

                public sealed class Other
                {
                    static void Rules(FluentValidation.InlineValidator<Supplier> rules) { }
                }
            }
            """);

        var found = run.HasGeneratedSource("__RaskValidatorRegistry");

        Assert.False(found);
    }

    [Fact]
    public void A_value_objects_Rules_override_its_Rules_to_ask_them()
    {
        var run = Run("""
            namespace Shop
            {
                public sealed partial record Opening(System.DateOnly? Date, decimal? Quantity) : Rask.Values.Value
                {
                    static void Rules(FluentValidation.InlineValidator<Opening> rules) { }
                }
            }
            """);

        var half = run.GeneratedSource("Opening.RaskRules");

        Assert.Contains("namespace Shop;", half, StringComparison.Ordinal);
        Assert.Contains("partial record Opening", half, StringComparison.Ordinal);
        Assert.Contains("global::Rask.RaskValidators.Problems(this);", half, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_object_with_its_own_Rules_override_gets_no_second_one()
    {
        var run = Run("""
            namespace Shop
            {
                public sealed partial record Opening(decimal? Quantity) : Rask.Values.Value
                {
                    static void Rules(FluentValidation.InlineValidator<Opening> rules) { }

                    protected override System.Collections.Generic.IEnumerable<Rask.Values.Problem> Rules() => [];
                }
            }
            """);

        var emitted = run.HasGeneratedSource("Opening.RaskRules");

        Assert.False(emitted);
    }

    private static GeneratorRun Run(string source) =>
        GeneratorDriverFixture.Run([("/proj/Stubs.cs", Stubs), ("/proj/Shop.cs", source)], [new ValidatorRegistryGenerator()]);
}
