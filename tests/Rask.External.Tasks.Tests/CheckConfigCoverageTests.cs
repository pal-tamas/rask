using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Rask.External.Tasks;

namespace Rask.External.Tasks.Tests;

// The prop type-check is the only thing standing between an island's C# props and the front-end code
// that reads them, and every checker in _RaskExternalTypeCheck is gated on `Exists(<its config>)`. So
// "no config was written" and "the check passed" are the same green build, and the difference used to be
// invisible (#943).
//
// What these pin is the CORRECTED rule, which is not the one the issue first proposed. Checking the
// discovered files regardless of what the assembly declared was tried and is wrong: the file list cannot
// identify island code by itself, and this repository holds two counter-examples — a scoped-TypeScript
// Gantt.ts (#938) and the whole client/ tree of each Rask.Example.Meta.* sample. So the skip stays; what
// changes is that it is loud, and that it no longer leaves a stale config behind for a checker to run
// against next build.
public sealed class CheckConfigCoverageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rask-external-checkconfig").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void An_assembly_declaring_no_components_writes_no_check_config()
    {
        // Not a regression to the early return: the point is that nothing is CLAIMED to have been
        // checked. A .tsx beside an assembly that declares no island is as likely to be a meta
        // framework's own page as an island, and it type-checks under a toolchain this config cannot
        // describe.
        var task = NewTask(Front("Chart.tsx"), Front("Panel.vue"), Front("Row.svelte"));

        Assert.True(task.Execute());

        Assert.False(File.Exists(Path.Combine(_root, "obj", "tsconfig.check.json")));
        Assert.False(File.Exists(Path.Combine(_root, "obj", "tsconfig.vue.json")));
        Assert.False(File.Exists(Path.Combine(_root, "obj", "tsconfig.svelte.json")));

        Assert.False(task.HasCheckConfig);
        Assert.False(task.HasVueConfig);
        Assert.False(task.HasSvelteConfig);
    }

    [Fact]
    public void The_skip_is_reported_when_there_are_files_to_check()
    {
        // The half of #943 that matters: the old skip said nothing at all, so a build that checked
        // nothing and a build that checked everything looked identical. Silence is the defect.
        //
        // Normal rather than High, and the difference is the point. Seven projects here legitimately
        // have front-end files and no islands, so High printed a paragraph each on every build — a line
        // that always fires is a line nobody reads. Normal shows under `-v:n`, which is what someone
        // asking "did my islands get checked?" actually runs.
        var engine = new StubEngine();
        var task = NewTask(Front("Chart.tsx"));
        task.BuildEngine = engine;

        Assert.True(task.Execute());

        var said = engine.Messages.Single(m => m.Importance == MessageImportance.Normal);
        Assert.Contains("declares no external components", said.Message, StringComparison.Ordinal);
        Assert.Contains("skipped", said.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_to_check_is_reported_quietly()
    {
        // The counterpart, so the message stays meaningful: a project with no front-end files at all has
        // nothing to report, and must not spend a visible line saying so.
        var engine = new StubEngine();
        var task = NewTask();
        task.BuildEngine = engine;

        Assert.True(task.Execute());

        // <= Normal, not >=: the enum runs High = 0, Normal = 1, Low = 2, so "at least as visible as
        // Normal" is the LOWER value. Written the other way this passed on a Low message and would have
        // let a shouted one through too.
        Assert.DoesNotContain(engine.Messages, m => m.Importance <= MessageImportance.Normal);
    }

    [Fact]
    public void A_stale_config_from_a_previous_build_is_deleted()
    {
        // The issue's second half. The early return skipped this too, so a tsconfig.vue.json written
        // when the project still had a Vue island survived its removal — and the targets decide whether
        // to run a checker by whether its config EXISTS, so vue-tsc kept running against a file list
        // that no longer matched the tree.
        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        var stale = Path.Combine(_root, "obj", "tsconfig.vue.json");
        File.WriteAllText(stale, "{ \"files\": [\"../../gone.vue\"] }");

        Assert.True(NewTask(Front("Chart.tsx")).Execute());

        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void A_bare_ts_is_ambiguous_and_a_tsx_is_not()
    {
        // `Name.ts` is claimed by filename for a Lit or Angular island AND by Rask.Core for scoped
        // TypeScript (#938), so it may only be checked when a declared island claims it. The JSX and
        // single-file-component extensions carry no such ambiguity: scoped TypeScript is never any of
        // them. Pinned directly, because it is the rule that keeps a correct file out of a wrong config.
        Assert.True(WriteExternalPropTypesTask.IsAmbiguouslyPaired("/app/Features/Gantt/Gantt.ts"));
        Assert.True(WriteExternalPropTypesTask.IsAmbiguouslyPaired("/app/widgets/gauge.js"));

        Assert.False(WriteExternalPropTypesTask.IsAmbiguouslyPaired("/app/Islands/Counter.tsx"));
        Assert.False(WriteExternalPropTypesTask.IsAmbiguouslyPaired("/app/Islands/Counter.jsx"));
        Assert.False(WriteExternalPropTypesTask.IsAmbiguouslyPaired("/app/Islands/Chart.vue"));
        Assert.False(WriteExternalPropTypesTask.IsAmbiguouslyPaired("/app/Islands/Meter.svelte"));
    }

    private ITaskItem Front(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "// island");
        return new TaskItem(path);
    }

    // AssemblyPath points at this test assembly on purpose: it is a real, readable assembly that
    // declares no RaskExternalGeneratedTypeScript constants, which is exactly the state under test.
    private WriteExternalPropTypesTask NewTask(params ITaskItem[] frontEnd)
    {
        var obj = Path.Combine(_root, "obj");
        Directory.CreateDirectory(obj);

        return new WriteExternalPropTypesTask
        {
            BuildEngine = new StubEngine(),
            AssemblyPath = typeof(CheckConfigCoverageTests).Assembly.Location,
            IslandAssemblyPath = typeof(CheckConfigCoverageTests).Assembly.Location,
            OutputDirectory = Path.Combine(obj, "props"),
            TsConfigPath = Path.Combine(obj, "tsconfig.rask.json"),
            ProjectDirectory = _root,
            FrontEndFiles = frontEnd,
            CheckConfigPath = Path.Combine(obj, "tsconfig.check.json"),
            VueConfigPath = Path.Combine(obj, "tsconfig.vue.json"),
            SvelteConfigPath = Path.Combine(obj, "tsconfig.svelte.json"),
            SolidConfigPath = Path.Combine(obj, "tsconfig.solid.json"),
            PreactConfigPath = Path.Combine(obj, "tsconfig.preact.json"),
            AngularConfigPath = Path.Combine(obj, "tsconfig.angular.json"),
        };
    }

    private sealed class StubEngine : IBuildEngine
    {
        public List<BuildMessageEventArgs> Messages { get; } = [];

        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "test.csproj";

        public void LogErrorEvent(BuildErrorEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);
        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(
            string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => true;
    }
}
