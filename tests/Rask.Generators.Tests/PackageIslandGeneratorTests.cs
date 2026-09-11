using Microsoft.CodeAnalysis;
using Rask.Generators.External;

namespace Rask.Generators.Tests;

/// <summary>
///     Covers package islands end to end through both generators: a class naming an npm component by its
///     module, a committed props snapshot beside it, and the properties, writers and chain steps that come out.
/// </summary>
/// <remarks>
///     Both generators run over one compilation, and the assertion that matters most is that the result
///     COMPILES: the island generator declares a property and the factory generator emits its setter, and
///     neither can see the other's output. A test over one generator's text would pass while a step pointed at
///     a property that was never declared.
/// </remarks>
public class PackageIslandGeneratorTests
{
    private const string IslandSource =
        """
        namespace Shop;

        public sealed partial class MuiButton : Rask.External.ReactComponent
        {
            protected override string Module => "@mui/material/Button";
        }
        """;

    private const string Snapshot =
        """
        {
          "schema": 1,
          "runtime": "react",
          "module": "@mui/material/Button",
          "export": "default",
          "package": { "name": "@mui/material", "version": "7.3.1" },
          "content": "node",
          "props": [
            { "name": "variant", "doc": "The variant to use.", "default": "'text'",
              "type": { "kind": "enum", "base": "string", "values": ["text", "outlined", "contained"] } },
            { "name": "size", "type": { "kind": "enum", "base": "string", "values": ["small", "medium", "large"] } },
            { "name": "disabled", "type": { "kind": "boolean" } },
            { "name": "elevation", "type": { "kind": "number" } },
            { "name": "aria-label", "type": { "kind": "string" } },
            { "name": "startedAt", "type": { "kind": "date" } },
            { "name": "tags", "type": { "kind": "array", "element": { "kind": "string" } } },
            { "name": "classes", "type": { "kind": "ref", "name": "ButtonClasses" } },
            { "name": "value", "type": { "kind": "union", "of": [ { "kind": "string" }, { "kind": "number" } ] } },
            { "name": "onClick",
              "type": { "kind": "callback", "args": [ { "name": "event", "type": { "kind": "event", "name": "MouseEvent" } } ] } },
            { "name": "onChange",
              "type": { "kind": "callback", "args": [
                { "name": "event", "type": { "kind": "event", "name": "ChangeEvent" } },
                { "name": "value", "type": { "kind": "number" } } ] } }
          ],
          "skipped": [ { "name": "children", "reason": "node" } ],
          "types": {
            "ButtonClasses": { "kind": "object", "members": [
              { "name": "root", "required": true, "type": { "kind": "string" } },
              { "name": "label", "type": { "kind": "string" } } ] }
          }
        }
        """;

    private const string HostSource =
        """
        namespace Shop;

        public sealed partial class Page : Rask.Core.Component
        {
            private double _value;

            protected override Rask.Core.Component? Render() =>
                MuiButton
                    .Variant(MuiButtonVariant.Contained)
                    .Size(MuiButtonSize.Small)
                    .Disabled(true)
                    .Elevation(2)
                    .AriaLabel("Save")
                    .Tags(new[] { "a", "b" })
                    .Classes(new MuiButtonButtonClasses { Root = "r" })
                    .Value("ten")
                    .OnClick(() => { })
                    .OnChange(v => _value = v);
        }
        """;

    [Fact]
    public void The_chain_binds_every_generated_step_and_everything_compiles()
    {
        var run = Run(IslandSource, Snapshot, HostSource);

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void An_enum_prop_is_written_as_the_package_literal_not_a_number()
    {
        var generated = Run(IslandSource, Snapshot).GeneratedSource("MuiButton.External");

        Assert.Contains("writer.WriteStringValue(\"contained\")", generated, StringComparison.Ordinal);
        Assert.Contains("enum MuiButtonVariant", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unset_prop_is_omitted_so_the_package_default_applies()
    {
        var generated = Run(IslandSource, Snapshot).GeneratedSource("MuiButton.External");

        Assert.Contains("if (this.Disabled is not null)", generated, StringComparison.Ordinal);
        Assert.Contains("writer.WritePropertyName(\"aria-label\")", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_callback_forwards_only_its_scalar_argument_and_never_the_event()
    {
        var generated = Run(IslandSource, Snapshot).GeneratedSource("MuiButton.External");

        Assert.Contains("global::Rask.Core.Callback<double>? OnChange", generated, StringComparison.Ordinal);
        Assert.Contains("__ArgOnChange", generated, StringComparison.Ordinal);
        Assert.Contains("global::Rask.Core.Callback? OnClick", generated, StringComparison.Ordinal);

        // Every package callback carries $a, so the client never tries to serialize an event object.
        Assert.Equal(2, CountOf(generated, "writer.WriteStartArray(\"$a\")"));
    }

    [Fact]
    public void A_date_is_tagged_for_the_client_to_revive()
    {
        var generated = Run(IslandSource, Snapshot).GeneratedSource("MuiButton.External");

        Assert.Contains("writer.WriteString(\"$d\", value)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_prop_named_after_a_rask_member_is_renamed_rather_than_hiding_it()
    {
        var run = Run(IslandSource, PropSnapshot("key", """{ "kind": "string" }"""));

        Assert.Contains("string? KeyProp", run.GeneratedSource("MuiButton.External"), StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void Package_prose_cannot_escape_its_doc_comment()
    {
        var snapshot = PropSnapshot("label", """{ "kind": "string" }""", doc: "Line\\n}\\nclass Evil {");

        var run = Run(IslandSource, snapshot);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain(
            run.RunResult.Results.SelectMany(r => r.GeneratedSources),
            s => s.SourceText.ToString().Contains("\nclass Evil", StringComparison.Ordinal));
    }

    [Fact]
    public void A_wire_name_with_a_quote_is_escaped()
    {
        var run = Run(IslandSource, PropSnapshot("a\\\"b", """{ "kind": "string" }"""));

        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_hand_declared_prop_reopens_an_enum_and_suppresses_its_type()
    {
        const string island =
            """
            namespace Shop;

            public sealed partial class MuiButton : Rask.External.ReactComponent
            {
                protected override string Module => "@mui/material/Button";

                /// <summary>Any variant the theme defines.</summary>
                public string? Variant { get; set; }
            }
            """;

        var run = Run(island, Snapshot);
        var generated = run.GeneratedSource("MuiButton.External");

        Assert.DoesNotContain("enum MuiButtonVariant", generated, StringComparison.Ordinal);
        Assert.Contains("writer.WritePropertyName(\"variant\")", generated, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_package_island_with_no_snapshot_and_no_props_is_RASK077()
    {
        var run = Run(IslandSource, snapshot: null);

        var diagnostic = Assert.Single(run.Diagnostics.Where(d => d.Id == "RASK077").DistinctBy(d => d.GetMessage()));
        Assert.Contains("MuiButton.props.json", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_package_island_that_declares_its_own_props_needs_no_snapshot()
    {
        const string island =
            """
            namespace Shop;

            public sealed partial class Vendor : Rask.External.ReactComponent
            {
                protected override string Module => "@acme/charts/Chart";

                /// <summary>The heading.</summary>
                public string? Heading { get; set; }
            }
            """;

        Assert.DoesNotContain(Run(island, snapshot: null).Diagnostics, d => d.Id == "RASK077");
    }

    [Fact]
    public void An_unreadable_snapshot_is_RASK078_at_the_line_it_breaks_on()
    {
        var run = Run(IslandSource, "{\n  \"schema\": 1,\n  \"props\": [ oops ]\n}");

        var diagnostic = run.Diagnostics.First(d => d.Id == "RASK078");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.EndsWith("MuiButton.props.json", diagnostic.Location.GetLineSpan().Path, StringComparison.Ordinal);
        Assert.Equal(2, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void A_snapshot_for_another_runtime_is_RASK079_and_generates_nothing()
    {
        var run = Run(IslandSource, Snapshot.Replace("\"react\"", "\"vue\"", StringComparison.Ordinal));

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK079" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain("Variant", run.GeneratedSource("MuiButton.External"), StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_snapshot_for_another_export_is_RASK079()
    {
        var run = Run(IslandSource, Snapshot.Replace("\"export\": \"default\"", "\"export\": \"Button\"", StringComparison.Ordinal));

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK079");
    }

    [Fact]
    public void A_named_export_matches_its_snapshot()
    {
        var island = IslandSource.Replace("\"@mui/material/Button\"", "\"@mui/material#Button\"", StringComparison.Ordinal);
        var snapshot = Snapshot
            .Replace("\"@mui/material/Button\"", "\"@mui/material\"", StringComparison.Ordinal)
            .Replace("\"export\": \"default\"", "\"export\": \"Button\"", StringComparison.Ordinal);

        var run = Run(island, snapshot);

        Assert.DoesNotContain(run.Diagnostics, d => d.Id is "RASK078" or "RASK079");
        Assert.Contains("MuiButtonVariant", run.GeneratedSource("MuiButton.External"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_of_an_export_and_vue_event_names_pair_generate_and_compile()
    {
        // What the extractor writes for a Vue component reached through a namespace export: the export keeps its dot,
        // and Vue's event props keep the names Vue matches them by, while C# gets names it can compile.
        const string island =
            """
            namespace Shop;

            public sealed partial class MuiButton : Rask.External.VueComponent
            {
                protected override string Module => "fixture-vue#Parts.Toggle";
            }
            """;

        const string snapshot =
            """
            {
              "schema": 1, "runtime": "vue", "module": "fixture-vue", "export": "Parts.Toggle",
              "props": [
                { "name": "modelValue", "required": false, "type": { "kind": "boolean" } },
                { "name": "onUpdate:modelValue", "required": false,
                  "type": { "kind": "callback", "args": [ { "name": "value", "type": { "kind": "boolean" } } ] } },
                { "name": "onValue-change", "required": false,
                  "type": { "kind": "callback", "args": [
                    { "name": "value", "type": { "kind": "number" } },
                    { "name": "source", "type": { "kind": "string" } } ] } }
              ]
            }
            """;

        const string host =
            """
            namespace Shop;

            public sealed partial class Page : Rask.Core.Component
            {
                private bool _on;
                private double _value;

                protected override Rask.Core.Component? Render() =>
                    MuiButton
                        .ModelValue(true)
                        .OnUpdateModelValue(v => _on = v)
                        .OnValueChange(v => _value = v);
            }
            """;

        var run = Run(island, snapshot, host);
        var generated = run.GeneratedSource("MuiButton.External");

        Assert.DoesNotContain(run.Diagnostics, d => d.Id is "RASK078" or "RASK079" or "RASK080");
        Assert.Contains("writer.WritePropertyName(\"onUpdate:modelValue\")", generated, StringComparison.Ordinal);
        Assert.Contains("writer.WritePropertyName(\"onValue-change\")", generated, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_lit_element_named_by_its_tag_and_its_event_wire_pair_generate_and_compile()
    {
        // What the extractor writes for a Lit element whose define module is named by the tag it registers: the export is
        // the tag, and the event prop travels under `@fx-change` — the adapter's cue to listen rather than assign.
        const string island =
            """
            namespace Shop;

            public sealed partial class MuiButton : Rask.External.LitComponent
            {
                protected override string Module => "fixture-lit/fx-switch.js#fx-switch";
            }
            """;

        const string snapshot =
            """
            {
              "schema": 1, "runtime": "lit", "module": "fixture-lit/fx-switch.js", "export": "fx-switch", "tag": "fx-switch",
              "props": [
                { "name": "checked", "required": false, "type": { "kind": "boolean" } },
                { "name": "on-fx-change", "wire": "@fx-change", "required": false,
                  "type": { "kind": "callback", "args": [ { "name": "event", "type": { "kind": "event", "name": "CustomEvent" } } ] } }
              ]
            }
            """;

        const string host =
            """
            namespace Shop;

            public sealed partial class Page : Rask.Core.Component
            {
                private int _changes;

                protected override Rask.Core.Component? Render() =>
                    MuiButton
                        .Checked(true)
                        .OnFxChange(() => _changes++);
            }
            """;

        var run = Run(island, snapshot, host);
        var generated = run.GeneratedSource("MuiButton.External");

        Assert.DoesNotContain(run.Diagnostics, d => d.Id is "RASK078" or "RASK079" or "RASK080");
        Assert.Contains("writer.WritePropertyName(\"@fx-change\")", generated, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void An_angular_component_with_a_required_aliased_input_and_an_output_generates_and_compiles()
    {
        // What the extractor writes for a signal-based Angular component: `label` is published as `for` and required, so
        // it is a step the chain takes first and travels under the alias Angular sets it by; the `valueChange` output
        // travels as `@valueChange` — the adapter's cue to subscribe rather than set — carrying the number it emits.
        const string island =
            """
            namespace Shop;

            public sealed partial class MuiButton : Rask.External.AngularComponent
            {
                protected override string Module => "fixture-angular#FxSlider";
            }
            """;

        const string snapshot =
            """
            {
              "schema": 1, "runtime": "angular", "module": "fixture-angular", "export": "FxSlider", "tag": null,
              "props": [
                { "name": "label", "wire": "for", "required": true, "type": { "kind": "string" } },
                { "name": "onValueChange", "wire": "@valueChange", "required": false,
                  "type": { "kind": "callback", "args": [ { "name": "value", "type": { "kind": "number" } } ] } }
              ]
            }
            """;

        const string host =
            """
            namespace Shop;

            public sealed partial class Page : Rask.Core.Component
            {
                private double _value;

                protected override Rask.Core.Component? Render() =>
                    MuiButton
                        .Label("Volume")
                        .OnValueChange(value => _value = value);
            }
            """;

        var run = Run(island, snapshot, host);
        var generated = run.GeneratedSource("MuiButton.External");

        Assert.DoesNotContain(run.Diagnostics, d => d.Id is "RASK078" or "RASK079" or "RASK080");
        Assert.Contains("writer.WritePropertyName(\"for\")", generated, StringComparison.Ordinal);
        Assert.Contains("writer.WritePropertyName(\"@valueChange\")", generated, StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void An_island_whose_component_takes_content_takes_children_of_its_own_runtime()
    {
        // Every spelling the chain teaches for a React island's children: literals, a nested island, a conditional one, a
        // projection of islands (which binds by covariance, since a conversion never lifts through IEnumerable<>), and a
        // list of text.
        const string host =
            """
            using System.Linq;

            namespace Shop;

            public sealed partial class Page : Rask.Core.Component
            {
                private readonly string[] _names = ["Ada", "Grace"];
                private bool _saving;

                protected override Rask.Core.Component? Render() =>
                    Div[
                        MuiButton["Save ", 3, " items"],
                        MuiButton[MuiButton.Disabled(true)["nested"]],
                        MuiButton[_saving ? MuiButton["busy"] : null],
                        MuiButton[_names.Select(n => MuiButton[n])],
                        MuiButton[_names]];
            }
            """;

        var run = Run(IslandSource, Snapshot, host);

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void An_island_whose_component_takes_no_content_gets_no_children_indexer()
    {
        var snapshot = Snapshot.Replace("\"content\": \"node\"", "\"content\": \"none\"", StringComparison.Ordinal);

        var generated = Run(IslandSource, snapshot).GeneratedSource("MuiButton.External");

        // Only ExternalComponent's refusing indexers remain, which RASK062 reports at the call.
        Assert.DoesNotContain("this[", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Rask_markup_as_an_islands_child_does_not_compile()
    {
        // The compile error by construction: the refusing indexer's ref struct cannot become a Component. RASK062's own
        // report at the brackets is pinned in IslandChildrenAnalyzerTests; this pins that the TYPES alone refuse it.
        const string host =
            """
            namespace Shop;

            public sealed partial class Page : Rask.Core.Component
            {
                protected override Rask.Core.Component? Render() => Div[MuiButton[Span["x"]]];
            }
            """;

        var run = Run(IslandSource, Snapshot, host);

        Assert.Contains(run.GeneratedCompileErrors(), e => e.Id is "CS1503" or "CS0029");
    }

    [Fact]
    public void A_prop_with_no_csharp_type_is_RASK080_and_the_rest_are_still_generated()
    {
        var snapshot = PropSnapshot("mixed", """{ "kind": "union", "of": [ { "kind": "boolean" }, { "kind": "string" } ] }""");

        var run = Run(IslandSource, snapshot);

        var diagnostic = run.Diagnostics.First(d => d.Id == "RASK080");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("mixed", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_snapshot_beside_a_file_island_is_ignored()
    {
        const string island =
            """
            namespace Shop;

            public sealed partial class MuiButton : Rask.External.ReactComponent
            {
            }
            """;

        var run = Run(island, Snapshot);

        Assert.DoesNotContain("Variant", run.GeneratedSource("MuiButton.External"), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Diagnostics, d => d.Id is "RASK077" or "RASK078" or "RASK079" or "RASK080");
    }

    [Fact]
    public void A_snapshot_in_another_directory_does_not_pair()
    {
        var run = GeneratorDriverFixture.Run(
            [("/src/Shop/MuiButton.cs", IslandSource)],
            [new ExternalGenerator(), new ComponentFactoryGenerator()],
            [("/src/Other/MuiButton.props.json", Snapshot)]);

        Assert.Contains(run.Diagnostics, d => d.Id == "RASK077");
    }

    [Fact]
    public void The_island_is_left_out_of_the_typescript_carrier_and_kept_in_the_island_carrier()
    {
        var run = Run(IslandSource, Snapshot);

        Assert.DoesNotContain(
            "MuiButton", run.GeneratedSource("RaskExternalGeneratedTypeScript"), StringComparison.Ordinal);
        Assert.Contains(
            "MuiButton = \"react|@mui/material/Button\"", run.GeneratedSource("RaskExternalIslands"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_prop_named_like_any_member_the_island_declares_is_renamed()
    {
        // Not only a public settable prop: a [SkipFactory] property, a field or a method of the same name would
        // be a second declaration in the generated half — CS0102 in code the author never wrote.
        const string island =
            """
            namespace Shop;

            public sealed partial class MuiButton : Rask.External.ReactComponent
            {
                protected override string Module => "@mui/material/Button";

                /// <summary>Where the button links to, set by the page rather than the chain.</summary>
                [Rask.Core.SkipFactory]
                public string Href { get; set; } = "#";
            }
            """;

        var run = Run(island, PropSnapshot("href", """{ "kind": "string" }"""));

        Assert.Contains("string? HrefProp", run.GeneratedSource("MuiButton.External"), StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_generated_type_never_takes_the_name_of_a_type_the_namespace_already_has()
    {
        var run = GeneratorDriverFixture.Run(
            [
                ("/src/Shop/MuiButton.cs", IslandSource),
                ("/src/Shop/MuiButtonVariant.cs", "namespace Shop; public sealed class MuiButtonVariant { }"),
            ],
            [new ExternalGenerator(), new ComponentFactoryGenerator()],
            [("/src/Shop/MuiButton.props.json", Snapshot)]);

        Assert.Contains("enum MuiButtonVariant2", run.GeneratedSource("MuiButton.External"), StringComparison.Ordinal);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void Two_islands_whose_generated_type_names_meet_both_compile()
    {
        // Island Data's `gridMode` and island DataGrid's `mode` both want DataGridMode, and generated types land
        // at namespace level. Both generators allocate names across every island in one fixed order, so the
        // second takes a suffix — the same suffix in both halves.
        var run = GeneratorDriverFixture.Run(
            [
                ("/src/Shop/Data.cs",
                    "namespace Shop; public sealed partial class Data : Rask.External.ReactComponent { protected override string Module => \"data-lib\"; }"),
                ("/src/Shop/DataGrid.cs",
                    "namespace Shop; public sealed partial class DataGrid : Rask.External.ReactComponent { protected override string Module => \"grid-lib\"; }"),
                ("/src/Shop/Page.cs",
                    """
                    namespace Shop;

                    public sealed partial class Page : Rask.Core.Component
                    {
                        protected override Rask.Core.Component? Render() =>
                            Div[Data.GridMode(DataGridMode.A), DataGrid.Mode(DataGridMode2.B)];
                    }
                    """),
            ],
            [new ExternalGenerator(), new ComponentFactoryGenerator()],
            [
                ("/src/Shop/Data.props.json", EnumSnapshot("data-lib", "gridMode")),
                ("/src/Shop/DataGrid.props.json", EnumSnapshot("grid-lib", "mode")),
            ]);

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(run.GeneratedCompileErrors());
    }

    [Fact]
    public void A_hand_declared_callback_forwards_nothing_when_the_package_passes_only_an_event()
    {
        // Forwarding the event is exactly the failure $a exists to prevent: it holds `view: window`, so the host's
        // JSON.stringify throws and the call never reaches C#.
        const string island =
            """
            namespace Shop;

            public sealed partial class MuiButton : Rask.External.ReactComponent
            {
                protected override string Module => "@mui/material/Button";

                /// <summary>Clicked.</summary>
                public Rask.Core.Callback<string>? OnClick { get; set; }
            }
            """;

        var generated = Run(island, Snapshot).GeneratedSource("MuiButton.External");

        var onClick = generated.Substring(generated.IndexOf("WritePropertyName(\"onClick\")", StringComparison.Ordinal));
        var start = onClick.IndexOf("WriteStartArray(\"$a\")", StringComparison.Ordinal);
        var end = onClick.IndexOf("WriteEndArray()", start, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteNumberValue", onClick.Substring(start, end - start), StringComparison.Ordinal);
    }

    private static string EnumSnapshot(string module, string prop) =>
        $$"""
          {
            "schema": 1, "runtime": "react", "module": "{{module}}", "export": "default",
            "props": [ { "name": "{{prop}}", "type": { "kind": "enum", "base": "string", "values": ["a", "b"] } } ]
          }
          """;

    private static string PropSnapshot(string name, string type, string? doc = null) =>
        $$"""
          {
            "schema": 1, "runtime": "react", "module": "@mui/material/Button", "export": "default",
            "props": [ { "name": "{{name}}", {{(doc is null ? string.Empty : $"\"doc\": \"{doc}\",")}} "type": {{type}} } ]
          }
          """;

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static GeneratorRun Run(string island, string? snapshot, string? host = null)
    {
        var sources = new List<(string, string)> { ("/src/Shop/MuiButton.cs", island) };
        if (host is not null)
        {
            sources.Add(("/src/Shop/Page.cs", host));
        }

        return GeneratorDriverFixture.Run(
            sources.ToArray(),
            [new ExternalGenerator(), new ComponentFactoryGenerator()],
            snapshot is null ? null : [("/src/Shop/MuiButton.props.json", snapshot)]);
    }
}
