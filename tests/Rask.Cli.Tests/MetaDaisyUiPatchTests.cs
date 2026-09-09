using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     Loading daisyUI into a stylesheet the framework's own creator wrote.
/// </summary>
/// <remarks>
///     <para>
///         Four of the six meta templates take Tailwind from their own creator, so the file that imports
///         it belongs to them. daisyUI has to be loaded from that same file, which means patching it —
///         and patching a file somebody else wrote is where this lane's failures live.
///     </para>
///     <para>
///         The fixtures below are what those creators actually wrote, captured from real scaffolds
///         rather than invented, because every interesting case here is one an invented fixture would
///         not have: SvelteKit quotes with apostrophes and already carries a <c>@plugin</c>, and
///         TanStack's Tailwind import is the third line, under a Google Fonts <c>@import url(...)</c>.
///     </para>
/// </remarks>
public sealed class MetaDaisyUiPatchTests
{
    // create-next-app@latest --ts --app --no-src-dir --tailwind → app/globals.css
    private const string NextSheet =
        """
        @import "tailwindcss";

        :root {
          --background: #ffffff;
          --foreground: #171717;
        }
        """;

    // sv create --add sveltekit-adapter=adapter:node tailwindcss=plugins:typography
    //   → src/routes/layout.css. Single quotes, and a plugin of its own.
    private const string SvelteKitSheet =
        """
        @import 'tailwindcss';
        @plugin '@tailwindcss/typography';
        """;

    // @tanstack/cli create --framework react --deployment nitro → src/styles.css.
    // The Tailwind import is NOT the first one in the file.
    private const string TanStackSheet =
        """

        @import url("https://fonts.googleapis.com/css2?family=Fraunces:opsz,wght@9..144,500&display=swap");
        @import "tailwindcss";
        @plugin "@tailwindcss/typography";
        """;

    // create-solid@latest --solidstart --v2 -t with-tailwindcss → src/app.css
    private const string SolidStartSheet =
        """
        @import "tailwindcss";

        :root {
          --background-rgb: 214, 219, 220;
        }
        """;

    [Theory]
    [InlineData(NextSheet)]
    [InlineData(SvelteKitSheet)]
    [InlineData(TanStackSheet)]
    [InlineData(SolidStartSheet)]
    public void The_plugin_lands_after_the_tailwind_import(string sheet)
    {
        var patched = ProjectGenerator.AddDaisyUi(sheet);

        // Before the import, the plugin compiles to nothing.
        Assert.True(
            patched.IndexOf("@import", StringComparison.Ordinal)
            < patched.IndexOf("@plugin \"daisyui\";", StringComparison.Ordinal),
            "daisyUI must be loaded after Tailwind, not before it.");
    }

    [Fact]
    public void It_finds_the_tailwind_import_and_not_merely_the_first_one()
    {
        // TanStack's sheet opens with a web-font @import. Inserting after "the first import" would put
        // the plugin above the Tailwind one, where it does nothing.
        var patched = ProjectGenerator.AddDaisyUi(TanStackSheet);

        Assert.True(
            patched.IndexOf("tailwindcss\";", StringComparison.Ordinal)
            < patched.IndexOf("@plugin \"daisyui\";", StringComparison.Ordinal),
            "the plugin was inserted after the fonts import rather than after Tailwind's.");
    }

    [Fact]
    public void A_plugin_the_creator_already_added_survives()
    {
        // SvelteKit's add-on installs AND configures typography. Overwriting this file — rather than
        // patching it — would silently delete something the developer asked for, which is the same
        // argument that keeps the Vite configs patched.
        var patched = ProjectGenerator.AddDaisyUi(SvelteKitSheet);

        Assert.Contains("@plugin '@tailwindcss/typography';", patched, StringComparison.Ordinal);
        Assert.Contains("@plugin \"daisyui\";", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void Single_quotes_are_understood()
    {
        // sv writes 'tailwindcss', not "tailwindcss". A patch that matched only double quotes would
        // find nothing here and throw, on the one framework whose sheet is hardest to hand-fix.
        Assert.Contains(
            "@plugin \"daisyui\";",
            ProjectGenerator.AddDaisyUi(SvelteKitSheet),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NextSheet)]
    [InlineData(SvelteKitSheet)]
    [InlineData(TanStackSheet)]
    [InlineData(SolidStartSheet)]
    public void The_layer_order_is_declared_first(string sheet)
    {
        // daisyUI emits into a `daisyui` layer that Tailwind's import does not rank, so left alone it
        // outranks the utilities beside it: `class="btn px-8"` keeps .btn's padding and drops px-8.
        var patched = ProjectGenerator.AddDaisyUi(sheet);
        var order = "@layer properties, theme, base, components, daisyui, utilities;";

        Assert.Contains(order, patched, StringComparison.Ordinal);
        Assert.True(
            patched.IndexOf(order, StringComparison.Ordinal)
            < patched.IndexOf("@import", StringComparison.Ordinal),
            "a layer name is already placed by the time a later statement tries to order it.");
    }

    [Theory]
    [InlineData(NextSheet)]
    [InlineData(SvelteKitSheet)]
    public void Patching_twice_changes_nothing(string sheet)
    {
        // `rask new --force` runs over a tree that may already have been scaffolded. Stacking a second
        // @plugin and a second @layer statement would be valid CSS and quietly wrong.
        var once = ProjectGenerator.AddDaisyUi(sheet);

        Assert.Equal(once, ProjectGenerator.AddDaisyUi(once), StringComparer.Ordinal);
    }

    [Fact]
    public void A_sheet_with_no_tailwind_import_is_reported_rather_than_ignored()
    {
        // The creators' output is theirs to change, and this is the shape that change would take. A
        // silent no-op produces a project whose every daisyUI class names a rule that does not exist,
        // and the page renders as unstyled text on a green build — so it says so instead, naming the
        // one line to add by hand.
        var error = Assert.Throws<InvalidOperationException>(
            () => ProjectGenerator.AddDaisyUi("body { margin: 0 }"));

        Assert.Contains("@plugin \"daisyui\";", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_meta_template_gets_daisyui_exactly_one_way()
    {
        // Either Rask writes the whole sheet (Nuxt, Analog) and the directive is in it, or the creator
        // wrote the sheet and it is patched. Both would be two copies of daisyUI's CSS; neither would
        // be a starter whose every class styles nothing.
        foreach (var framework in MetaTemplate.All)
        {
            var writes = framework.TailwindStylesheet is { Length: > 0 };
            var patches = framework.DaisyUiStylesheet is { Length: > 0 };

            Assert.True(
                writes ^ patches,
                $"[{framework.Key}] gets daisyUI {(writes && patches ? "twice" : "not at all")}.");
        }
    }
}
