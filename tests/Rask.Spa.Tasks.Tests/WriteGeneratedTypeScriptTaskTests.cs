using System.Collections;
using Microsoft.Build.Framework;

namespace Rask.Spa.Tasks.Tests;

/// <summary>
///     The build writes TypeScript into a front end only when the host declares a remote message, and only
///     then asks the front end to be TypeScript.
/// </summary>
public sealed class WriteGeneratedTypeScriptTaskTests : IDisposable
{
    private const string OneQuery = """
        using Rask.Cqrs;

        namespace Shop;

        public sealed record Greeting(string Text);

        public sealed record GetGreeting(string Name) : IQuery<Greeting>;
        """;

    private readonly string _directory = Directory.CreateTempSubdirectory("rask-spa-task-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_host_with_no_remote_message_leaves_the_front_end_alone()
    {
        var assembly = TestCompilation.Emit("namespace Shop; public sealed class Nothing;", _directory);
        var (task, engine) = Task(assembly, typeScriptConfig: "missing.json");

        var succeeded = task.Execute();

        Assert.True(succeeded, string.Join("; ", engine.Errors));
        Assert.False(task.HasContracts);
        Assert.False(Directory.Exists(task.OutputDirectory));
    }

    [Fact]
    public void A_remote_message_is_written_into_a_TypeScript_front_end()
    {
        var assembly = TestCompilation.Emit(OneQuery, _directory);
        File.WriteAllText(Path.Combine(_directory, "tsconfig.json"), "{}");
        var (task, engine) = Task(assembly, typeScriptConfig: "tsconfig.json");

        var succeeded = task.Execute();

        Assert.True(succeeded, string.Join("; ", engine.Errors));
        Assert.True(task.HasContracts);
        Assert.Contains("Greeting", File.ReadAllText(Path.Combine(task.OutputDirectory, "contracts.ts")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(task.OutputDirectory, "messages.ts")));
    }

    [Fact]
    public void A_remote_message_refuses_a_front_end_with_no_TypeScript_configuration()
    {
        var assembly = TestCompilation.Emit(OneQuery, _directory);
        var (task, engine) = Task(assembly, typeScriptConfig: "tsconfig.json");

        var succeeded = task.Execute();

        Assert.False(succeeded);
        Assert.Contains(engine.Errors, error => error.StartsWith("RASKSPA004:", StringComparison.Ordinal));
        Assert.False(Directory.Exists(task.OutputDirectory));
    }

    private (WriteGeneratedTypeScriptTask Task, RecordingBuildEngine Engine) Task(string assembly, string typeScriptConfig)
    {
        var engine = new RecordingBuildEngine();
        var task = new WriteGeneratedTypeScriptTask
        {
            BuildEngine = engine,
            AssemblyPath = assembly,
            OutputDirectory = Path.Combine(_directory, "src", "rask"),
            TypeScriptConfig = Path.Combine(_directory, typeScriptConfig),
        };

        return (task, engine);
    }

    /// <summary>Keeps each error as <c>CODE: message</c>.</summary>
    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => string.Empty;

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add($"{e.Code}: {e.Message}");

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) =>
            false;
    }
}
