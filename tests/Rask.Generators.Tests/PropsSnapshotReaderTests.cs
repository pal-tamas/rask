using Rask.Generators.External.PackageIslands;

namespace Rask.Generators.Tests;

/// <summary>
///     Covers <see cref="PropsSnapshotReader" />: a committed props snapshot is read tolerantly where that is
///     safe, strictly where it is not, and never by throwing.
/// </summary>
public class PropsSnapshotReaderTests
{
    private const string Sample =
        """
        {
          "schema": 1,
          "runtime": "react",
          "module": "@mui/material/Button",
          "export": "default",
          "package": { "name": "@mui/material", "version": "7.3.1" },
          "tag": null,
          "content": "node",
          "props": [
            { "name": "color", "required": false, "doc": "The color.", "default": "'primary'",
              "type": { "kind": "enum", "base": "string", "values": ["error", "inherit", "primary"], "open": true } },
            { "name": "disabled", "type": { "kind": "boolean" } },
            { "name": "onClick",
              "type": { "kind": "callback", "args": [ { "name": "event", "type": { "kind": "event", "name": "MouseEvent" } } ] } }
          ],
          "skipped": [ { "name": "children", "reason": "node" }, { "name": "sx", "reason": "unsupported", "detail": "SxProps<Theme>" } ],
          "types": { "ButtonClasses": { "kind": "object", "members": [ { "name": "root", "required": true, "type": { "kind": "string" } } ] } }
        }
        """;

    [Fact]
    public void The_documented_sample_reads_in_full()
    {
        var snapshot = PropsSnapshotReader.Read("/src/MuiButton.props.json", Sample);

        Assert.Null(snapshot.Defect);
        Assert.Equal(1, snapshot.Schema);
        Assert.Equal("react", snapshot.Runtime);
        Assert.Equal("@mui/material/Button", snapshot.Module);
        Assert.Equal("default", snapshot.Export);
        Assert.Equal("7.3.1", snapshot.PackageVersion);
        Assert.Equal("node", snapshot.Content);
        Assert.Equal(3, snapshot.Props.Count);
        Assert.Equal(2, snapshot.Skipped.Count);

        var color = snapshot.Props[0];
        Assert.Equal("enum", color.Type.Kind);
        Assert.True(color.Type.Open);
        Assert.Equal(["error", "inherit", "primary"], color.Type.Values.Select(v => v.Text));
        Assert.Equal("'primary'", color.Default);

        var onClick = snapshot.Props[2];
        Assert.Equal("callback", onClick.Type.Kind);
        Assert.Equal("event", Assert.Single(onClick.Type.Args).Type.Kind);

        Assert.NotNull(snapshot.NamedType("ButtonClasses"));
    }

    [Fact]
    public void Two_reads_of_one_file_are_equal()
    {
        // What the incremental pipeline relies on: an unchanged snapshot must compare equal, or every output
        // built from it regenerates on every keystroke.
        Assert.Equal(
            PropsSnapshotReader.Read("/a.props.json", Sample),
            PropsSnapshotReader.Read("/a.props.json", Sample));
    }

    [Fact]
    public void An_unknown_field_is_ignored()
    {
        var snapshot = PropsSnapshotReader.Read(
            "/a.props.json",
            """{ "schema": 1, "runtime": "react", "module": "x", "future": { "a": [1, 2] }, "props": [] }""");

        Assert.Null(snapshot.Defect);
    }

    [Fact]
    public void An_unknown_kind_is_kept_for_the_resolver_to_report()
    {
        var snapshot = PropsSnapshotReader.Read(
            "/a.props.json",
            """{ "schema": 1, "runtime": "react", "module": "x", "props": [ { "name": "p", "type": { "kind": "tuple" } } ] }""");

        Assert.Null(snapshot.Defect);
        Assert.Equal("tuple", Assert.Single(snapshot.Props).Type.Kind);
    }

    [Theory]
    [InlineData("1.3")]
    [InlineData("1")]
    public void A_minor_schema_version_is_read_as_its_major(string version)
    {
        var snapshot = PropsSnapshotReader.Read(
            "/a.props.json",
            $$"""{ "schema": "{{version}}", "runtime": "react", "module": "x", "props": [] }""");

        Assert.Null(snapshot.Defect);
        Assert.Equal(1, snapshot.Schema);
    }

    [Fact]
    public void A_newer_schema_is_a_defect_that_names_both_versions()
    {
        var snapshot = PropsSnapshotReader.Read(
            "/a.props.json",
            """{ "schema": 2, "runtime": "react", "module": "x", "props": [] }""");

        Assert.NotNull(snapshot.Defect);
        Assert.Contains("schema 2", snapshot.Defect, StringComparison.Ordinal);
        Assert.Contains("schema 1", snapshot.Defect, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "runtime": "react", "module": "x", "props": [] }""")]
    [InlineData("""{ "schema": 0, "runtime": "react", "module": "x", "props": [] }""")]
    [InlineData("""{ "schema": 1, "module": "x", "props": [] }""")]
    [InlineData("""{ "schema": 1, "runtime": "react", "module": "x" }""")]
    [InlineData("""[]""")]
    [InlineData("")]
    public void A_snapshot_missing_what_it_must_say_is_a_defect(string text)
    {
        Assert.NotNull(PropsSnapshotReader.Read("/a.props.json", text).Defect);
    }

    [Fact]
    public void Malformed_json_is_a_defect_with_a_line_and_column_not_an_exception()
    {
        var snapshot = PropsSnapshotReader.Read(
            "/a.props.json",
            "{\n  \"schema\": 1,\n  \"props\": [ \"unterminated\n]\n}");

        Assert.NotNull(snapshot.Defect);
        Assert.Equal(3, snapshot.DefectLine);
        Assert.True(snapshot.DefectColumn > 1);
    }

    [Fact]
    public void Nesting_deeper_than_the_cap_is_refused_rather_than_overflowing_the_stack()
    {
        var text = new string('[', 10_000) + new string(']', 10_000);

        var snapshot = PropsSnapshotReader.Read("/a.props.json", text);

        Assert.NotNull(snapshot.Defect);
        Assert.Contains("nested", snapshot.Defect, StringComparison.Ordinal);
    }

    [Fact]
    public void Escapes_are_unescaped()
    {
        var snapshot = PropsSnapshotReader.Read(
            "/a.props.json",
            """{ "schema": 1, "runtime": "react", "module": "x", "props": [ { "name": "ab\"c", "type": { "kind": "string" } } ] }""");

        Assert.Equal("ab\"c", Assert.Single(snapshot.Props).Name);
    }
}
