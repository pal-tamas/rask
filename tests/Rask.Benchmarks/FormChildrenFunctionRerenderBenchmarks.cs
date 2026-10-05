using BenchmarkDotNet.Attributes;
using Rask.Core;

namespace Rask.Benchmarks;

// A page re-rendered around a CLEAN panel that hosts a form — the cost a form's children function adds.
//
// A children function (`Form.Model(m)[f => [ … ]]`) builds its fields during the walk, and only a real render
// rewinds the counter those fields are matched by, so the panel hosting one renders for real on every walk
// instead of serving its cache (LiveState.BuildsChildrenInWalk). Two arms, the same three kit fields:
//
//   FixedChildren      Form.Model(m)[ … ]        — the panel is served from the render cache
//   ChildrenFunction   Form.Model(m)[f => [ … ]] — the panel re-renders, its fields reused by position
//
// Each operation is a FRESH page, its first render and ten re-renders, so the work per op is bounded however
// the tree behaves. (A page kept across ops is what a long-lived session looks like, and on a tree where the
// clean panel still ran the function from its cache the child map grew by a field set per render — per-op cost
// climbed without bound and BenchmarkDotNet never settled.)
[MemoryDiagnoser]
public partial class FormChildrenFunctionRerenderBenchmarks : global::Rask.Core.RaskMarkup
{
    private const int Rerenders = 10;

    [Benchmark(Baseline = true)]
    public string FixedChildren() => RenderAround(false);

    [Benchmark]
    public string ChildrenFunction() => RenderAround(true);

    private static string RenderAround(bool childrenFunction)
    {
        var page = FormPanelPage.ChildrenFunction(childrenFunction);
        var html = page.RenderAsLiveRoot();
        for (var i = 0; i < Rerenders; i++)
        {
            html = page.RenderAsLiveRoot();
        }

        return html;
    }
}

public sealed partial class FormPanelPage : Component
{
    public required bool ChildrenFunction { get; set; }

    protected override Component? Render() =>
        Div.Class("page")[
            H1["Sign up"],
            FormPanel.ChildrenFunction(ChildrenFunction)
        ];
}

public sealed partial class FormPanel : Component
{
    private readonly SignupModel _model = new();

    public required bool ChildrenFunction { get; set; }

    protected override Component? Render() =>
        ChildrenFunction
            ? Form.Model(_model).OnSubmit(SaveAsync)[f => [
                Ui.Input.Bind(() => _model.Name).Label("Name").Disabled(f.Submitting),
                Ui.Input.Bind(() => _model.Email).Label("Email").Disabled(f.Submitting),
                Ui.Button.Primary.Submit.Disabled(f.Submitting)[f.Submitting ? "Saving…" : "Sign up"]
            ]]
            : Form.Model(_model).OnSubmit(SaveAsync)[
                Ui.Input.Bind(() => _model.Name).Label("Name"),
                Ui.Input.Bind(() => _model.Email).Label("Email"),
                Ui.Button.Primary.Submit["Sign up"]
            ];

    private static Task SaveAsync(SignupModel m) => Task.CompletedTask;

    private sealed class SignupModel
    {
        public string Name { get; set; } = "";

        public string Email { get; set; } = "";
    }
}
