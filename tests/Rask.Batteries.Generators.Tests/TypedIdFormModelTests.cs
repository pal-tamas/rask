namespace Rask.Batteries.Generators.Tests;

/// <summary>
///     A generated form model holds another row's typed id AS the typed id — <c>CompanyId?</c>, never the <c>int?</c> it
///     holds — so a select binds it straight to the read face's choices and the write hands it to the aggregate as it is.
/// </summary>
/// <remarks>TypedIdGenerator is not part of this run, so the Guid id writes its own <c>New()</c>.</remarks>
public class TypedIdFormModelTests
{
    private const string Source = """
        using System;
        using Rask.Data;
        namespace Shop;
        public readonly partial record struct CompanyId(int Value);
        public readonly partial record struct DepotId(Guid Value) { public static DepotId New() => new(Guid.CreateVersion7()); }
        public sealed class Company : Aggregate<CompanyId, Model> { private Company() { } public string Name { get; private set; } = ""; }
        public sealed class Depot : Aggregate<DepotId, Model> { private Depot() { } public string Name { get; private set; } = ""; }
        public sealed class Contract : Aggregate<Guid, Model>
        {
            private Contract() { }
            public CompanyId Company { get; private set; }
            public DepotId? Depot { get; private set; }
        }
        """;

    private static GeneratorRun Run() =>
        GeneratorHarness.Run(Source, new ModelInputGenerator(), "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    [Fact]
    public void A_typed_id_member_is_the_typed_id_on_the_model()
    {
        var run = Run();

        var model = run.GeneratedSource("Shop.ContractModel");

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains("public global::Shop.CompanyId? Company { get; set; }", model, StringComparison.Ordinal);
        Assert.Contains("public global::Shop.DepotId? Depot { get; set; }", model, StringComparison.Ordinal);
        Assert.DoesNotContain("int? Company", model, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid? Depot", model, StringComparison.Ordinal);
    }

    [Fact]
    public void The_model_and_the_aggregate_trade_the_typed_id_with_no_unwrap()
    {
        var run = Run();

        var model = run.GeneratedSource("Shop.ContractModel");

        Assert.Contains("__Setter0_Company(entity, __v0);", model, StringComparison.Ordinal);
        Assert.Contains("__Setter1_Depot(entity, __v1);", model, StringComparison.Ordinal);
        Assert.Contains("model.Company = entity.Company;", model, StringComparison.Ordinal);
        Assert.Contains("model.Depot = entity.Depot;", model, StringComparison.Ordinal);
        Assert.DoesNotContain(".Value", model, StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_id_is_another_rows_id_and_required_unless_the_aggregate_lets_it_be_empty()
    {
        var run = Run();

        var model = run.GeneratedSource("Shop.ContractModel");

        Assert.Contains("\"Company\" => new global::Rask.Core.Forms.FieldShape(global::Rask.Core.Forms.FieldKind.Id),", model, StringComparison.Ordinal);
        Assert.Contains("\"Depot\" => new global::Rask.Core.Forms.FieldShape(global::Rask.Core.Forms.FieldKind.Id),", model, StringComparison.Ordinal);
        Assert.Contains("property is \"Company\";", model, StringComparison.Ordinal);
    }
}
