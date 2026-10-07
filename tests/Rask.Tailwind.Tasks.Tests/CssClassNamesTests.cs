namespace Rask.Tailwind.Tasks.Tests;

/// <summary>
///     A compiled sheet read back into the class names markup writes — the list a component library
///     hands an app's Tailwind build (<c>RaskTailwindClassList</c>).
/// </summary>
public sealed class CssClassNamesTests
{
    [Fact]
    public void A_utility_is_named_the_way_markup_writes_it()
    {
        var css = """.dark\:bg-white\/10:where(.dark,.dark *){background-color:#ffffff1a}.w-1\.5{width:.375rem}""";

        var names = CssClassNames.In(css);

        Assert.Equal(["dark", "dark:bg-white/10", "w-1.5"], names);
    }

    [Fact]
    public void A_class_that_starts_with_a_digit_comes_back_from_its_hex_escape()
    {
        // CSS cannot open an identifier with a digit, so `2xl:grid` is written `\32 xl\:grid`.
        var names = CssClassNames.In("""@media (min-width:96rem){.\32 xl\:grid{display:grid}}""");

        Assert.Equal(["2xl:grid"], names);
    }

    [Fact]
    public void An_arbitrary_variant_keeps_its_brackets_and_everything_in_them()
    {
        var css = """.\[\&\>\[data-ui-label\]\]\:mb-3>[data-ui-label]{margin-bottom:.75rem}.-mx-\[calc\(var\(--ui-bleed-x\)\+1px\)\]{margin-inline:0}""";

        var names = CssClassNames.In(css);

        Assert.Equal(["-mx-[calc(var(--ui-bleed-x)+1px)]", "[&>[data-ui-label]]:mb-3"], names);
    }

    [Fact]
    public void Every_class_in_a_selector_is_taken_not_just_the_first()
    {
        // A variant can put the candidate anywhere, and a component rule names several classes at once.
        var css = """:where(.rask-ops) .in-\[\.x\]\:p-2{padding:.5rem}.card-sm .card-body,.btn:is(.btn-active){color:red}""";

        var names = CssClassNames.In(css);

        Assert.Equal(["btn", "btn-active", "card-body", "card-sm", "in-[.x]:p-2", "rask-ops"], names);
    }

    [Fact]
    public void A_dot_that_is_not_in_a_selector_names_nothing()
    {
        // A length, a file name, a string, a comment and a layer's dotted name all carry dots.
        var css = """
            /* .commented{} */
            @layer daisyui.l1.l2{.a{margin:.5rem 1.5rem;background:url(img.hidden.png);content:".flex{";transition:all .2s}}
            @keyframes spin{12.5%{opacity:.5}}
            """;

        Assert.Equal(["a"], CssClassNames.In(css));
    }

    [Fact]
    public void Nested_rules_are_read_as_selectors_too()
    {
        // The unminified shape: Tailwind nests a variant inside the class it belongs to.
        var css = """
            .hover\:underline {
              &:hover {
                @media (hover: hover) {
                  text-decoration-line: underline;
                }
              }
            }
            .group-hover\:block {
              &:is(:where(.group):hover *) { display: block; }
            }
            """;

        Assert.Equal(["group", "group-hover:block", "hover:underline"], CssClassNames.In(css));
    }

    [Fact]
    public void A_sheet_with_no_class_at_all_fails_the_task_rather_than_writing_an_empty_list()
    {
        // An empty list compiles: every project reading it gets a sheet with none of the library's
        // classes and renders unstyled, green.
        var dir = Path.Combine(Path.GetTempPath(), "rask-classlist-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "in.css"), "@layer base{html{color:red}}");
            var engine = new RecordingEngine();
            var task = new WriteTailwindClassListTask
            {
                BuildEngine = engine,
                Stylesheet = Path.Combine(dir, "in.css"),
                Output = Path.Combine(dir, "out", "classes.txt"),
            };

            var ok = task.Execute();

            Assert.False(ok);
            Assert.False(File.Exists(task.Output));
            Assert.Contains("defines no class at all", Assert.Single(engine.Errors), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void The_list_is_one_name_per_line_and_untouched_when_nothing_changed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rask-classlist-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "in.css"), ".b{color:red}.a{color:red}");
            var task = new WriteTailwindClassListTask
            {
                BuildEngine = new RecordingEngine(),
                Stylesheet = Path.Combine(dir, "in.css"),
                Output = Path.Combine(dir, "classes.txt"),
            };

            Assert.True(task.Execute());
            var written = File.GetLastWriteTimeUtc(task.Output);
            File.SetLastWriteTimeUtc(task.Output, written.AddHours(-1));
            Assert.True(task.Execute());

            // Nothing but the names: Tailwind reads every word of the file as a candidate.
            Assert.Equal("a\nb\n", File.ReadAllText(task.Output));
            // A consumer recompiles when this file is newer than its sheet, so it only moves on a change.
            Assert.Equal(written.AddHours(-1), File.GetLastWriteTimeUtc(task.Output));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
