using Rask.Wasm;

namespace Rask.Wasm.Tests.Hosting;

/// <summary>
///     The boot script defers starting the runtime on a page that already has its content. These pin
///     the parts of that contract whose loss is silent.
/// </summary>
/// <remarks>
///     <para>
///         Why it exists: a module script runs after parsing and before the first paint, so
///         <c>dotnet.create()</c> takes the main thread while the browser still has nothing on screen —
///         and then holds it for as long as several megabytes of runtime take to instantiate. Measured
///         on this repo's own site: a largest-contentful-paint of 37.8s whose element was in the served
///         HTML the whole time, 37.4s of it recorded as render delay. Deferring until the page has
///         painted took the same measurement to 1.4s.
///     </para>
///     <para>
///         Asserted against the SOURCE rather than by running it. <c>main.ts</c> imports
///         <c>./_framework/dotnet.js</c>, which does not exist outside a published bundle, so there is
///         no honest way to execute this file in a unit test — and the browser E2E cannot see the
///         difference either, because every outcome here ends with a booted app. What is checkable is
///         that the two halves of the contract still agree, which is exactly the part that drifts.
///     </para>
/// </remarks>
public sealed class DeferredBootContractTests
{
    private static readonly string _source = ReadBootScript();

    [Fact]
    public void TheBootScriptLooksForTheATTRIBUTETheSpliceWrites()
    {
        // The two halves are in different languages and different projects: C# stamps the marker during
        // the publish, TypeScript reads it in the browser. Nothing but this connects them, and a rename
        // on either side is a silent revert — the page still works, still boots, and quietly goes back
        // to a 37s largest-contentful-paint that no test and no journey would notice.
        Assert.Contains(PrerenderShell.PrerenderedAttribute, _source, StringComparison.Ordinal);
    }

    [Fact]
    public void AShellStillBootsImmediately()
    {
        // The deferral is conditional, and must stay conditional. On a page that is NOT prerendered the
        // runtime is the only thing between the visitor and any content at all, so every millisecond of
        // waiting is a millisecond of spinner.
        Assert.Contains("if (PRERENDERED) await whenPainted();", _source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnyUserInputBootsAtOnce()
    {
        // The window between "the page looks finished" and "the page answers" is the cost of this
        // feature, and this is what bounds it. Without these listeners a visitor who clicks during the
        // wait gets nothing back and no explanation.
        foreach (var input in new[] { "pointerdown", "keydown", "touchstart" })
        {
            Assert.Contains($"\"{input}\"", _source, StringComparison.Ordinal);
        }

        Assert.Contains("capture: true", _source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWaitHasACeiling()
    {
        // requestAnimationFrame does not fire in a backgrounded tab, and `load` never arrives on a page
        // with a request that hangs. Either would leave the app permanently un-booted, which is the one
        // outcome strictly worse than booting too early.
        Assert.Contains("BOOT_DEFER_CEILING_MS", _source, StringComparison.Ordinal);
        Assert.Contains("setTimeout(go, BOOT_DEFER_CEILING_MS)", _source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRuntimeClearsTheMarkerWhenItTakesThePageOver()
    {
        // The third half of the same contract, and the one #973 was about. The attribute says two things
        // at once: "defer the boot" (read by main.ts) and "these controls are not live yet" — a
        // prerendered page's buttons are present and look clickable from the first paint, and anything
        // clicked before the runtime attaches its handlers is silently lost.
        //
        // An app styles that state by selecting on the attribute, so the attribute has to STOP being
        // true at the moment it stops being true. Cleared in handle(), which is where __raskPainted is
        // set, because that is the one place both render paths pass through: a diff-mode first frame
        // never morphs, and the morph that does happen drops the attribute only as a side effect of
        // replacing <html>'s attributes — which is not the same as clearing it, and is not the kind of
        // thing to leave a visible contract resting on.
        var runtime = ReadClientRuntime();

        Assert.Contains(
            $"document.documentElement?.removeAttribute(\"{PrerenderShell.PrerenderedAttribute}\")",
            runtime,
            StringComparison.Ordinal);
    }

    private static string ReadClientRuntime() =>
        ReadRepoFile(Path.Combine("src", "Rask.Wasm", "Resources", "rask.wasm.ts"), "the client runtime");

    private static string ReadBootScript() =>
        ReadRepoFile(Path.Combine("src", "Rask.Wasm", "Browser", "main.ts"), "the boot script");

    private static string ReadRepoFile(string relativePath, string what)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, relativePath);
        Assert.True(File.Exists(path), $"{what} moved: {path}");
        return File.ReadAllText(path);
    }
}
