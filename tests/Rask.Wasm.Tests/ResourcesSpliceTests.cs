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
        var eventsPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-events.js");
        var pwaPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-pwa.js");
        var wasmApiPath = Path.Combine(repoRoot, "src", "Rask.Wasm", "Resources", "rask-wasm-api.js");
        var inputPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-input.js");
        var scopedPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-scoped.js");
        var hotReloadPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-hotreload.js");
        var devErrorPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-deverror.js");
        var filesPath = Path.Combine(repoRoot, "src", "Rask.Core", "Resources", "rask-files.js");
        var browserPath = Path.Combine(repoRoot, "src", "Rask.Wasm", "Browser", "rask.wasm.js");

        var template = File.ReadAllText(templatePath);
        var dom = File.ReadAllText(domPath);
        var morph = File.ReadAllText(morphPath);
        var api = File.ReadAllText(apiPath);
        var events = File.ReadAllText(eventsPath);
        var pwa = File.ReadAllText(pwaPath);
        var wasmApi = File.ReadAllText(wasmApiPath);
        var input = File.ReadAllText(inputPath);
        var scoped = File.ReadAllText(scopedPath);
        var hotReload = File.ReadAllText(hotReloadPath);
        var devError = File.ReadAllText(devErrorPath);
        var files = File.ReadAllText(filesPath);
        var committed = File.ReadAllText(browserPath);

        // Mirror the marker splice order in _RaskSpliceClientJs (Rask.Wasm.csproj): the diff codec
        // (rask-dom.js), the full-HTML morph (rask-morph.js), the shared interop helpers (rask-api.js),
        // the extended event delegation + keyboard/drag (rask-events.js), the transport-agnostic PWA
        // helpers (rask-pwa.js — shared with the Server client), the WASM-only helpers (rask-wasm-api.js),
        // then the shared rAF input/scroll coalescing (rask-input.js) and scoped-CSS FOUC gating
        // (rask-scoped.js), and finally the shared dev-only hot-reload indicator (rask-hotreload.js) —
        // the last three shared with rask.js.
        var spliced = template
            .Replace("// @@RASK_DOM@@", dom)
            .Replace("// @@RASK_MORPH@@", morph)
            .Replace("// @@RASK_API@@", api)
            .Replace("// @@RASK_EVENTS@@", events)
            .Replace("// @@RASK_PWA@@", pwa)
            .Replace("// @@RASK_WASM_API@@", wasmApi)
            .Replace("// @@RASK_INPUT@@", input)
            .Replace("// @@RASK_SCOPED@@", scoped)
            .Replace("// @@RASK_HOTRELOAD@@", hotReload)
            .Replace("// @@RASK_DEVERROR@@", devError)
            .Replace("// @@RASK_FILES@@", files);

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
