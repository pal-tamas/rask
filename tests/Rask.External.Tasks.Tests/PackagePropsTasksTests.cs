using System.Text.Json;
using Microsoft.Build.Utilities;

namespace Rask.External.Tasks.Tests;

/// <summary>
///     Covers the build tasks around a package island's props snapshot: finding the islands, writing the
///     extractor's request, syncing what it extracted, and checking the committed files when nothing can be
///     extracted.
/// </summary>
public sealed class PackagePropsTasksTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rask-package-props").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_package_island_is_found_with_everything_its_diagnostics_need()
    {
        var source = Write("Shop/MuiButton.cs",
            "namespace Shop;\npublic sealed partial class MuiButton : ReactComponent\n{\n    protected override string Module => \"@mui/material/Button\";\n}\n");

        var engine = new RecordingEngine();
        var task = new FindExternalPackageIslandsTask { BuildEngine = engine, Sources = [new TaskItem(source)] };

        Assert.True(task.Execute());
        var island = Assert.Single(task.PackageIslands);
        Assert.Equal(Path.Combine(_root, "Shop", "MuiButton.props.json"), island.ItemSpec);
        Assert.Equal("MuiButton", island.GetMetadata("IslandName"));
        Assert.Equal("react", island.GetMetadata("Runtime"));
        Assert.Equal("@mui/material/Button", island.GetMetadata("PackageModule"));
        Assert.Equal(source, island.GetMetadata("DeclaringFile"));
        Assert.Equal("4", island.GetMetadata("ModuleLine"));
    }

    [Fact]
    public void A_lit_island_may_name_the_tag_its_define_module_registers()
    {
        // A define module often exports nothing at all: the tag is what mounts the element.
        var source = Write("FxSwitch.cs",
            "public sealed partial class FxSwitch : LitComponent { protected override string Module => \"fixture-lit/fx-switch.js#fx-switch\"; }");

        var engine = new RecordingEngine();
        var task = new FindExternalPackageIslandsTask { BuildEngine = engine, Sources = [new TaskItem(source)] };

        Assert.True(task.Execute());
        Assert.Empty(engine.Errors);
        Assert.Equal("fixture-lit/fx-switch.js#fx-switch", Assert.Single(task.PackageIslands).GetMetadata("PackageModule"));
    }

    [Fact]
    public void A_tag_is_no_export_for_any_runtime_but_lit()
    {
        // `#fx-switch` would be written into a React entry as `import { fx-switch as Component }`.
        var source = Write("FxSwitch.cs",
            "public sealed partial class FxSwitch : ReactComponent { protected override string Module => \"fixture-lit/fx-switch.js#fx-switch\"; }");

        var engine = new RecordingEngine();
        var task = new FindExternalPackageIslandsTask { BuildEngine = engine, Sources = [new TaskItem(source)] };

        Assert.False(task.Execute());
        Assert.Equal("RASKISLAND005", Assert.Single(engine.Errors).Code);
    }

    [Fact]
    public void A_relative_source_is_spelled_from_the_project_directory_not_the_current_one()
    {
        // MSBuild hands @(Compile) over relative. Resolved against the process's current directory, macOS spells a
        // /var/folders project as /private/var/folders, so the snapshot this names and the one the evaluation glob
        // found compare unequal and reach the compile twice. The project directory's own spelling is kept.
        Write("Shop/MuiButton.cs",
            "namespace Shop;\npublic sealed partial class MuiButton : ReactComponent\n{\n    protected override string Module => \"@mui/material/Button\";\n}\n");

        var task = new FindExternalPackageIslandsTask
        {
            BuildEngine = new RecordingEngine(),
            Sources = [new TaskItem(Path.Combine("Shop", "MuiButton.cs"))],
            ProjectDirectory = _root,
        };

        Assert.True(task.Execute());
        var island = Assert.Single(task.PackageIslands);
        Assert.Equal(Path.Combine(_root, "Shop", "MuiButton.props.json"), island.ItemSpec);
        Assert.Equal(Path.Combine(_root, "Shop", "MuiButton.cs"), island.GetMetadata("DeclaringFile"));
    }

    [Fact]
    public void A_package_module_beside_a_front_end_file_is_refused()
    {
        var source = Write("MuiButton.cs",
            "public sealed partial class MuiButton : ReactComponent { protected override string Module => \"@mui/material/Button\"; }");
        Write("MuiButton.tsx", "export default function MuiButton() { return null }");

        var engine = new RecordingEngine();
        var task = new FindExternalPackageIslandsTask { BuildEngine = engine, Sources = [new TaskItem(source)] };

        Assert.False(task.Execute());
        var error = Assert.Single(engine.Errors);
        Assert.Equal("RASKISLAND005", error.Code);
        Assert.Contains("MuiButton.tsx", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_export_that_is_not_an_identifier_is_refused_before_it_reaches_generated_javascript()
    {
        var source = Write("MuiButton.cs",
            "public sealed partial class MuiButton : ReactComponent { protected override string Module => \"pkg#x'};alert(1);//\"; }");

        var engine = new RecordingEngine();
        var task = new FindExternalPackageIslandsTask { BuildEngine = engine, Sources = [new TaskItem(source)] };

        Assert.False(task.Execute());
        Assert.Equal("RASKISLAND005", Assert.Single(engine.Errors).Code);
    }

    [Fact]
    public void The_request_names_the_typescript_the_project_and_each_island()
    {
        var requestPath = Path.Combine(_root, "props", "request.json");
        var task = new WriteExternalPropsRequestTask
        {
            BuildEngine = new RecordingEngine(),
            Islands = [Island("MuiButton", "@mui/material#Button")],
            TypeScriptPath = "/cache/typescript/lib/typescript.js",
            ProjectDirectory = _root,
            OutputDirectory = Path.Combine(_root, "props"),
            RequestPath = requestPath,
        };

        Assert.True(task.Execute());

        using var request = JsonDocument.Parse(File.ReadAllText(requestPath));
        Assert.Equal("/cache/typescript/lib/typescript.js", request.RootElement.GetProperty("typescript").GetString());
        var island = Assert.Single(request.RootElement.GetProperty("islands").EnumerateArray());
        Assert.Equal("@mui/material", island.GetProperty("module").GetString());
        Assert.Equal("Button", island.GetProperty("export").GetString());
        Assert.Equal(Path.Combine(_root, "props", "MuiButton.props.json"), island.GetProperty("out").GetString());
    }

    [Fact]
    public void A_dotted_export_with_an_empty_segment_is_refused_before_the_request_is_written()
    {
        // The request's export reaches the extractor's probe as generated TypeScript, so it is held to the same rule
        // the entry module is: identifiers separated by dots, nothing empty.
        var requestPath = Path.Combine(_root, "props", "request.json");
        var engine = new RecordingEngine();
        var task = new WriteExternalPropsRequestTask
        {
            BuildEngine = engine,
            Islands = [Island("SwitchRoot", "bits-ui#Switch.")],
            TypeScriptPath = "/cache/typescript/lib/typescript.js",
            ProjectDirectory = _root,
            OutputDirectory = Path.Combine(_root, "props"),
            RequestPath = requestPath,
        };

        Assert.False(task.Execute());
        Assert.Equal("RASKISLAND005", Assert.Single(engine.Errors).Code);
        Assert.False(File.Exists(requestPath));
    }

    [Fact]
    public void A_first_snapshot_is_written_and_announced()
    {
        var (task, engine, snapshot) = Sync(extracted: Snapshot("7.3.1"), committed: null);

        Assert.True(task.Execute());
        Assert.Equal(Snapshot("7.3.1"), File.ReadAllText(snapshot));
        Assert.Contains(engine.Messages, m => m.Contains("wrote MuiButton.props.json", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unchanged_snapshot_is_not_rewritten()
    {
        var (task, _, snapshot) = Sync(extracted: Snapshot("7.3.1"), committed: Snapshot("7.3.1"));
        var before = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(snapshot, before);

        Assert.True(task.Execute());
        Assert.Equal(before, File.GetLastWriteTimeUtc(snapshot));
    }

    [Fact]
    public void A_snapshot_checked_out_with_crlf_is_not_drift()
    {
        var (task, engine, _) = Sync(extracted: Snapshot("7.3.1"), committed: Snapshot("7.3.1").Replace("\n", "\r\n"), locked: true);

        Assert.True(task.Execute());
        Assert.Empty(engine.Errors);
    }

    [Fact]
    public void A_changed_snapshot_is_refreshed_naming_the_version_move()
    {
        var (task, engine, snapshot) = Sync(extracted: Snapshot("7.4.0"), committed: Snapshot("7.3.1"));

        Assert.True(task.Execute());
        Assert.Equal(Snapshot("7.4.0"), File.ReadAllText(snapshot));
        Assert.Contains(engine.Messages, m => m.Contains("7.3.1 → 7.4.0", StringComparison.Ordinal));
    }

    [Fact]
    public void A_locked_build_refuses_drift_and_leaves_the_file_alone()
    {
        var (task, engine, snapshot) = Sync(extracted: Snapshot("7.4.0"), committed: Snapshot("7.3.1"), locked: true);

        Assert.False(task.Execute());
        var error = Assert.Single(engine.Errors);
        Assert.Equal("RASKISLAND008", error.Code);
        Assert.Equal(Snapshot("7.3.1"), File.ReadAllText(snapshot));
    }

    [Fact]
    public void An_island_the_extractor_could_not_read_is_reported_at_its_module_line()
    {
        var (task, engine, _) = Sync(extracted: null, committed: null,
            result: """[ { "name": "MuiButton", "ok": false, "code": "module-not-found", "message": "'@mui/material/Button' could not be resolved." } ]""");

        Assert.False(task.Execute());
        var error = Assert.Single(engine.Errors);
        Assert.Equal("RASKISLAND007", error.Code);
        Assert.Equal("/src/MuiButton.cs", error.File);
        Assert.Equal(4, error.LineNumber);
        Assert.Contains("module-not-found", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_snapshot_with_nothing_able_to_extract_it_is_an_error()
    {
        var engine = new RecordingEngine();
        var task = new CheckExternalPropsSnapshotsTask
        {
            BuildEngine = engine,
            Islands = [Island("MuiButton", "@mui/material/Button")],
            Reason = "RaskExternalBuild=false",
        };

        Assert.False(task.Execute());
        var error = Assert.Single(engine.Errors);
        Assert.Equal("RASKISLAND006", error.Code);
        Assert.Contains("RaskExternalBuild=false", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_snapshot_from_another_version_than_the_lockfile_pins_is_a_warning()
    {
        var item = Island("MuiButton", "@mui/material/Button");
        File.WriteAllText(item.ItemSpec, Snapshot("7.3.1"));
        var lockFile = Write("package-lock.json",
            """{ "lockfileVersion": 3, "packages": { "node_modules/@mui/material": { "version": "7.4.0", "resolved": "x" } } }""");

        var engine = new RecordingEngine();
        var task = new CheckExternalPropsSnapshotsTask { BuildEngine = engine, Islands = [item], LockFile = lockFile };

        Assert.True(task.Execute());
        var warning = Assert.Single(engine.Warnings);
        Assert.Equal("RASKISLAND010", warning.Code);
        Assert.Contains("7.4.0", warning.Message, StringComparison.Ordinal);
    }

    private (SyncExternalPropsSnapshotsTask Task, RecordingEngine Engine, string Snapshot) Sync(
        string? extracted, string? committed, bool locked = false, string? result = null)
    {
        var output = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        File.WriteAllText(Path.Combine(output, "result.json"),
            result ?? """[ { "name": "MuiButton", "ok": true } ]""");
        if (extracted is not null)
        {
            File.WriteAllText(Path.Combine(output, "MuiButton.props.json"), extracted);
        }

        var item = Island("MuiButton", "@mui/material/Button");
        if (committed is not null)
        {
            File.WriteAllText(item.ItemSpec, committed);
        }

        var engine = new RecordingEngine();
        return (new SyncExternalPropsSnapshotsTask
        {
            BuildEngine = engine,
            Islands = [item],
            OutputDirectory = output,
            Locked = locked,
        }, engine, item.ItemSpec);
    }

    private TaskItem Island(string name, string module)
    {
        var item = new TaskItem(Path.Combine(_root, name + ".props.json"));
        item.SetMetadata("IslandName", name);
        item.SetMetadata("Runtime", "react");
        item.SetMetadata("PackageModule", module);
        item.SetMetadata("DeclaringFile", "/src/" + name + ".cs");
        item.SetMetadata("ModuleLine", "4");
        return item;
    }

    private static string Snapshot(string version) =>
        "{\n  \"schema\": 1,\n  \"runtime\": \"react\",\n  \"module\": \"@mui/material/Button\",\n  \"export\": \"default\",\n"
        + $"  \"package\": {{\n    \"name\": \"@mui/material\",\n    \"version\": \"{version}\"\n  }},\n  \"props\": []\n}}\n";

    private string Write(string relative, string contents)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }
}
