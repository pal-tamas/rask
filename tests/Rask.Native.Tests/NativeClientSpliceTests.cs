namespace Rask.Native.Tests;

// Byte-equality guard for the native client dialect, mirroring Rask.Wasm.Tests.ResourcesSpliceTests:
// the committed Assets/rask.native.js must equal the template with every shared module spliced in at
// its marker, in the exact order of _RaskSpliceNativeClientJs (Rask.Native.csproj). This is what
// keeps the three ES-dialect clients from drifting once a block is shared — a change to any shared
// module that isn't regenerated into the committed artifact fails here.
public sealed class NativeClientSpliceTests
{
    [Fact]
    public void Resources_template_splices_to_committed_Assets_rask_native_js()
    {
        var repoRoot = LocateRepoRoot();
        var core = Path.Combine(repoRoot, "src", "Rask.Core", "Resources");
        var template = File.ReadAllText(Path.Combine(repoRoot, "src", "Rask.Native", "Resources", "rask.native.js"));
        var dom = File.ReadAllText(Path.Combine(core, "rask-dom.js"));
        var morph = File.ReadAllText(Path.Combine(core, "rask-morph.js"));
        var api = File.ReadAllText(Path.Combine(core, "rask-api.js"));
        var events = File.ReadAllText(Path.Combine(core, "rask-events.js"));
        var pwa = File.ReadAllText(Path.Combine(core, "rask-pwa.js"));
        var input = File.ReadAllText(Path.Combine(core, "rask-input.js"));
        var scoped = File.ReadAllText(Path.Combine(core, "rask-scoped.js"));
        var committed = File.ReadAllText(Path.Combine(repoRoot, "src", "Rask.Native", "Assets", "rask.native.js"));

        // Mirror the marker splice order in _RaskSpliceNativeClientJs (Rask.Native.csproj).
        var spliced = template
            .Replace("// @@RASK_DOM@@", dom)
            .Replace("// @@RASK_MORPH@@", morph)
            .Replace("// @@RASK_API@@", api)
            .Replace("// @@RASK_EVENTS@@", events)
            .Replace("// @@RASK_PWA@@", pwa)
            .Replace("// @@RASK_INPUT@@", input)
            .Replace("// @@RASK_SCOPED@@", scoped);

        Assert.True(
            spliced == committed,
            "src/Rask.Native/Assets/rask.native.js is out of sync with its template. Edit " +
            "Rask.Native/Resources/rask.native.js (the source of truth) or a shared Rask.Core module — " +
            "never hand-edit Assets/rask.native.js. The _RaskSpliceNativeClientJs target regenerates it " +
            "on every build; if they diverge, the native app ships a client that doesn't match the " +
            "shared render/diff behaviour of the Server + WASM clients.");
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
