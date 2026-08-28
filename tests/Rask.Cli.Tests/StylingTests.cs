using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     Styling is not a choice: every scaffolded project is Tailwind.
/// </summary>
/// <remarks>
///     This was an axis with three answers — plain, Bootstrap, Tailwind — and the tests below were about
///     which one you got. Tailwind is a battery now, always referenced and always wired, so what is left
///     to assert is that the loop closes on the first build: the stylesheet is scaffolded, the shell links
///     what the build writes, and the starter page is written in classes Tailwind will actually find. An
///     empty starter page would compile an empty stylesheet and look broken for a reason nobody could see.
/// </remarks>
public sealed class StylingTests
{
    private const string Root = "/tmp/styling";

    [Fact]
    public void Every_project_scaffolds_the_stylesheet_its_build_compiles()
    {
        var files = Generate();

        Assert.Contains("Styles/app.css", files.Keys);
        Assert.Contains("@import \"tailwindcss\";", files["Styles/app.css"], StringComparison.Ordinal);

        // One import and nothing else: v4 needs no config file, no content array and no PostCSS. The
        // sources are detected from the project, which is why the C# pages are scanned with nothing
        // telling it to.
        Assert.DoesNotContain("content:", files["Styles/app.css"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_shell_links_what_the_build_produces()
    {
        // A plain <link>, nothing framework-specific: the build writes wwwroot/css/app.css and every host
        // already serves wwwroot.
        Assert.Contains("/css/app.css", Generate()["Features/Shared/App.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_starter_page_is_written_in_the_classes_its_own_build_will_find()
    {
        var home = Generate()["Features/Home/HomePage.cs"];

        Assert.Contains("Class(\"", home, StringComparison.Ordinal);
        Assert.Contains("rounded-xl", home, StringComparison.Ordinal);
    }

    [Fact]
    public void The_project_takes_the_Tailwind_package()
    {
        var packages = ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries(), "1.2.3").Packages;

        Assert.Contains("Rask.Tailwind", packages);
    }

    // The axis is gone, so nothing in a generated project should still name the package that was one of
    // its answers. Asserted on the SHIPPED files rather than on the package list, because a leftover in a
    // scaffolded page compiles into a project that then does not.
    [Fact]
    public void Nothing_scaffolded_still_reaches_for_Rask_Bootstrap()
    {
        foreach (var (path, content) in Generate())
        {
            Assert.DoesNotContain("Rask.Bootstrap", content, StringComparison.Ordinal);
            Assert.DoesNotContain("BootstrapStyles", content, StringComparison.Ordinal);
            Assert.False(
                content.Contains("BsCard", StringComparison.Ordinal)
                || content.Contains("BsButton", StringComparison.Ordinal),
                $"{path} still uses a Bs* component.");
        }
    }

    private static Dictionary<string, string> Generate()
    {
        var result = ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries(), "1.2.3");

        return result.Files.ToDictionary(
            f => f.Path.Replace('\\', '/')[(Root.Length + 1)..],
            f => f.Content,
            StringComparer.Ordinal);
    }
}
