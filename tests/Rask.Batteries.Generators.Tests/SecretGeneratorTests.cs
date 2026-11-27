using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators.Tests;

/// <summary>
/// A <c>Secret&lt;T&gt;</c> an aggregate holds is stored as its ciphertext wherever it sits — its own column, a value
/// object's member, a JSON collection's element — and is never on the read face or the generated model.
/// </summary>
public class SecretGeneratorTests
{
    private const string Partner = """
        using System;
        using Rask.Data;
        using Rask.Values;
        namespace Shop;

        public sealed partial record ApiKey(string Value) : Text(Value, MaxLength: 200);
        public sealed record Pair(string FileId, int DbId);
        public sealed record Well
        {
            private Well() { }
            public string Name { get; init; } = "";
            public Secret<ApiKey>? Key { get; init; }
            public ValueList<Pair> Pairs { get; init; } = [];
        }

        public sealed class Partner : Aggregate<Guid>, IHasTimestamps, IVersioned, IHasForm, IDeletable
        {
            private Partner() { }
            public string Name { get; private set; } = "";
            public Secret<string>? Token { get; private set; }
            public Well? Settings { get; private set; }
            public ValueList<Well> Wells { get; private set; } = [];
        }
        """;

    private static GeneratorRun Run(IIncrementalGenerator generator) =>
        GeneratorHarness.Run(Partner, generator, "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void Each_secret_type_the_model_holds_however_deep_is_converted_once()
    {
        var run = Run(new ModelRegistryGenerator());

        var registry = run.GeneratedSource("__RaskModelRegistry");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("configurationBuilder.Properties<global::Rask.Values.Secret<global::Shop.ApiKey>>()", registry, StringComparison.Ordinal);
        Assert.Contains(".HaveConversion<global::Rask.Data.SecretConverter<global::Shop.ApiKey>>();", registry, StringComparison.Ordinal);
        Assert.Contains("configurationBuilder.Properties<global::Rask.Values.Secret<string>>()", registry, StringComparison.Ordinal);
        Assert.Equal(2, registry.Split("Properties<global::Rask.Values.Secret<global::Shop.ApiKey>>()").Length);
    }

    [Fact]
    public void A_secret_is_left_off_the_read_face_on_the_row_inside_a_value_object_and_in_a_list()
    {
        var run = Run(new ReadModelGenerator());

        var face = run.GeneratedSource("Shop_PartnerRead");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(" Name { get; init; }", face, StringComparison.Ordinal);
        Assert.DoesNotContain(" Token { get;", face, StringComparison.Ordinal);
        Assert.Contains(" SettingsName { get;", face, StringComparison.Ordinal);
        Assert.DoesNotContain(" SettingsKey { get;", face, StringComparison.Ordinal);
        Assert.DoesNotContain(" Wells { get;", face, StringComparison.Ordinal);
    }

    [Fact]
    public void A_secret_is_left_off_the_generated_model_so_no_form_is_ever_filled_with_it()
    {
        var run = Run(new ModelInputGenerator());

        var model = run.GeneratedSource("Shop.PartnerModel");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(" Name { get; set; }", model, StringComparison.Ordinal);
        Assert.DoesNotContain(" Token { get;", model, StringComparison.Ordinal);
    }
}
