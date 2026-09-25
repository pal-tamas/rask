using Microsoft.CodeAnalysis;

namespace Rask.Generators.Tests;

// The declaration text in each test is what tsgo --declaration emits for the script — the generator
// never sees the .ts itself (see Rask.Core.targets).
public class ScopedScriptCallsGeneratorTests
{
    private const string Card = """
                                namespace Foo;
                                public sealed partial class Card : Rask.Core.Component
                                {
                                    protected override Rask.Core.Component? Render() => this;
                                }
                                """;

    [Fact]
    public void A_number_export_becomes_a_private_method_returning_a_double()
    {
        var run = Run("export declare function width(el: HTMLElement | null): number;\nexport {};");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains(
            "private global::System.Threading.Tasks.ValueTask<double> Width(global::Rask.Core.ElementRef? el)",
            generated);
        Assert.Contains("ScopedScript.Call<double>(this, \"Rask.Card.width\", new object?[] { el })", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_promise_of_void_becomes_a_ValueTask()
    {
        var run = Run("export declare function copy(text: string, btn: HTMLElement | null): Promise<void>;");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains(
            "private global::System.Threading.Tasks.ValueTask Copy(string text, global::Rask.Core.ElementRef? btn)",
            generated);
        Assert.Contains("ScopedScript.Call(this, \"Rask.Card.copy\"", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void Optional_parameters_default_to_null()
    {
        var run = Run("export declare function greet(name: string, title?: string): string;");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("Greet(string name, string? title = null)", generated);
        Assert.Contains("ScopedScript.Trim(new object?[] { name, title }, 1)", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_string_union_and_an_array_map_to_string_and_a_read_only_list()
    {
        var run = Run("""
                      type Mode = "light" | "dark";
                      export declare function modes(current: Mode): readonly number[];
                      """);

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains(
            "ValueTask<global::System.Collections.Generic.IReadOnlyList<double>> Modes(string current)",
            generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void An_inferred_union_of_number_literals_becomes_a_double()
    {
        var run = Run("export declare function width(el: HTMLElement | null): 0 | 1;");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("ValueTask<double> Width(", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void Any_goes_in_as_object_and_comes_back_as_a_JsonElement()
    {
        var run = Run("export declare function raw(x: any): unknown;");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("ValueTask<global::System.Text.Json.JsonElement> Raw(object? x)", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void An_exported_interface_becomes_a_nested_record()
    {
        var run = Run("""
                      /** A point on the page. */
                      export interface Point {
                          x: number;
                          label?: string | null;
                      }
                      export declare function centre(): Point;
                      """);

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("private sealed record Point", generated);
        Assert.Contains("public required double X { get; init; }", generated);
        Assert.Contains("public string? Label { get; init; }", generated);
        Assert.Contains("JsonIgnoreCondition.WhenWritingNull", generated);
        Assert.Contains("JsonPropertyName(\"label\")", generated);
        Assert.Contains("ValueTask<Point> Centre()", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_callback_parameter_gets_a_sync_and_an_async_overload()
    {
        var run = Run("export declare function onTick(cb: (n: number) => void): void;");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("OnTick(global::System.Action<double> cb)", generated);
        Assert.Contains("OnTick(global::System.Func<double, global::System.Threading.Tasks.Task> cb)", generated);
        Assert.Contains("ScopedScript.Callback(this, new global::Rask.Core.Callback<double>(cb))", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_lambda_binds_to_the_generated_callback_overloads()
    {
        const string source = """
                              namespace Foo;
                              public sealed partial class Card : Rask.Core.Component
                              {
                                  private double _n;
                                  protected override Rask.Core.Component? Render() => this;
                                  private async System.Threading.Tasks.Task Wire()
                                  {
                                      await OnTick(n => _n = n);
                                      await OnTick(async n => await System.Threading.Tasks.Task.Delay((int)n));
                                      await OnDone(() => _n = 0);
                                  }
                              }
                              """;

        var run = GeneratorDriverFixture.RunScopedDeclarations(
            [("/proj/Card.cs", source)],
            [("/proj/Card.ts", """
                               export declare function onTick(cb: (n: number) => void): void;
                               export declare function onDone(cb?: () => void): void;
                               """)]);

        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void An_exported_class_gets_a_nested_proxy_and_a_New_method()
    {
        var run = Run("""
                      export declare class Chart {
                          private el;
                          constructor(el: HTMLElement);
                          draw(data: number[]): void;
                          get size(): number;
                      }
                      export declare function share(chart: Chart): void;
                      """);

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("private sealed class Chart : global::Rask.Core.ScopedAssets.ScriptObject", generated);
        Assert.Contains("ValueTask<Chart> NewChart(global::Rask.Core.ElementRef? el)", generated);
        Assert.Contains("\"Rask.Card.__new_Chart\"", generated);
        Assert.Contains("public global::System.Threading.Tasks.ValueTask Draw(", generated);
        Assert.Contains("new object?[] { chart.Reference }", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_name_the_component_inherits_is_hidden_with_new()
    {
        var run = Run("export declare function stop(root: HTMLElement | null): void;");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("private new global::System.Threading.Tasks.ValueTask Stop(", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_name_only_an_internal_framework_member_has_is_not_hidden()
    {
        // AdoptChild is internal to Rask.Core: the app cannot see it, so `new` would be CS0109.
        var run = Run("export declare function adoptChild(): void;");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("private global::System.Threading.Tasks.ValueTask AdoptChild()", generated);
        Assert.DoesNotContain("private new", generated);
    }

    [Fact]
    public void The_JSDoc_of_an_export_becomes_its_XML_documentation()
    {
        var run = Run("""
                      /**
                       * Measures the `box`.
                       * @param el The element to measure.
                       * @returns Its width in pixels.
                       */
                      export declare function width(el: HTMLElement): number;
                      """);

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("/// <summary>Measures the <c>box</c>.</summary>", generated);
        Assert.Contains("/// <param name=\"el\">The element to measure.</param>", generated);
        Assert.Contains("/// <returns>Its width in pixels.</returns>", generated);
    }

    [Fact]
    public void A_parameter_named_like_a_keyword_is_escaped()
    {
        var run = Run("export declare function handle(event: string, class: number): void;");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("Handle(string @event, double @class)", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void An_unmappable_export_is_skipped_with_RASK094_and_the_rest_still_generate()
    {
        var run = Run("""
                      export declare function pair(): [number, string?];
                      export declare function width(): number;
                      """);

        var generated = run.GeneratedSource("Card.ScopedScript");

        var diagnostic = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK094");
        Assert.Contains("'pair'", diagnostic.GetMessage());
        Assert.Contains("a tuple with an optional element", diagnostic.GetMessage());
        Assert.DoesNotContain("Pair(", generated);
        Assert.Contains("Width()", generated);
    }

    [Fact]
    public void An_element_return_is_reported_as_RASK094()
    {
        var run = Run("export declare function find(): HTMLElement;");

        var diagnostic = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK094");

        Assert.Contains("cannot come back to C#", diagnostic.GetMessage());
    }

    [Fact]
    public void A_non_partial_component_reports_RASK094()
    {
        const string source = """
                              namespace Foo;
                              public sealed class Card : Rask.Core.Component
                              {
                                  protected override Rask.Core.Component? Render() => this;
                              }
                              """;

        var run = GeneratorDriverFixture.RunScopedDeclarations(
            [("/proj/Card.cs", source)],
            [("/proj/Card.ts", "export declare function width(): number;")]);

        var diagnostic = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK094");
        Assert.Contains("partial", diagnostic.GetMessage());
        Assert.Empty(run.RunResult.Results.SelectMany(r => r.GeneratedSources));
    }

    [Fact]
    public void An_export_named_like_a_member_the_component_declares_reports_RASK094()
    {
        const string source = """
                              namespace Foo;
                              public sealed partial class Card : Rask.Core.Component
                              {
                                  protected override Rask.Core.Component? Render() => this;
                                  private void Width() { }
                              }
                              """;

        var run = GeneratorDriverFixture.RunScopedDeclarations(
            [("/proj/Card.cs", source)],
            [("/proj/Card.ts", "export declare function width(): number;")]);

        var diagnostic = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK094");
        Assert.Contains("already has a member named 'Width'", diagnostic.GetMessage());
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_function_sharing_its_interfaces_name_reports_RASK094()
    {
        var run = Run("""
                      export interface Viewport { width: number; }
                      export declare function viewport(): Viewport;
                      """);

        var diagnostic = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK094");

        Assert.Contains("the interface 'Viewport'", diagnostic.GetMessage());
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void An_arrow_in_an_exported_const_becomes_a_method_like_a_function()
    {
        var run = Run("export declare const double: (x: number) => number;");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("private global::System.Threading.Tasks.ValueTask<double> Double(double x)", generated);
        Assert.Contains("ScopedScript.Call<double>(this, \"Rask.Card.double\", new object?[] { x })", generated);
        Assert.DoesNotContain(run.RunResult.Diagnostics, d => d.Id == "RASK094");
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void An_exported_value_is_reported_as_having_nothing_to_call()
    {
        var run = Run("export declare const PI = 3.14;");

        var diagnostic = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK094");

        Assert.Contains("'PI'", diagnostic.GetMessage());
        Assert.Contains("a value, not a function", diagnostic.GetMessage());
    }

    [Fact]
    public void A_tuple_return_becomes_a_CSharp_tuple_read_element_by_element()
    {
        var run = Run("export declare function pair(): [number, string];");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("private global::System.Threading.Tasks.ValueTask<(double, string)> Pair()", generated);
        Assert.Contains(
            "ScopedScript.Tuple<(double, string)>(this, \"Rask.Card.pair\", static __r => "
            + "(global::Rask.Core.ScopedAssets.ScopedScript.Item<double>(__r, 0), "
            + "global::Rask.Core.ScopedAssets.ScopedScript.Item<string>(__r, 1))",
            generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_labelled_tuple_names_its_elements()
    {
        var run = Run("export declare function size(): Promise<[width: number, height: number]>;");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("ValueTask<(double Width, double Height)> Size()", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_tuple_parameter_crosses_as_the_array_the_script_expects()
    {
        var run = Run("export declare function place(at: [number, number]): void;");

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("Place((double, double) at)", generated);
        Assert.Contains("new object?[] { new object?[] { at.Item1, at.Item2 } }", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_class_method_can_return_a_tuple_too()
    {
        var run = Run("""
                      export declare class Meter {
                          constructor();
                          range(): [number, number];
                      }
                      """);

        var generated = run.GeneratedSource("Card.ScopedScript");

        Assert.Contains("CallScriptTuple<(double, double)>(\"range\"", generated);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_tuple_inside_other_data_is_reported_with_what_to_do_instead()
    {
        var run = Run("export declare function pairs(): [number, string][];");

        var diagnostic = Assert.Single(run.RunResult.Diagnostics, d => d.Id == "RASK094");

        Assert.Contains("a tuple inside other data", diagnostic.GetMessage());
    }

    [Fact]
    public void A_declaration_file_with_no_matching_component_generates_nothing()
    {
        var run = GeneratorDriverFixture.RunScopedDeclarations(
            [("/proj/Card.cs", Card)],
            [("/proj/Other.ts", "export declare function width(): number;")]);

        Assert.Empty(run.RunResult.Results.SelectMany(r => r.GeneratedSources));
        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    private static GeneratorRun Run(string declarations) =>
        GeneratorDriverFixture.RunScopedDeclarations([("/proj/Card.cs", Card)], [("/proj/Card.ts", declarations)]);
}
