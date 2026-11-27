namespace Rask.Generators.Tests;

public class TypedIdGeneratorTests
{
    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("System.Guid")]
    [InlineData("string")]
    public void A_readonly_partial_record_struct_over_one_Value_is_completed_into_an_id_that_compiles(string value)
    {
        var src = $"namespace Fleet; public readonly partial record struct VehicleId({value} Value);";

        var run = GeneratorDriverFixture.Run([("VehicleId.cs", src)], new TypedIdGenerator());

        var generated = run.GeneratedSource("Fleet.VehicleId.TypedId.g.cs");
        Assert.Contains("readonly partial record struct VehicleId : global::System.ISpanParsable<VehicleId>", generated);
        Assert.Contains("JsonConverter(typeof(VehicleId.__JsonConverter))", generated);
        Assert.Contains("public override string ToString()", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void Only_an_id_over_a_Guid_gets_New()
    {
        var src = """
                  namespace Fleet;
                  public readonly partial record struct VehicleId(int Value);
                  public readonly partial record struct DriverId(System.Guid Value);
                  """;

        var run = GeneratorDriverFixture.Run([("Ids.cs", src)], new TypedIdGenerator());

        Assert.Contains("public static DriverId New()", run.GeneratedSource("Fleet.DriverId.TypedId.g.cs"));
        Assert.DoesNotContain(" New()", run.GeneratedSource("Fleet.VehicleId.TypedId.g.cs"));
    }

    [Theory]
    [InlineData("public readonly record struct VehicleId(int Value);")]
    [InlineData("public partial record struct VehicleId(int Value);")]
    [InlineData("public readonly partial record struct Count(int Value);")]
    [InlineData("public readonly partial record struct VehicleId(int Number);")]
    [InlineData("public readonly partial record struct VehicleId(int Value, int Revision);")]
    [InlineData("public readonly partial record struct VehicleId(decimal Value);")]
    [InlineData("public sealed partial record VehicleId(int Value);")]
    [InlineData("public readonly partial record struct VehicleId<T>(int Value);")]
    public void A_declaration_outside_the_rule_is_left_as_it_is(string declaration)
    {
        var src = "namespace Fleet; " + declaration;

        var run = GeneratorDriverFixture.Run([("VehicleId.cs", src)], new TypedIdGenerator());

        Assert.False(run.HasGeneratedSource(".TypedId.g.cs"));
    }

    [Fact]
    public void What_the_author_wrote_is_kept_and_only_the_rest_is_generated()
    {
        var src = """
                  namespace Fleet;
                  public readonly partial record struct VehicleId(int Value)
                  {
                      public override string ToString() => "V" + Value;
                      public static VehicleId Parse(string s) => new(int.Parse(s[1..]));
                  }
                  """;

        var run = GeneratorDriverFixture.Run([("VehicleId.cs", src)], new TypedIdGenerator());

        var generated = run.GeneratedSource("Fleet.VehicleId.TypedId.g.cs");
        Assert.DoesNotContain("override string ToString", generated);
        Assert.DoesNotContain("public static VehicleId Parse(string s)", generated);
        Assert.Contains("public static bool TryParse(", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_typed_id_split_over_two_files_is_completed_once()
    {
        var run = GeneratorDriverFixture.Run(
            [
                ("A.cs", "namespace Fleet; public readonly partial record struct VehicleId(int Value);"),
                ("B.cs", "namespace Fleet; readonly partial record struct VehicleId(int Value) { public bool IsSet => Value > 0; }"),
            ],
            new TypedIdGenerator());

        Assert.Single(run.RunResult.Results.SelectMany(r => r.GeneratedSources),
            s => s.HintName.EndsWith(".TypedId.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void A_route_param_of_a_typed_id_binds_and_is_registered_for_the_AOT_parser()
    {
        var src = """
                  using Rask.Core;
                  using Rask.Core.Routing;
                  namespace Fleet;
                  public readonly partial record struct VehicleId(int Value);
                  [Route("/vehicles/{id}")]
                  public sealed class VehiclePage : Component
                  {
                      [RouteParam] public VehicleId Id { get; set; }
                      protected override Component? Render() => this;
                  }
                  """;

        var run = GeneratorDriverFixture.Run([("Fleet.cs", src)], [new RoutesGenerator(), new TypedIdGenerator()]);

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        Assert.Contains("RegisterParsable<global::Fleet.VehicleId>()", run.GeneratedSource("__RaskRoutesRegistry.g.cs"));
        Assert.Contains("VehiclePage(global::Fleet.VehicleId Id)", run.GeneratedSource("Routes.g.cs"));
        Assert.Empty(run.GeneratedCompileErrors());
    }
}
