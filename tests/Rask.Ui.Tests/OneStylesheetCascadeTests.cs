using System.Text.RegularExpressions;

namespace Rask.UiTests;

/// <summary>
///     An app's utilities and the kit's are ranked by Tailwind, in one sheet — not by which of two
///     stylesheets a document linked last.
/// </summary>
/// <remarks>
///     <para>
///         <b>The bug, measured on the site in a browser.</b> The kit compiled its own sheet, the app
///         compiled a second, and the app's was linked after it. Both put utilities in
///         <c>@layer utilities</c>, and the kit's <c>dark</c> variant is a <c>:where()</c> — no
///         specificity of its own. So between a kit variant and ANY base utility the app happened to
///         write somewhere, layer and specificity tied and link order decided: a Flux card
///         (<c>bg-white dark:bg-white/4</c>) computed <c>rgb(255, 255, 255)</c> in dark mode because the
///         showcase writes <c>bg-white</c> on some other element; <c>dark:text-zinc-300</c> lost to
///         <c>text-zinc-500</c>; a metric's <c>sm:flex-row</c> lost to <c>flex-col</c>. Every class in
///         the markup was right and every gate was green.
///     </para>
///     <para>
///         A Flux app never has this: one Tailwind build scans the app and Flux's views, so each utility
///         exists once and Tailwind's own order holds — a base utility before its variants, a shorthand
///         before its longhands. <c>@import "./vendor/rask-ui.css"</c> is that arrangement here.
///     </para>
/// </remarks>
public sealed class OneStylesheetCascadeTests
{
    // What a page of the app writes. None of it is exotic: each is a base utility or a shorthand whose
    // variant or longhand some kit component writes on an element of its own.
    private const string AppMarkup = "bg-white m-0 flex-col text-zinc-500 dark:bg-red-500";

    // (the app's class, the kit's class that must win where both apply)
    public static TheoryData<string, string> Pairs() => new()
    {
        { "bg-white", "dark:bg-white/10" },
        { "text-zinc-500", "dark:text-zinc-300" },
        { "flex-col", "sm:flex-row" },
        { "m-0", "mt-auto" },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Linked_after_the_precompiled_kit_an_apps_utility_has_the_last_word_over_the_kits(string app, string kit)
    {
        // The reproduction, and the reason the build refuses this pairing (KitTailwindDeliveryTests).
        // The document is the kit's sheet, then the app's — the order every Rask app linked them in.
        var document = UiStylesheet.Css + _appAlone.Value;

        Assert.True(LastRule(UiStylesheet.Css, kit) >= 0, $"the kit no longer writes {kit}; pick another pair.");
        Assert.True(LastRule(UiStylesheet.Css, app) < LastRule(UiStylesheet.Css, kit), "inside the kit's own sheet the order is right.");

        Assert.True(
            LastRule(document, app) > LastRule(document, kit),
            $"with two sheets the app's .{app} should come after the kit's .{kit} — if it no longer does, "
            + "the hazard this suite records has moved and the build's refusal may be out of date.");
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void In_the_apps_one_sheet_the_kits_variant_follows_the_apps_base_utility(string app, string kit)
    {
        var sheet = _appWithKit.Value;

        Assert.True(LastRule(sheet, kit) >= 0, $".{kit} is not in the app's sheet, so the kit's component renders without it.");
        Assert.True(
            LastRule(sheet, app) < LastRule(sheet, kit),
            $".{app} comes after .{kit} in the one sheet, so the app's utility would still outrank the kit's.");
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void In_the_apps_one_sheet_a_utility_both_write_exists_once(string app, string kit)
    {
        // The kit writes bg-white too. Two copies would be two places in the order, which is the bug.
        _ = Assert.Single(Rules(_appWithKit.Value, app));

        // A variant may be several rules (one per branch of `dark`), but in ONE run: nothing of the
        // app's sits between them.
        var rules = Rules(_appWithKit.Value, kit);
        Assert.True(rules[0] > Rules(_appWithKit.Value, app)[0], $".{kit} starts before .{app}.");
    }

    [Fact]
    public void The_apps_own_dark_utilities_are_the_kits_dark_variant()
    {
        // One definition of `dark`, carried by the import. An app that defined its own would have dark:
        // utilities that follow a different switch than the components beside them.
        var selectors = string.Join('\n', Regex.Matches(_appWithKit.Value, @"\.dark\\:bg-red-500[^{]*\{").Select(m => m.Value));

        Assert.Contains(":where(.dark, .dark *)", selectors, StringComparison.Ordinal);
        Assert.Contains("[data-rask-ui]:not([data-theme])", selectors, StringComparison.Ordinal);
    }

    [Theory]
    // An app's utilities beat its own preflight, daisyUI and the kit's rules...
    [InlineData("base", "utilities")]
    [InlineData("daisyui", "utilities")]
    [InlineData("rask", "utilities")]
    // ...and the kit's corrections beat the daisyUI component rules they correct, which are in `utilities`.
    [InlineData("utilities", "rask-ui-corrections")]
    [InlineData("theme", "base")]
    public void The_one_sheet_ranks_its_layers_as_the_kits_own_sheet_does(string lower, string higher)
    {
        var order = LayerOrder(_appWithKit.Value);

        Assert.Contains(lower, order);
        Assert.Contains(higher, order);
        Assert.True(order.IndexOf(lower) < order.IndexOf(higher), $"'{lower}' must rank below '{higher}': {string.Join(", ", order)}");
    }

    [Fact]
    public void Both_entries_declare_the_same_layer_order()
    {
        // ui.precompiled.css states it for the kit's own sheet and rask-ui.css for an app's. One order,
        // written twice because each is the first line of a different sheet.
        var styles = Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Styles");

        var precompiled = Statement(File.ReadAllText(Path.Combine(styles, "ui.precompiled.css")));
        var app = Statement(File.ReadAllText(Path.Combine(styles, "rask-ui.css")));

        Assert.Equal("properties, theme, base, components, daisyui, rask, utilities", app);
        Assert.Equal(precompiled, app);
    }

    [Fact]
    public void The_kits_own_file_declares_no_order_and_imports_no_Tailwind()
    {
        // ui.css is imported by both entries, after each has said what Tailwind it wants. A statement or
        // an entry point of its own there would be a second opinion in every sheet.
        var kit = Code(File.ReadAllText(Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui", "Styles", "ui.css")));

        Assert.DoesNotMatch(new Regex(@"@layer\s+[\w-]+(\s*,\s*[\w-]+)+\s*;"), kit);
        Assert.DoesNotContain("@import", kit, StringComparison.Ordinal);
    }

    // The app as it was: its own Tailwind build, knowing nothing of the kit.
    private static readonly Lazy<string> _appAlone = new(() => KitConsumer.Compile("@import \"tailwindcss\";", AppMarkup, withKit: false));

    private static readonly Lazy<string> _appWithKit = new(() => KitConsumer.Compile(KitConsumer.Import, AppMarkup));

    private static int LastRule(string css, string name) => Rules(css, name).LastOrDefault(-1);

    // Where `.name` opens a rule: at a boundary, and not as the head of a longer class.
    private static List<int> Rules(string css, string name)
    {
        var selector = Regex.Escape("." + Regex.Replace(name, @"[:/.\[\]]", @"\$0"));
        return [.. Regex.Matches(css, $@"(?<=[{{}}\s,]){selector}(?![\w\\-])(?=[^{{}};]*\{{)").Select(m => m.Index)];
    }

    private static List<string> LayerOrder(string css)
    {
        var order = new List<string>();
        foreach (Match m in Regex.Matches(css, @"@layer\s+([A-Za-z0-9_\-]+(?:\s*,\s*[A-Za-z0-9_\-]+)*)\s*[{;]"))
        {
            foreach (var name in m.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!order.Contains(name, StringComparer.Ordinal))
                {
                    order.Add(name);
                }
            }
        }

        return order;
    }

    private static string Statement(string css) =>
        Regex.Match(Code(css), @"@layer\s+([\w-]+(?:\s*,\s*[\w-]+)+)\s*;").Groups[1].Value;

    private static string Code(string css) => Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
}
