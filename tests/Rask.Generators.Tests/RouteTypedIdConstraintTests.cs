using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Generators.Tests;

/// <summary>
///     A typed id meets the route constraint of the value it holds — <c>{id:guid}</c> for a <c>DriverId(Guid Value)</c> —
///     and the generated factory and <c>Url()</c> keep taking the typed id.
/// </summary>
public class RouteTypedIdConstraintTests
{
    private const string Page = """
                                [Route("/drivers/{id:guid}")]
                                public sealed partial class DriverPage : Component
                                {
                                    [RouteParam] public DriverId Id { get; set; }
                                    public override Component? Render() => this;
                                }
                                """;

    [Fact]
    public void A_typed_id_over_a_Guid_meets_a_guid_constraint_and_the_url_takes_the_typed_id()
    {
        var src = $$"""
                    using System;
                    using Rask.Core;
                    using Rask.Core.Routing;
                    namespace Demo;
                    public readonly partial record struct DriverId(Guid Value);
                    {{Page}}
                    """;

        var run = GeneratorDriverFixture.RunRoutes(src);
        var output = run.GeneratedSource("Routes.g.cs");

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK005");
        Assert.Contains("public static global::Rask.Core.Routing.RouteUrl DriverPage(global::Demo.DriverId Id)", output, StringComparison.Ordinal);
        Assert.Contains("public static global::Rask.Core.Routing.RouteUrl Url(global::Demo.DriverId Id)", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int", "int")]
    [InlineData("long", "long")]
    public void A_typed_id_over_a_number_meets_its_number_constraint(string value, string constraint)
    {
        var src = $$"""
                    using Rask.Core;
                    using Rask.Core.Routing;
                    namespace Demo;
                    public readonly partial record struct DriverId({{value}} Value);
                    {{Page.Replace("{id:guid}", "{id:" + constraint + "}", StringComparison.Ordinal)}}
                    """;

        var run = GeneratorDriverFixture.RunRoutes(src);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK005");
        Assert.Contains("DriverPage(global::Demo.DriverId Id)", run.GeneratedSource("Routes.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_id_over_a_Guid_under_an_int_constraint_is_still_RASK005()
    {
        var src = $$"""
                    using System;
                    using Rask.Core;
                    using Rask.Core.Routing;
                    namespace Demo;
                    public readonly partial record struct DriverId(Guid Value);
                    {{Page.Replace("{id:guid}", "{id:int}", StringComparison.Ordinal)}}
                    """;

        var run = GeneratorDriverFixture.RunRoutes(src);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK005");
    }

    [Fact]
    public void A_typed_id_from_a_referenced_assembly_meets_a_guid_constraint()
    {
        var library = ReferencedTypedId();

        var run = GeneratorDriverFixture.RunRoutes($$"""
                                                     using Lib;
                                                     using Rask.Core;
                                                     using Rask.Core.Routing;
                                                     namespace Demo;
                                                     {{Page}}
                                                     """, [library]);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "RASK005");
        Assert.Contains("Url(global::Lib.DriverId Id)", run.GeneratedSource("Routes.g.cs"), StringComparison.Ordinal);
    }

    // The typed id as TypedIdGenerator completes it, compiled into a library: read from metadata, with no declaration.
    private static MetadataReference ReferencedTypedId()
    {
        var tree = CSharpSyntaxTree.ParseText("""
                                              using System;
                                              namespace Lib;
                                              public readonly record struct DriverId(Guid Value) : ISpanParsable<DriverId>
                                              {
                                                  public static DriverId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));
                                                  public static bool TryParse(string? s, IFormatProvider? provider, out DriverId result)
                                                  {
                                                      var parsed = Guid.TryParse(s, provider, out var value);
                                                      result = new(value);
                                                      return parsed;
                                                  }
                                                  public static DriverId Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => new(Guid.Parse(s, provider));
                                                  public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out DriverId result)
                                                  {
                                                      var parsed = Guid.TryParse(s, provider, out var value);
                                                      result = new(value);
                                                      return parsed;
                                                  }
                                              }
                                              """, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "Lib", [tree], GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
