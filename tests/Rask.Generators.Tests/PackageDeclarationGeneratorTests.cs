using Microsoft.CodeAnalysis;
using Rask.Generators.External;

namespace Rask.Generators.Tests;

/// <summary>
///     A package declaration — <c>Mui : ReactPackage</c> with <c>Exports =&gt; ["Button", "Card"]</c> — is one island
///     per export, reached as <c>Mui.Button</c>: the island generator declares the class, the factory generator its
///     chain, and the bare <c>Button</c> stays the HTML tag.
/// </summary>
public sealed class PackageDeclarationGeneratorTests
{
    private const string Declaration =
        """
        namespace Shop;

        public sealed partial class Mui : Rask.External.ReactPackage
        {
            protected override string Module => "@mui/material";
            protected override string[] Exports => ["Button", "Card"];
        }
        """;

    private const string ButtonSnapshot =
        """
        {
          "schema": 1, "runtime": "react", "module": "@mui/material", "export": "Button",
          "package": { "name": "@mui/material", "version": "7.3.1" },
          "content": "node",
          "props": [
            { "name": "variant", "type": { "kind": "enum", "base": "string", "values": ["text", "contained"] } },
            { "name": "onClick",
              "type": { "kind": "callback", "args": [ { "name": "event", "type": { "kind": "event", "name": "MouseEvent" } } ] } }
          ]
        }
        """;

    private const string CardSnapshot =
        """
        {
          "schema": 1, "runtime": "react", "module": "@mui/material", "export": "Card",
          "package": { "name": "@mui/material", "version": "7.3.1" },
          "content": "node",
          "props": [ { "name": "raised", "type": { "kind": "boolean" } } ]
        }
        """;

    private const string Page =
        """
        namespace Shop;

        public sealed partial class Page : Rask.Core.Component
        {
            protected override Rask.Core.Component? Render() =>
                Mui.Card.Raised(true)[
                    Mui.Button.Variant(MuiButtonVariant.Contained).OnClick(() => { })["Save"]
                ];
        }
        """;

    [Fact]
    public void Each_export_is_an_island_reached_through_the_declaration_and_everything_compiles()
    {
        var run = Run(Declaration, Page);

        Assert.DoesNotContain(run.Diagnostics, d => d.Severity >= DiagnosticSeverity.Warning);
        Assert.Empty(run.GeneratedCompileErrors());

        var button = run.GeneratedSource("MuiButton.External");
        Assert.Contains("sealed partial class MuiButton : global::Rask.External.ReactComponent", button, StringComparison.Ordinal);
        Assert.Contains("protected override string Module => \"@mui/material\";", button, StringComparison.Ordinal);
        Assert.Contains("protected override string Export => \"Button\";", button, StringComparison.Ordinal);
        Assert.Contains("RaskChainGroup(typeof(global::Shop.Mui), \"Button\")", button, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bare_tag_keeps_its_name_and_the_island_has_no_bare_entry()
    {
        const string page =
            """
            namespace Shop;

            public sealed partial class Page : Rask.Core.Component
            {
                protected override Rask.Core.Component? Render() => Button["plain"];
            }
            """;

        var run = Run(Declaration, page);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain(
            run.RunResult.Results.SelectMany(r => r.GeneratedSources),
            s => s.SourceText.ToString().Contains(" MuiButton => ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Button", "Button")]
    [InlineData("Switch.Root", "SwitchRoot")]
    [InlineData("sl-switch", "SlSwitch")]
    [InlineData("x'};alert(1)//", "")]
    public void An_export_is_named_for_the_call_site(string export, string member)
    {
        Assert.Equal(member, External.PackageIslands.PackageDeclarations.MemberName(export));
    }

    [Fact]
    public void A_computed_exports_list_is_RASK059_naming_Exports()
    {
        const string declaration =
            """
            namespace Shop;

            public sealed partial class Mui : Rask.External.ReactPackage
            {
                private static readonly string[] All = ["Button"];
                protected override string Module => "@mui/material";
                protected override string[] Exports => All;
            }
            """;

        var diagnostic = Assert.Single(Distinct(Run(declaration), "RASK059"));
        Assert.Contains("overrides Exports", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declaration_that_is_not_partial_is_RASK056()
    {
        const string declaration =
            """
            namespace Shop;

            public sealed class Mui : Rask.External.ReactPackage
            {
                protected override string Module => "@mui/material";
                protected override string[] Exports => ["Button"];
            }
            """;

        Assert.Contains(Run(declaration).Diagnostics, d => d.Id == "RASK056");
    }

    [Fact]
    public void An_export_with_no_snapshot_is_RASK077_at_the_exports_line()
    {
        var run = GeneratorDriverFixture.Run(
            [("/src/Shop/Mui.cs", Declaration)],
            [new ExternalGenerator(), new ComponentFactoryGenerator()],
            [("/src/Shop/MuiButton.props.json", ButtonSnapshot)]);

        var missing = Assert.Single(Distinct(run, "RASK077"));
        Assert.Contains("MuiCard", missing, StringComparison.Ordinal);
    }

    // The fixture drives the generators more than once, so one report can appear twice.
    private static IEnumerable<string> Distinct(GeneratorRun run, string id) =>
        run.Diagnostics.Where(d => d.Id == id).Select(static d => d.GetMessage()).Distinct();

    private static GeneratorRun Run(string declaration, string? page = null)
    {
        var sources = new List<(string, string)> { ("/src/Shop/Mui.cs", declaration) };
        if (page is not null)
        {
            sources.Add(("/src/Shop/Page.cs", page));
        }

        return GeneratorDriverFixture.Run(
            sources.ToArray(),
            [new ExternalGenerator(), new ComponentFactoryGenerator()],
            [("/src/Shop/MuiButton.props.json", ButtonSnapshot), ("/src/Shop/MuiCard.props.json", CardSnapshot)]);
    }
}
