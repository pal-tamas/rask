using Microsoft.CodeAnalysis;
using Rask.Cqrs.Generators;
using Rask.Generators.Shared;

namespace Rask.Data.Generators.Tests;

/// <summary>
/// A Rask.Data entity's generated model, as the OTHER battery generators meet it.
/// </summary>
/// <remarks>
/// Source generators cannot see each other's output: in the compilation that declares <c>Product</c>,
/// <c>ProductModel</c> is an unresolved error type to every generator but the one that emits it. So each case
/// runs the model generator beside its consumer in ONE driver, and <see cref="GeneratorRun.GeneratedCompileErrors"/>
/// compiles both outputs together — the only place "does the codec build a model it was never shown" has an
/// answer. Asserting on the codec's text alone would pass for a codec naming a type that does not bind.
/// </remarks>
public class GeneratedModelAcrossGeneratorsTests
{
    private const string Entities = """
        using System;
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations;
        using Rask.Cqrs;
        using Rask.Data;

        namespace Shop
        {
            public sealed record Money(decimal Amount, string Currency) : IValueObject;

            public sealed class Product : Model<Guid>, IVersioned
            {
                private Product() { }
                [Required, MaxLength(200)] public string Name { get; private set; } = "";
                public Money Price { get; private set; } = new(0m, "EUR");
                public string? Notes { get; private set; }
                public int Version { get; private set; }
            }
        }

        """;

    // Rask.Cqrs.Client is here for its [assembly: RaskCqrsTransport]: without a transport the codec
    // generator emits nothing at all, and every assertion below would pass against an empty run.
    private static GeneratorRun RunCodec(string source) =>
        GeneratorHarness.Run(
            Entities + source,
            [new ModelInputGenerator(), new CqrsCodecGenerator()],
            "Rask.Data", "Rask.Cqrs", "Rask.Cqrs.Client", "Rask.Wire", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void A_message_carrying_a_generated_model_gets_a_codec_that_builds_it()
    {
        var run = RunCodec("""
            namespace Shop.Contracts
            {
                public sealed record SaveProduct(ProductModel Product) : ICommand;
                public sealed record ImportProducts(List<ProductModel> Products) : ICommand<int>;
                public sealed record GetProduct(Guid Id) : IQuery<ProductModel?>;
            }
            """);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK053");
        Assert.Empty(run.GeneratedCompileErrors());

        var codec = run.GeneratedSource("__RaskCqrsCodecs");

        // Every member the model generator gives the model — the version included — is read back, and the key,
        // which the model does not carry, is not.
        Assert.Contains("return new global::Shop.ProductModel", codec, StringComparison.Ordinal);
        foreach (var member in new[] { "Name", "Price", "Notes", "Version" })
        {
            Assert.Contains($"{member} = v_{member},", codec, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Id = v_Id,", codec, StringComparison.Ordinal);

        // The value object travels as its nested model, which is what the model's property is typed as.
        Assert.Contains("return new global::Shop.ProductModel.MoneyModel", codec, StringComparison.Ordinal);
        Assert.Contains("Amount = v_Amount,", codec, StringComparison.Ordinal);
        Assert.Contains("writer.WritePropertyName(\"price\");", codec, StringComparison.Ordinal);

        // Named in full wherever it sits inside another type, or it would not bind from the global namespace.
        Assert.Contains("global::System.Collections.Generic.List<global::Shop.ProductModel>", codec, StringComparison.Ordinal);
        Assert.Contains("ResultType = typeof(global::Shop.ProductModel),", codec, StringComparison.Ordinal);
        Assert.DoesNotContain("<ProductModel>", codec, StringComparison.Ordinal);
    }

    [Fact]
    public void A_partial_model_travels_with_the_generated_members_and_the_authors_own()
    {
        // The partial RESOLVES to other generators — to the author's half only. Encoding what is visible
        // would send a model with none of its values and no error to say so.
        var run = RunCodec("""
            namespace Shop
            {
                public sealed partial class ProductModel
                {
                    public string? Tag { get; set; }
                }
            }
            namespace Shop.Contracts
            {
                public sealed record SaveProduct(ProductModel Product) : ICommand;
            }
            """);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK053");
        Assert.Empty(run.GeneratedCompileErrors());

        var codec = run.GeneratedSource("__RaskCqrsCodecs");
        Assert.Contains("Name = v_Name,", codec, StringComparison.Ordinal);
        Assert.Contains("Tag = v_Tag,", codec, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unresolved_Model_type_with_no_entity_behind_it_is_still_RASK053()
    {
        var run = RunCodec("""
            namespace Shop.Contracts
            {
                public sealed record SaveFoo(FooModel Foo) : ICommand;
            }
            """);

        Assert.Contains(
            run.Diagnostics,
            d => d.Id == "RASK053" && d.GetMessage().Contains("SaveFoo", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unqualified_model_two_entities_could_own_is_RASK053_naming_the_way_out()
    {
        var run = RunCodec("""
            namespace Billing
            {
                public sealed class Product : Model<Guid>
                {
                    private Product() { }
                    public string Sku { get; private set; } = "";
                }
            }
            namespace Shop.Contracts
            {
                public sealed record SaveProduct(ProductModel Product) : ICommand;
            }
            """);

        var message = Assert.Single(
            run.Diagnostics.Where(d => d.Id == "RASK053").Select(d => d.GetMessage()).Distinct());
        Assert.Contains("'Shop.Product'", message, StringComparison.Ordinal);
        Assert.Contains("'Billing.Product'", message, StringComparison.Ordinal);
        Assert.Contains("with its namespace", message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_qualified_model_is_resolved_in_the_namespace_it_names()
    {
        var run = RunCodec("""
            namespace Billing
            {
                public sealed class Product : Model<Guid>
                {
                    private Product() { }
                    public string Sku { get; private set; } = "";
                }
            }
            namespace Orders
            {
                public sealed record SaveBilling(Billing.ProductModel Product) : ICommand;
            }
            """);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK053");
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("Sku = v_Sku,", run.GeneratedSource("__RaskCqrsCodecs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_handler_answering_with_a_generated_model_registers_under_its_full_name()
    {
        var run = GeneratorHarness.Run(
            Entities + """
                namespace Shop.Contracts
                {
                    public sealed record GetProduct(Guid Id) : IQuery<ProductModel>;

                    public sealed class GetProductHandler : IQueryHandler<GetProduct, ProductModel>
                    {
                        public System.Threading.Tasks.Task<ProductModel> HandleAsync(
                            GetProduct query, System.Threading.CancellationToken cancellationToken) =>
                            throw new NotSupportedException();
                    }
                }
                """,
            [new ModelInputGenerator(), new CqrsDispatchGenerator()],
            "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore",
            "Microsoft.Extensions.DependencyInjection.Abstractions");

        // An error type has no accessibility to read, so the handler was skipped as "not accessible from
        // generated code" — a warning, an empty registry that compiles, and a request that throws at runtime.
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK029");
        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            run.RunResult.Results.SelectMany(r => r.GeneratedSources),
            s => s.SourceText.ToString().Contains(
                "global::Rask.Cqrs.IQueryHandler<global::Shop.Contracts.GetProduct, global::Shop.ProductModel>",
                StringComparison.Ordinal));
    }

    [Fact]
    public void A_generated_model_is_declared_in_TypeScript_under_its_own_name()
    {
        var run = RunCodec("""
            namespace Shop.Contracts
            {
                public sealed record SaveProduct(ProductModel Product) : ICommand;
            }
            """);

        var message = run.Compilation.GetTypeByMetadataName("Shop.Contracts.SaveProduct")
                      ?? throw new InvalidOperationException("SaveProduct did not compile.");

        var emitter = new TypeScriptEmitter();
        emitter.Ensure(WireShape.Classify(message, allowFile: false, compilation: run.Compilation));
        var ts = emitter.Declarations;

        Assert.Contains("product: ProductModel;", ts, StringComparison.Ordinal);
        Assert.Contains("export interface ProductModel {", ts, StringComparison.Ordinal);
        Assert.Contains("price: MoneyModel;", ts, StringComparison.Ordinal);
        Assert.Contains("notes: string | null;", ts, StringComparison.Ordinal);
        Assert.Contains("export interface MoneyModel {", ts, StringComparison.Ordinal);
        Assert.DoesNotContain("Anonymous", ts, StringComparison.Ordinal);
    }

    [Fact]
    public void A_nullable_generated_model_member_is_nullable_in_TypeScript()
    {
        // `ProductModel?` binds as Nullable<ProductModel> to a generator, so the property's annotation says
        // nothing — the member was declared `draft: ProductModel;` and the front end was promised a value.
        var run = RunCodec("""
            namespace Shop.Contracts
            {
                public sealed record SaveDraft(ProductModel? Draft) : ICommand;
            }
            """);

        var message = run.Compilation.GetTypeByMetadataName("Shop.Contracts.SaveDraft")!;
        var emitter = new TypeScriptEmitter();
        emitter.Ensure(WireShape.Classify(message, allowFile: false, compilation: run.Compilation));

        Assert.Contains("draft: ProductModel | null;", emitter.Declarations, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_value_object_built_through_accessors_travels_as_its_ordinary_nested_model()
    {
        // How the entity's value object is rebuilt is the model generator's business; the nested model is the
        // same mutable class either way, so the codec builds it with an object initializer as before.
        var run = GeneratorHarness.Run(
            """
            using System;
            using Rask.Cqrs;
            using Rask.Data;

            namespace Shop
            {
                public sealed class Address : IValueObject
                {
                    private Address() { }
                    public string Street { get; private set; } = "";
                    public string City { get; private set; } = "";
                }

                public sealed class Customer : Model<Guid>
                {
                    private Customer() { }
                    public string Name { get; private set; } = "";
                    public Address Home { get; private set; } = null!;
                }
            }

            namespace Shop.Contracts
            {
                public sealed record SaveCustomer(Guid Id, CustomerModel Customer) : ICommand;
            }
            """,
            [new ModelInputGenerator(), new CqrsCodecGenerator()],
            "Rask.Data", "Rask.Cqrs", "Rask.Cqrs.Client", "Rask.Wire", "Microsoft.EntityFrameworkCore");

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK053");
        Assert.Empty(run.GeneratedCompileErrors());

        var codec = run.GeneratedSource("__RaskCqrsCodecs");
        Assert.Contains("return new global::Shop.CustomerModel.AddressModel", codec, StringComparison.Ordinal);
        Assert.Contains("Street = v_Street,", codec, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_compilation_the_model_is_unsupported_exactly_as_before()
    {
        var run = RunCodec("""
            namespace Shop.Contracts
            {
                public sealed record SaveProduct(ProductModel Product) : ICommand;
            }
            """);

        var message = run.Compilation.GetTypeByMetadataName("Shop.Contracts.SaveProduct")!;

        Assert.Equal(WireKind.Unsupported, WireShape.Classify(message, allowFile: true).Kind);
    }
}
