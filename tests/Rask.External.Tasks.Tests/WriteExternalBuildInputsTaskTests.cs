using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Rask.External.Tasks.Tests;

/// <summary>
///     Pins that every refusal of <see cref="WriteExternalBuildInputsTask" /> is a CODED build error whose message
///     starts <c>Rask islands:</c> and names its fix — an error logged without a code cannot be looked up.
/// </summary>
public sealed class WriteExternalBuildInputsTaskTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rask-build-inputs").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Two_files_registering_one_island_name_are_refused_as_RASKISLAND011()
    {
        var (task, engine) = Inputs(Island("a/Chart.tsx", "react"), Island("b/Chart.tsx", "react"));

        var succeeded = task.Execute();

        Assert.False(succeeded);
        var error = Assert.Single(engine.Errors);
        Assert.Equal("RASKISLAND011", error.Code);
        Assert.StartsWith("Rask islands:", error.Message, StringComparison.Ordinal);
        Assert.Contains("Rename one of the files", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_runtime_with_no_adapter_is_refused_as_RASKISLAND012_naming_the_ones_there_are()
    {
        var (task, engine) = Inputs(Island("Chart.vue", "vue3"));

        var succeeded = task.Execute();

        Assert.False(succeeded);
        var error = Assert.Single(engine.Errors);
        Assert.Equal("RASKISLAND012", error.Code);
        Assert.Contains("vue3", error.Message, StringComparison.Ordinal);
        Assert.Contains("Use one of:", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dev_server_url_with_a_path_is_refused_as_RASKISLAND013()
    {
        var (task, engine) = Inputs(Island("Chart.tsx", "react"));
        task.DevServerUrl = "http://localhost:5174/islands";

        var succeeded = task.Execute();

        Assert.False(succeeded);
        var error = Assert.Single(engine.Errors);
        Assert.Equal("RASKISLAND013", error.Code);
        Assert.Contains("http://localhost:5174", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void React_beside_preact_is_refused_as_RASKISLAND014()
    {
        var (task, engine) = Inputs(Island("a/Chart.tsx", "react"), Island("b/Gauge.tsx", "preact"));

        var succeeded = task.Execute();

        Assert.False(succeeded);
        var error = Assert.Single(engine.Errors);
        Assert.Equal("RASKISLAND014", error.Code);
        Assert.Contains("Pick one", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_assembly_whose_runtimes_cannot_be_read_is_a_coded_warning_and_the_build_goes_on()
    {
        var (task, engine) = Inputs(Island("Chart.tsx", "react"));
        task.AssemblyPath = Path.Combine(_root, "NotAnAssembly.dll");
        File.WriteAllText(task.AssemblyPath, "not a PE file");

        var succeeded = task.Execute();

        Assert.True(succeeded);
        var warning = Assert.Single(engine.Warnings);
        Assert.Equal("RASKISLAND018", warning.Code);
        Assert.Contains("dotnet build --no-incremental", warning.Message, StringComparison.Ordinal);
    }

    private (WriteExternalBuildInputsTask Task, RecordingEngine Engine) Inputs(params ITaskItem[] islands)
    {
        var engine = new RecordingEngine();
        var task = new WriteExternalBuildInputsTask
        {
            BuildEngine = engine,
            Islands = islands,
            IntermediateDirectory = Path.Combine(_root, "obj"),
            AdapterDirectory = Path.Combine(_root, "obj", "rask"),
            OutputDirectory = Path.Combine(_root, "wwwroot", "_rask", "external"),
            ManifestPath = Path.Combine(_root, "wwwroot", "_rask", "external", "manifest.json"),
            PublicBase = "/_rask/external/",
        };

        return (task, engine);
    }

    private TaskItem Island(string relativePath, string runtime)
    {
        var item = new TaskItem(Path.Combine(_root, relativePath));
        item.SetMetadata("Runtime", runtime);
        return item;
    }
}
