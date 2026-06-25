namespace Rask.Wasm.Tests;

public sealed class ResourcesSpliceTests
{
    [Fact]
    public void Resources_template_splices_to_committed_Browser_rask_wasm_js()
    {
        var repoRoot = LocateRepoRoot();
        var templatePath = Path.Combine(repoRoot, "src", "Rask.Wasm", "Resources", "rask.wasm.js");
        var domPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-dom.js");
        var morphPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-morph.js");
        var apiPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-api.js");
        var browserPath = Path.Combine(repoRoot, "src", "Rask.Wasm", "Browser", "rask.wasm.js");

        var template = File.ReadAllText(templatePath);
        var dom = File.ReadAllText(domPath);
        var morph = File.ReadAllText(morphPath);
        var api = File.ReadAllText(apiPath);
        var committed = File.ReadAllText(browserPath);

        // Mirror the marker splice order in _RaskSpliceClientJs (Rask.Wasm.csproj): the diff codec
        // (rask-dom.js), then the full-HTML morph (rask-morph.js), then the interop helpers (rask-api.js).
        var spliced = template
            .Replace("// @@RASK_DOM@@", dom)
            .Replace("// @@RASK_MORPH@@", morph)
            .Replace("// @@RASK_API@@", api);

        Assert.True(
            spliced == committed,
            "src/Rask.Wasm/Browser/rask.wasm.js is out of sync with its template. " +
            "Edit Rask.Wasm/Resources/rask.wasm.js (the source of truth) — never hand-edit " +
            "Browser/rask.wasm.js. The _RaskSpliceClientJs target in Rask.Wasm.csproj " +
            "regenerates Browser/rask.wasm.js from the template + rask-dom.js + rask-morph.js " +
            "on every clean build; if those diverge, CI ships a Browser/rask.wasm.js that " +
            "doesn't match the .NET dispatch surface and the deployed GH Pages example breaks.");
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");
    }
}
