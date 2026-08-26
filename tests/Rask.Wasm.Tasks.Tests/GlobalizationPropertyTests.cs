namespace Rask.Wasm.Tasks.Tests;

/// <summary>
///     Pins the <c>RaskGlobalization</c> property flow in <c>Rask.Wasm.targets</c> — the one line that
///     decides whether a WASM bundle carries ICU.
/// </summary>
/// <remarks>
///     <para>
///         Two properties have to move together and used to be written by hand in every app's csproj.
///         <c>PredefinedCulturesOnly</c> is the trap: it defaults to <c>true</c> whenever
///         <c>InvariantGlobalization</c> is true, and under it <c>CultureInfo.GetCultureInfo("hu-HU")</c>
///         does not fall back to invariant — it <b>throws</b>. An app that opted into globalization but
///         left that default in place would fail at the first culture lookup.
///     </para>
///     <para>
///         Following this project's existing convention, these assert the <i>shape</i> of the targets
///         rather than executing them. The behaviour itself was verified by real evaluation
///         (<c>dotnet msbuild -getProperty:InvariantGlobalization -getProperty:PredefinedCulturesOnly</c>)
///         against <c>samples/Rask.Example.Wasm</c> in all three states:
///         default → <c>true</c>/<c>true</c> (ICU dropped);
///         <c>-p:RaskGlobalization=true</c> → <c>false</c>/<c>false</c> (ICU shipped, any culture allowed);
///         <c>-p:WasmBuildNative=false</c> → <c>false</c> (no invariant flag, so no native relink is
///         forced and the fast unit gate is unaffected).
///     </para>
/// </remarks>
public sealed class GlobalizationPropertyTests
{
    private static readonly string _targets = File.ReadAllText(Path.Combine(
        LocateRepoRoot(), "src", "Rask.Wasm", "build", "Rask.Wasm.targets"));

    [Fact]
    public void Globalization_is_off_by_default_because_ICU_is_not_free()
    {
        Assert.Contains(
            "<RaskGlobalization Condition=\" '$(RaskGlobalization)' == '' \">false</RaskGlobalization>",
            _targets,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Opting_in_also_clears_PredefinedCulturesOnly()
    {
        // The half everyone forgets. Without it the app ships ICU and still throws on the first
        // GetCultureInfo("hu-HU"), which looks like a Rask bug rather than a project-file one.
        var block = Between(
            "'$(RaskWasm)' == 'true' AND '$(RaskGlobalization)' == 'true' \">",
            "</PropertyGroup>");

        Assert.Contains("<InvariantGlobalization>false</InvariantGlobalization>", block, StringComparison.Ordinal);
        Assert.Contains("<PredefinedCulturesOnly>false</PredefinedCulturesOnly>", block, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fast_unit_gates_build_mode_is_still_exempt()
    {
        // InvariantGlobalization=true forces a native relink, which conflicts with the no-native build
        // the unit gate uses (-p:WasmBuildNative=false). This guard came from the per-app property this
        // block replaced and has to survive the move, or the gate starts relinking.
        Assert.Contains(
            "'$(RaskGlobalization)' != 'true'\n                             AND '$(WasmBuildNative)' != 'false' \">",
            _targets.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Asking_for_globalization_while_forcing_invariant_fails_the_build()
    {
        // The one contradiction the properties cannot resolve: the SDK would honour the invariant flag,
        // every culture would format identically, and the app would look like it had simply been given
        // wrong translations. Detected against a snapshot taken BEFORE the overwrite, because by then
        // the SDK has already defaulted the property and "unset" is indistinguishable from "false".
        Assert.Contains("_RaskGlobalizationConflict", _targets, StringComparison.Ordinal);
        Assert.Contains(
            "'$(_RaskAppInvariantGlobalization)' == 'true'", _targets, StringComparison.Ordinal);
    }

    private static string Between(string start, string end)
    {
        var from = _targets.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"expected to find {start} in Rask.Wasm.targets");
        var to = _targets.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"expected {end} after {start}");
        return _targets[from..to];
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
