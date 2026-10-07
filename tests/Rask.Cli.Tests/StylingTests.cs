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

        // One import and nothing else: v4 needs no config file, no content array and no PostCSS. The
        // sources are detected from the project, which is why the C# pages are scanned with nothing
        // telling it to.
        Assert.DoesNotContain("content:", files["Styles/app.css"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_stylesheet_takes_Tailwind_and_the_kit_in_with_one_import()
    {
        // The app compiles ONE sheet, as a Flux app does: Tailwind, the kit's theme and `dark` variant,
        // daisyUI, and the classes the kit's components write, beside the classes this project writes.
        // The kit's precompiled sheet used to be linked ahead of this one, and two `@layer utilities`
        // are ranked by link order alone — the app's `bg-white` over the kit's `dark:bg-zinc-800`.
        var sheet = Code(Generate()["Styles/app.css"]);

        Assert.StartsWith("@import \"./vendor/rask-ui.css\";", sheet.TrimStart(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_stylesheet_repeats_nothing_the_import_carries()
    {
        // Each of these was a line of the scaffold once, and each is in rask-ui.css now. A second copy
        // is a second Tailwind (every utility twice), a second layer order that can only be a no-op or
        // wrong, and a second daisyUI whose theme paints :root where the kit's is scoped.
        var sheet = Code(Generate()["Styles/app.css"]);

        Assert.DoesNotContain("@import \"tailwindcss\"", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("@layer", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("@plugin", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("@source", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void The_App_leaves_the_stylesheet_and_the_theme_to_the_host()
    {
        // RaskApp links the app's one sheet and puts the theme scope on <html> — see RaskAppDocumentTests.
        // An App that linked the kit's precompiled sheet as well would be the double include the build
        // refuses, from a place the build cannot see; a Shell override would take the theme scope away.
        var app = Generate()["Features/Shared/App.cs"];

        Assert.DoesNotContain("UiStylesheet", app, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell(", app, StringComparison.Ordinal);
        Assert.Contains("Render() => Router", app, StringComparison.Ordinal);
    }

    [Fact]
    public void The_project_file_carries_no_kit_stylesheet_switch()
    {
        // The import in Styles/app.css is the whole opt-in: the build reads it, writes the kit's Tailwind
        // sources beside the sheet and tells the host not to link a second one. RaskUiWriteStylesheet
        // beside that import is the kit twice, and the build stops on it.
        var csproj = Generate()["App.csproj"];

        Assert.DoesNotContain("RaskUiWriteStylesheet", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("RaskUiWriteDaisyUiPlugin", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("RaskUiTailwind", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void The_generated_stylesheet_and_the_kits_sources_are_not_committed()
    {
        // Both are written INTO the tree by the build rather than into obj/, because Tailwind resolves an
        // @import against the stylesheet and a browser needs the sheet under wwwroot. Neither is
        // anybody's source.
        var ignore = Generate()[".gitignore"];

        Assert.Contains("wwwroot/css/app.css", ignore, StringComparison.Ordinal);
        Assert.Contains("Styles/vendor/", ignore, StringComparison.Ordinal);
    }

    [Fact]
    public void The_starter_page_is_written_in_the_classes_its_own_build_will_find()
    {
        var home = Generate()["Features/Home/HomePage.cs"];

        Assert.Contains("Class(\"", home, StringComparison.Ordinal);

        // The skeleton every `rask new` template draws, down to the class names, so a project looks the
        // same whichever front end it was scaffolded with.
        foreach (var name in (string[])["navbar", "hero", "card bg-base-100", "card-body", "card-actions", "btn btn-primary", "footer"])
        {
            Assert.Contains(name, home, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_daisyui_class_on_the_starter_page_is_a_complete_literal()
    {
        // The rule the whole thing rests on. daisyUI emits a component's CSS only where Tailwind can SEE
        // the class name, so a name built by concatenation — "btn-" + tone — is absent from the sheet and
        // the component renders with NO styling at all while the build stays green and the markup carries
        // exactly the class the call site asked for.
        var home = Generate()["Features/Home/HomePage.cs"];

        Assert.DoesNotContain("Class($\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("\" + ", home, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_with_accounts_offers_the_sign_in_page_it_actually_has()
    {
        // The built-in /login page comes with the accounts battery, which comes with the database. An
        // app without one has nothing at that route, so the starter must not link it.
        var withAccounts = Generate(new ServerBatteries { Data = true })["Features/Home/HomePage.cs"];
        var without = Generate(new ServerBatteries { Data = false })["Features/Home/HomePage.cs"];

        Assert.Contains("\"/login\"", withAccounts, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/login\"", without, StringComparison.Ordinal);
    }

    // Built in means built IN: the Tailwind compiler ships inside the host package, so a scaffolded
    // project references it nowhere and still compiles a stylesheet on its first build. Asserted as an
    // absence on both surfaces a reference could appear on -- the summary list `rask new` prints, and
    // the csproj it writes -- because a stray reference is not inert here: it would import the same
    // targets a second time and run the Tailwind compiler twice over one output file.
    [Fact]
    public void Nothing_references_a_Tailwind_package_because_it_is_in_the_host()
    {
        var result = ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries(), "1.2.3");

        Assert.Equal(["Rask.Server", "Rask.DevTools"], result.Packages);
        Assert.DoesNotContain("Rask.Tailwind", Generate()["App.csproj"], StringComparison.Ordinal);
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

    // The sheet without its comments, so prose about a line is not read as the line.
    private static string Code(string css) =>
        System.Text.RegularExpressions.Regex.Replace(
            css, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);

    private static Dictionary<string, string> Generate() => Generate(new ServerBatteries());

    private static Dictionary<string, string> Generate(ServerBatteries batteries)
    {
        var result = ProjectGenerator.GenerateServer(Root, "App", batteries, "1.2.3");

        return result.Files.ToDictionary(
            f => f.Path.Replace('\\', '/')[(Root.Length + 1)..],
            f => f.Content,
            StringComparer.Ordinal);
    }
}
