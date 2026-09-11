namespace Rask.External.Tasks.Tests;

/// <summary>
///     Covers <see cref="ExternalPackageScan" />: the islands whose constant <c>Module</c> names a package are
///     found in the source before the compile, in every form the island generator accepts, and nothing a comment
///     or a string contains can fake or hide one.
/// </summary>
public sealed class ExternalPackageScanTests
{
    private static readonly Dictionary<string, string> Islands = new(StringComparer.Ordinal)
    {
        ["MuiButton"] = "react",
        ["Toggle"] = "vue",
    };

    [Theory]
    [InlineData("protected override string Module => \"@mui/material/Button\";")]
    [InlineData("protected override string Module { get => \"@mui/material/Button\"; }")]
    [InlineData("protected override string Module { get { return \"@mui/material/Button\"; } }")]
    [InlineData("protected override string Module { get; } = \"@mui/material/Button\";")]
    [InlineData("protected override string Module => @\"@mui/material/Button\";")]
    public void Every_form_the_generator_accepts_is_found(string member)
    {
        var island = Assert.Single(Scan($"public sealed partial class MuiButton : ReactComponent {{ {member} }}"));

        Assert.Equal("MuiButton", island.Name);
        Assert.Equal("react", island.Runtime);
        Assert.Equal("@mui/material/Button", island.Module);
    }

    [Fact]
    public void A_named_export_is_kept_whole_for_the_caller_to_split()
    {
        var island = Assert.Single(Scan(
            "partial class MuiButton : ReactComponent { protected override string Module => \"@mui/material#Button\"; }"));

        Assert.Equal("@mui/material#Button", island.Module);
    }

    [Fact]
    public void Double_slashes_inside_the_string_are_not_a_comment()
    {
        var island = Assert.Single(Scan(
            "partial class MuiButton : ReactComponent { protected override string Module => \"cdn-pkg//deep/Button\"; }"));

        Assert.Equal("cdn-pkg//deep/Button", island.Module);
    }

    [Fact]
    public void An_override_inside_a_comment_declares_nothing()
    {
        Assert.Empty(Scan(
            """
            partial class MuiButton : ReactComponent
            {
                // protected override string Module => "@mui/material/Button";
                /* protected override string Module => "@mui/material/Button"; */
            }
            """));
    }

    [Fact]
    public void A_file_module_is_not_a_package_island()
    {
        Assert.Empty(Scan(
            "partial class MuiButton : ReactComponent { protected override string Module => \"./widgets/Button.tsx\"; }"));
    }

    [Fact]
    public void A_computed_module_is_left_to_the_generator_to_report()
    {
        Assert.Empty(Scan(
            "partial class MuiButton : ReactComponent { protected override string Module => \"@mui/\" + Name; }"));
    }

    [Fact]
    public void A_class_that_is_not_an_island_is_ignored()
    {
        Assert.Empty(Scan(
            "partial class Other : ReactComponent { protected override string Module => \"@mui/material/Button\"; }"));
    }

    [Fact]
    public void A_nested_class_override_belongs_to_the_nested_class()
    {
        var islands = Scan(
            """
            partial class MuiButton : ReactComponent
            {
                private sealed class Helper : Base
                {
                    protected override string Module => "wrong-pkg";
                }

                protected override string Module => "@mui/material/Button";
            }
            """);

        Assert.Equal("@mui/material/Button", Assert.Single(islands).Module);
    }

    [Fact]
    public void Braces_inside_strings_do_not_end_the_class_early()
    {
        var island = Assert.Single(Scan(
            """
            partial class MuiButton : ReactComponent
            {
                private const string Template = "{ } }}";
                protected override string Module => "@mui/material/Button";
            }
            """));

        Assert.Equal("@mui/material/Button", island.Module);
    }

    [Fact]
    public void A_verbatim_string_opening_with_an_escaped_quote_is_not_a_raw_string()
    {
        // @"""quoted"" text" opens with three quotes, which is a raw string only without the '@'. Read as raw, no
        // closing run is found, the rest of the file is blanked, and the override below it is never seen.
        var island = Assert.Single(Scan(
            "partial class MuiButton : ReactComponent\n{\n    private const string Hint = @\"\"\"quoted\"\" text\";\n"
            + "    protected override string Module => \"@mui/material/Button\";\n}\n"));

        Assert.Equal("@mui/material/Button", island.Module);
    }

    [Fact]
    public void A_declaration_with_no_body_does_not_claim_the_next_class_override()
    {
        // A base list is no longer required, so the body is found by the next brace — which, after a bodiless
        // declaration, belongs to another class. Only the class that owns the override may claim it.
        var islands = Scan(
            """
            partial class MuiButton(int size);

            partial class Toggle : VueComponent
            {
                protected override string Module => "@acme/toggle";
            }
            """);

        var island = Assert.Single(islands);
        Assert.Equal("Toggle", island.Name);
        Assert.Equal("vue", island.Runtime);
    }

    [Fact]
    public void The_line_of_the_override_is_recorded()
    {
        var island = Assert.Single(Scan(
            "namespace Shop;\n\npartial class MuiButton : ReactComponent\n{\n    protected override string Module => \"@mui/material/Button\";\n}\n"));

        Assert.Equal(5, island.Line);
    }

    [Fact]
    public void The_snapshot_sits_beside_the_file_holding_the_override()
    {
        var island = new ScannedPackageIsland("MuiButton", "react", "@mui/material/Button", "/src/Shop/MuiButton.cs", 3);

        Assert.Equal(Path.Combine("/src/Shop", "MuiButton.props.json"), island.SnapshotPath);
    }

    [Fact]
    public void Every_file_is_scanned_and_the_part_with_the_override_wins()
    {
        var root = Directory.CreateTempSubdirectory("rask-package-scan").FullName;
        try
        {
            var a = Path.Combine(root, "MuiButton.cs");
            var b = Path.Combine(root, "Props", "MuiButton.Props.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(b)!);
            File.WriteAllText(a, "public sealed partial class MuiButton : ReactComponent { }");
            File.WriteAllText(b, "partial class MuiButton { protected override string Module => \"@mui/material/Button\"; }");

            var runtimes = new Dictionary<string, string>(StringComparer.Ordinal) { ["MuiButton"] = "react" };
            var island = Assert.Single(ExternalPackageScan.PackageIslands([a, b], runtimes));

            Assert.Equal(b, island.DeclaringFile);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("@mui/material/Button", true)]
    [InlineData("react-colorful", true)]
    [InlineData("./Chart.tsx", false)]
    [InlineData("../Chart.tsx", false)]
    [InlineData("/abs/Chart.tsx", false)]
    [InlineData("#internal", false)]
    [InlineData("https://cdn/x.js", false)]
    [InlineData("C:/x.tsx", false)]
    [InlineData("", false)]
    public void A_specifier_is_a_package_only_when_it_is_bare(string module, bool bare)
    {
        Assert.Equal(bare, ExternalPackageSpecifier.IsBare(module));
    }

    [Theory]
    [InlineData("@mui/material#Button", "@mui/material", "Button")]
    [InlineData("@mui/material/Button", "@mui/material/Button", "default")]
    [InlineData("bits-ui#Switch.Root", "bits-ui", "Switch.Root")]
    [InlineData("pkg#", "pkg#", "default")]
    public void A_specifier_splits_into_module_and_export(string module, string specifier, string export)
    {
        Assert.Equal((specifier, export), ExternalPackageSpecifier.Split(module));
    }

    [Theory]
    [InlineData("@mui/material/Button", "@mui/material")]
    [InlineData("react-colorful", "react-colorful")]
    [InlineData("lodash/debounce", "lodash")]
    public void The_package_name_is_the_scope_and_name(string specifier, string package)
    {
        Assert.Equal(package, ExternalPackageSpecifier.PackageName(specifier));
    }

    [Theory]
    [InlineData("Button", true)]
    [InlineData("default", true)]
    [InlineData("$el", true)]
    [InlineData("a b", false)]
    [InlineData("x'};alert(1);//", false)]
    [InlineData("1st", false)]
    [InlineData("Switch.Root", true)]
    [InlineData("Menu.Item.Label", true)]
    [InlineData("a..b", false)]
    [InlineData(".Root", false)]
    [InlineData("Switch.", false)]
    [InlineData("default.Root", true)]
    public void Only_an_identifier_or_a_dotted_path_of_them_can_be_written_into_an_import(string export, bool valid)
    {
        Assert.Equal(valid, ExternalPackageSpecifier.IsValidExport(export));
    }

    [Theory]
    [InlineData("sl-switch", "lit", true)]
    [InlineData("fx-switch", "react", false)]
    [InlineData("Sl-switch", "lit", false)]
    [InlineData("slswitch", "lit", true)]
    [InlineData("sl-switch'", "lit", false)]
    [InlineData("-switch", "lit", false)]
    [InlineData("Switch.Root", "lit", true)]
    public void Only_a_lit_island_may_name_the_tag_its_module_registers(string export, string runtime, bool valid)
    {
        // "slswitch" is valid for Lit as the identifier it is, not as a tag.
        Assert.Equal(valid, ExternalPackageSpecifier.IsValidExport(export, runtime));
    }

    private static List<ScannedPackageIsland> Scan(string text) =>
        ExternalPackageScan.Scan(text, "/src/MuiButton.cs", Islands).ToList();
}
