using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     The styling axis: plain CSS by default, Bootstrap or Tailwind on request.
/// </summary>
/// <remarks>
///     One choice with three answers rather than a pair of booleans. <c>--no-bootstrap --tailwind</c>
///     would have had to mean something, and two flags that are really one question always end up with a
///     state nobody designed.
/// </remarks>
public sealed class StylingTests
{
    private const string Root = "/tmp/styling";

    private static Dictionary<string, string> Generate(Styling styling)
    {
        var result = ProjectGenerator.GenerateServer(
            Root, "App", new ServerBatteries { Styling = styling }, "1.2.3");

        return result.Files.ToDictionary(
            f => f.Path.Replace('\\', '/')[(Root.Length + 1)..],
            f => f.Content,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Plain_is_what_you_get_by_not_choosing()
    {
        // The one answer that assumes nothing about what you are building. Bootstrap and Tailwind are
        // both opinions, and an opinion should be asked for.
        Assert.Equal(Styling.Plain, new ServerBatteries().Styling);
        Assert.False(new ServerBatteries().Bootstrap);
        Assert.False(new ServerBatteries().Tailwind);
    }

    [Fact]
    public void Plain_pulls_in_no_styling_package_and_no_stylesheet_file()
    {
        var files = Generate(Styling.Plain);

        // The baseline lives inline in the shell rather than as a file, so a plain project has no CSS to
        // serve and nothing to build — it works the same on every host with nothing extra wired up.
        Assert.DoesNotContain("Styles/app.css", files.Keys);
        Assert.Contains("A small baseline of our own", files["Features/Shared/App.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void Tailwind_scaffolds_the_stylesheet_its_build_compiles()
    {
        var files = Generate(Styling.Tailwind);

        Assert.Contains("Styles/app.css", files.Keys);
        Assert.Contains("@import \"tailwindcss\";", files["Styles/app.css"], StringComparison.Ordinal);

        // One import and nothing else: v4 needs no config file, no content array and no PostCSS. The
        // sources are detected from the project, which is why the C# pages are scanned with nothing
        // telling it to.
        Assert.DoesNotContain("content:", files["Styles/app.css"], StringComparison.Ordinal);
    }

    [Fact]
    public void Tailwind_links_what_the_build_produces()
    {
        var shell = Generate(Styling.Tailwind)["Features/Shared/App.cs"];

        // A plain <link>, nothing framework-specific: the build writes wwwroot/css/app.css and every host
        // already serves wwwroot.
        Assert.Contains("/css/app.css", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("BootstrapStyles", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Tailwind_page_is_written_in_the_classes_its_own_build_will_find()
    {
        // The page is what proves the loop on the first build: Tailwind scans THIS FILE, so an empty
        // starter page would compile an empty stylesheet and look broken for a reason nobody could see.
        var home = Generate(Styling.Tailwind)["Features/Home/HomePage.cs"];

        Assert.Contains("Class(\"", home, StringComparison.Ordinal);
        Assert.Contains("rounded-xl", home, StringComparison.Ordinal);
        Assert.DoesNotContain("BsCard", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_choice_brings_exactly_its_own_package()
    {
        // Bootstrap and Tailwind are both opinionated resets. Shipping both would have them fighting over
        // the same elements, so each choice brings its own package and only its own.
        Assert.Contains("Rask.Bootstrap", Packages(Styling.Bootstrap));
        Assert.DoesNotContain("Rask.Tailwind", Packages(Styling.Bootstrap));

        Assert.Contains("Rask.Tailwind", Packages(Styling.Tailwind));
        Assert.DoesNotContain("Rask.Bootstrap", Packages(Styling.Tailwind));
    }

    private static IReadOnlyList<string> Packages(Styling styling) =>
        ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries { Styling = styling }, "1.2.3").Packages;

    [Fact]
    public void Only_Tailwind_scaffolds_a_stylesheet_file()
    {
        Assert.DoesNotContain("Styles/app.css", Generate(Styling.Bootstrap).Keys);
        Assert.DoesNotContain("Styles/app.css", Generate(Styling.Plain).Keys);
    }
}
