using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Pages;

/// <summary>
/// The hero's three files are the ones tests/Rask.Testing.Database.Tests/Hero compiles and runs, character for
/// character — so the first code a visitor reads builds, and its test passes.
/// </summary>
/// <remarks>
/// The site is a WebAssembly app with no database and cannot compile an aggregate, so the files live in a test
/// project that can, and the site holds a copy. A copy drifts; this is what says so.
/// </remarks>
public sealed class HeroCodeSourceTests
{
    [Theory]
    [InlineData(0, "Product.cs")]
    [InlineData(1, "PriceForm.cs")]
    [InlineData(2, "ProductTests.cs")]
    public void Each_hero_file_is_the_compiled_one_after_its_namespace_line(int tab, string file)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoPaths.Root, "tests", "Rask.Testing.Database.Tests", "Hero", file));

        var compiled = string.Join('\n', lines.SkipWhile(l => !l.StartsWith("namespace ", StringComparison.Ordinal)).Skip(1)).Trim('\n');

        Assert.Equal(compiled, global::Rask.Site.HeroCode.Sources[tab].ReplaceLineEndings("\n"));
    }
}
