using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Validation;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK139 (a <c>Rules</c> taking an <c>InlineValidator</c> that is not the in-type convention) and RASK183 (a value
///     object with both a hand-written <c>Rules()</c> override and the in-type FluentValidation <c>Rules</c>).
/// </summary>
public class InlineRulesAnalyzerTests
{
    [Theory]
    [InlineData("public sealed class Supplier { public string Name { get; set; } = \"\"; void Rules(InlineValidator<Supplier> rules) { } }", "it is not static")]
    [InlineData("public sealed class Supplier { static bool Rules(InlineValidator<Supplier> rules) => true; }", "it returns 'bool'")]
    [InlineData("public sealed class Supplier { static void Rules(InlineValidator<Supplier> rules, int limit) { } }", "it takes other parameters")]
    [InlineData("public sealed class Supplier { static void Rules<T>(InlineValidator<Supplier> rules) { } }", "it is generic")]
    [InlineData("public sealed class Order { } public sealed class Supplier { static void Rules(InlineValidator<Order> rules) { } }", "it validates 'Order', not 'Supplier'")]
    [InlineData("public sealed class Supplier<T> { static void Rules(InlineValidator<Supplier<T>> rules) { } }", "'Supplier' is generic, so the registration cannot name it")]
    public async Task A_Rules_taking_an_InlineValidator_in_another_shape_is_RASK139(string declaration, string why)
    {
        var source = "using FluentValidation;\nnamespace Shop;\n" + declaration;

        var diagnostics = await RunAsync(new InlineRulesShapeAnalyzer(), source, withFluentValidation: true);

        var d = Assert.Single(diagnostics);
        Assert.Equal("RASK139", d.Id);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("'Supplier.Rules' is never registered — " + why, d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("'static void Rules(InlineValidator<Supplier> rules)'", d.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public sealed class Supplier { static void Rules(InlineValidator<Supplier> rules) { } }")]
    [InlineData("public sealed class Supplier { private static void Rules(InlineValidator<Supplier> rules) => rules.RuleFor(s => s.ToString()).NotEmpty(); }")]
    [InlineData("public sealed class Supplier { public static void Rules(InlineValidator<Supplier> rules) { } }")]
    [InlineData("public sealed class Supplier { void Rules(int limit) { } void Check(InlineValidator<Supplier> rules) { } }")]
    public async Task The_convention_or_a_method_that_takes_no_InlineValidator_is_not_RASK139(string declaration)
    {
        var source = "using FluentValidation;\nnamespace Shop;\n" + declaration;

        var diagnostics = await RunAsync(new InlineRulesShapeAnalyzer(), source, withFluentValidation: true);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_Rules_method_is_not_reported_when_the_app_does_not_reference_FluentValidation()
    {
        const string source = "namespace FluentLike { public sealed class InlineValidator<T> { } }\nnamespace Shop { public sealed class Supplier { void Rules(FluentLike.InlineValidator<Supplier> rules) { } } }";

        var diagnostics = await RunAsync(new InlineRulesShapeAnalyzer(), source, withFluentValidation: false);

        Assert.Empty(diagnostics);
    }

    private const string ValueObjectHeader = """
        using System.Collections.Generic;
        using FluentValidation;
        using Rask.Values;
        namespace Shop;
        """;

    [Fact]
    public async Task A_value_object_with_both_a_Rules_override_and_inline_FluentValidation_rules_is_RASK183()
    {
        const string source = ValueObjectHeader + """

            public sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 100)
            {
                protected override IEnumerable<Problem> Rules() => [];
                static void Rules(InlineValidator<SupplierName> rules) => rules.RuleFor(n => n.Value).NotEmpty();
            }
            """;

        var diagnostics = await RunAsync(new InlineRulesOverrideAnalyzer(), source, withFluentValidation: true);

        var d = Assert.Single(diagnostics);
        Assert.Equal("RASK183", d.Id);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("'SupplierName' overrides Rules() and declares a static Rules(InlineValidator<SupplierName>) too — ", d.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("protected override IEnumerable<Problem> Rules() => [];")]
    [InlineData("static void Rules(InlineValidator<SupplierName> rules) => rules.RuleFor(n => n.Value).NotEmpty();")]
    public async Task A_value_object_with_one_way_of_stating_its_rules_is_not_RASK183(string member)
    {
        var source = ValueObjectHeader + "\npublic sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 100) { " + member + " }";

        var diagnostics = await RunAsync(new InlineRulesOverrideAnalyzer(), source, withFluentValidation: true);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task The_generated_halfs_Rules_override_is_not_RASK183()
    {
        var user = ValueObjectHeader + "\npublic sealed partial record SupplierName(string Value) : Text(Value, MaxLength: 100) { static void Rules(InlineValidator<SupplierName> rules) => rules.RuleFor(n => n.Value).NotEmpty(); }";
        const string generated = "// <auto-generated/>\nnamespace Shop;\npartial record SupplierName { protected override System.Collections.Generic.IEnumerable<Rask.Values.Problem> Rules() => []; }";

        var diagnostics = await RunAsync(new InlineRulesOverrideAnalyzer(), user, withFluentValidation: true, generated);

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAsync(
        DiagnosticAnalyzer analyzer, string source, bool withFluentValidation, string? generated = null)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        SyntaxTree[] trees = generated is null
            ? [CSharpSyntaxTree.ParseText(source, parse)]
            : [CSharpSyntaxTree.ParseText(source, parse), CSharpSyntaxTree.ParseText(generated, parse, path: "SupplierName.Rules.g.cs")];
        // The test host's own dependencies are trusted platform assemblies, FluentValidation among them: dropped, so
        // "without" means without, and added back by name when the case wants it.
        var references = GeneratorDriverFixture.BuildReferences()
            .RemoveAll(static r => Path.GetFileName(r.Display ?? "").StartsWith("FluentValidation", StringComparison.Ordinal));
        if (withFluentValidation)
        {
            references = references.Add(GeneratorDriverFixture.AssemblyReference("FluentValidation"));
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        return await CompiledAnalysis.RunAsync(compilation, analyzer);
    }
}
