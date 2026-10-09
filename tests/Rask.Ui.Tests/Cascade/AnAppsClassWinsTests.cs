using System.Text;

namespace Rask.UiTests.Cascade;

/// <summary>
///     A class an app writes on a kit component has the last word over the utility the kit writes there by
///     default — decided by the app's ONE compiled sheet, the way a browser decides it.
/// </summary>
/// <remarks>
///     <para>
///         The kit's classes and the app's are utilities in one layer. Where both set the same property with
///         the same weight, the sheet's order picks the winner, and that order is Tailwind's, not the call
///         site's: <c>.Class("ps-10")</c> on a table's first cell did nothing, because <c>first:ps-0</c>
///         outweighed it. A default written <c>[:where(&amp;)]:…</c> weighs nothing, so whatever the app writes
///         wins, wherever it lands in the sheet.
///     </para>
///     <para>
///         Each component is rendered alone with a class, and for every guarded property the kit sets on that
///         element — in light, at rest, outside every media query — a handful of contradicting utilities are
///         put beside it and the cascade is resolved from the compiled sheet. <see cref="Known" /> names the
///         components where the kit still wins; there an app writes Tailwind's <c>!</c> (<c>ps-10!</c>), as
///         Flux's own guide says to. The full findings land in <c>artifacts/kit-class-overrides.txt</c>.
///     </para>
/// </remarks>
public sealed class AnAppsClassWinsTests
{
    // Per guarded family: each physical longhand, and the utilities an app would contradict it with.
    private static readonly (string Family, string Property, string[] Utilities)[] Guarded =
    [
        ("padding", "padding-top", ["pt-0", "pt-96", "py-0", "p-0"]),
        ("padding", "padding-bottom", ["pb-0", "pb-96", "py-0", "p-0"]),
        ("padding", "padding-left", ["ps-0", "ps-96", "px-0", "p-0"]),
        ("padding", "padding-right", ["pe-0", "pe-96", "px-0", "p-0"]),
        ("margin", "margin-top", ["mt-0", "mt-96", "my-0", "m-0"]),
        ("margin", "margin-bottom", ["mb-0", "mb-96", "my-0", "m-0"]),
        ("margin", "margin-left", ["ms-0", "ms-96", "mx-0", "m-0"]),
        ("margin", "margin-right", ["me-0", "me-96", "mx-0", "m-0"]),
        ("width", "width", ["w-0", "w-96"]),
        ("width", "min-width", ["min-w-0", "min-w-96"]),
        ("width", "max-width", ["max-w-0", "max-w-96"]),
        ("display", "display", ["hidden", "block", "flex", "grid", "inline-flex"]),
        ("text size", "font-size", ["text-xs", "text-9xl"]),
        ("text colour", "color", ["text-black", "text-amber-50", "text-zinc-950"]),
        ("rounded", "border-top-left-radius", ["rounded-none", "rounded-full"]),
        ("rounded", "border-top-right-radius", ["rounded-none", "rounded-full"]),
        ("rounded", "border-bottom-right-radius", ["rounded-none", "rounded-full"]),
        ("rounded", "border-bottom-left-radius", ["rounded-none", "rounded-full"]),
        ("gap", "row-gap", ["gap-y-0", "gap-y-96", "gap-0"]),
        ("gap", "column-gap", ["gap-x-0", "gap-x-96", "gap-0"]),
    ];

    // What a table's parts set beyond those, and an app lays its columns out with.
    private static readonly (string Family, string Property, string[] Utilities)[] TableOnly =
    [
        ("white-space", "white-space", ["whitespace-normal", "whitespace-pre"]),
        ("text alignment", "text-align", ["text-left", "text-center", "text-right", "text-end"]),
        ("table layout", "table-layout", ["table-auto"]),
        ("overflow", "overflow-x", ["overflow-hidden", "overflow-visible", "overflow-x-auto"]),
    ];

    /// <summary>
    ///     Where the kit's default still outweighs or outruns an app's utility, by component: the families an
    ///     app has to write with <c>!</c> there. Remove a family when its default becomes zero-specificity; the
    ///     test fails on an entry that is no longer true, and on a finding that is not here.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["UiAccordionContent"] = "padding, text colour, text size",
        ["UiAccordionHeading"] = "display, text colour, text size, width",
        ["UiAccordionItem"] = "padding",
        ["UiAura"] = "display, padding, rounded",
        ["UiAutocomplete"] = "display, gap, padding, rounded, text colour, text size, width",
        ["UiAvatar"] = "display, rounded, text colour, text size",
        ["UiAvatarGroup"] = "display",
        ["UiBadge"] = "display, margin, padding, rounded, text colour, text size",
        ["UiBadgeClose"] = "margin, padding",
        ["UiBrand"] = "display, margin",
        ["UiBreadcrumbs"] = "display",
        ["UiBreadcrumbsItem"] = "display, text size",
        ["UiButton"] = "display, gap, margin, rounded, text colour, text size, width",
        ["UiButtonGroup"] = "display",
        ["UiCallout"] = "display, padding, rounded",
        ["UiCalloutHeading"] = "display, gap, text size",
        ["UiCalloutText"] = "text size",
        ["UiCard"] = "padding, rounded",
        ["UiCardActions"] = "display, gap, margin",
        ["UiCardBleed"] = "margin, rounded",
        ["UiCardFooter"] = "display, padding",
        ["UiCardHeader"] = "display, padding",
        ["UiCardHeading"] = "text colour, text size",
        ["UiCardSubheading"] = "margin, text colour, text size",
        ["UiCarousel"] = "display",
        ["UiChartLegend"] = "display, gap, padding",
        ["UiChartTooltip"] = "display, rounded",
        ["UiChartTooltipHeading"] = "display, padding, text colour, text size",
        ["UiChartTooltipValue"] = "display, gap, padding, text colour, text size",
        ["UiChatBubble"] = "display, gap, padding",
        ["UiCheckbox"] = "display, margin",
        ["UiCheckboxAll"] = "display, margin",
        ["UiCheckboxIndicator"] = "display, text colour, text size",
        ["UiCommand"] = "display, gap, padding, rounded, text size, width",
        ["UiContext"] = "display",
        ["UiCountdown"] = "display",
        ["UiDataGrid"] = "display, gap",
        ["UiDatePicker"] = "width",
        ["UiDatePickerRange"] = "width",
        ["UiDescription"] = "text colour, text size",
        ["UiDiff"] = "display, width",
        ["UiDock"] = "display, padding, text colour, width",
        ["UiDropdown"] = "display",
        ["UiEditor"] = "rounded, width",
        ["UiEditorButton"] = "display, padding, rounded, text colour, text size",
        ["UiEditorToolbar"] = "rounded",
        ["UiError"] = "display, margin, text colour, text size",
        ["UiFab"] = "display, gap, text size",
        ["UiField"] = "display, gap",
        ["UiFileItem"] = "display, rounded, text size",
        ["UiFileItemRemove"] = "display, gap, rounded, text colour, text size, width",
        ["UiFileUploadDropzone"] = "display, padding, rounded",
        ["UiFooter"] = "display, gap, padding, text size, width",
        ["UiHeader"] = "display, padding",
        ["UiHeading"] = "text colour, text size",
        ["UiHero"] = "display, width",
        ["UiHover3d"] = "display",
        ["UiHoverGallery"] = "display, gap, width",
        ["UiIndicator"] = "display, width",
        ["UiInput"] = "width",
        ["UiInputGroup"] = "display, width",
        ["UiInputGroupPrefix"] = "display, padding, rounded, text colour, text size",
        ["UiInputGroupSuffix"] = "display, padding, rounded, text colour, text size",
        ["UiKanban"] = "display, gap",
        ["UiKanbanCard"] = "padding, rounded, width",
        ["UiKanbanColumnCards"] = "display, gap, padding",
        ["UiKanbanColumnFooter"] = "display, gap, padding",
        ["UiKanbanColumnHeader"] = "display, padding",
        ["UiKbd"] = "text size",
        ["UiLabel"] = "display, text colour, text size",
        ["UiLegend"] = "margin, text colour, text size, width",
        ["UiLink"] = "text colour",
        ["UiList"] = "display, text size",
        ["UiLoading"] = "display, gap",
        ["UiMain"] = "margin, padding, width",
        ["UiMask"] = "display",
        ["UiMenu"] = "padding, rounded, text colour, width",
        ["UiMenuCheckbox"] = "display, padding, rounded, text colour, text size, width",
        ["UiMenuCheckboxGroup"] = "display",
        ["UiMenuGroup"] = "margin, padding",
        ["UiMenuItem"] = "display, padding, rounded, text colour, text size, width",
        ["UiMenuRadio"] = "display, padding, rounded, text colour, text size, width",
        ["UiMenuRadioGroup"] = "display",
        ["UiMenuSubmenu"] = "display, padding, rounded, text colour, text size, width",
        ["UiMockupBrowser"] = "rounded",
        ["UiMockupPhone"] = "display, padding, rounded, width",
        ["UiMockupWindow"] = "display, padding, rounded",
        ["UiModal"] = "margin, padding, rounded, text colour",
        ["UiNavbar"] = "display, gap, padding",
        ["UiNavbarItem"] = "display, padding, rounded, text colour",
        ["UiNavlist"] = "display",
        ["UiNavlistItem"] = "display, gap, margin, padding, rounded, text colour",
        ["UiNavmenu"] = "padding, rounded, text colour, width",
        ["UiNavmenuItem"] = "display, padding, rounded, text colour, text size, width",
        ["UiOtp"] = "display, gap, width",
        ["UiPopover"] = "display",
        ["UiProfile"] = "display, padding, rounded",
        ["UiProgress"] = "width",
        ["UiRadio"] = "display, margin",
        ["UiRadioIndicator"] = "display, text colour, text size",
        ["UiRating"] = "display",
        ["UiSelect"] = "padding, rounded, text colour, text size, width",
        ["UiSeparator"] = "width",
        ["UiSidebar"] = "display, gap, padding",
        ["UiSidebarBrand"] = "display, gap, padding",
        ["UiSidebarCollapse"] = "display, margin",
        ["UiSidebarGroup"] = "display",
        ["UiSidebarHeader"] = "display, gap",
        ["UiSidebarItem"] = "display, gap, margin, padding, rounded, text colour, width",
        ["UiSidebarNav"] = "display",
        ["UiSidebarProfile"] = "display, padding, rounded, width",
        ["UiSidebarSearch"] = "display, gap, padding, rounded, text colour, text size, width",
        ["UiSidebarToggle"] = "display, gap, margin, rounded, text colour, text size",
        ["UiSkeletonLine"] = "padding",
        ["UiSlider"] = "display, width",
        ["UiSliderTick"] = "display, text colour, text size, width",
        ["UiStackLayout"] = "display",
        ["UiSteps"] = "display",
        ["UiSwap"] = "display",
        ["UiSwitch"] = "display, width",
        ["UiTab"] = "display, gap, margin, padding, text colour, text size",
        ["UiTabPanel"] = "padding",
        ["UiTabs"] = "display, gap, padding, rounded",
        ["UiText"] = "text colour, text size",
        ["UiTextarea"] = "padding, rounded, text colour, text size, width",
        ["UiTimePicker"] = "width",
        ["UiTimelineIndicator"] = "display, text colour, text size",
        ["UiTooltipContent"] = "padding, rounded, text colour, text size",
        ["UiTree"] = "display, padding, text size, width",
        ["UiValidator"] = "margin, text size",
    };

    /// <summary>
    ///     The components this guard cannot see: they render nothing alone (a part that reads its parent's
    ///     scope, a control that needs a service), or put the class on no element of that render.
    /// </summary>
    internal static readonly IReadOnlySet<string> Unseen = new HashSet<string>(StringComparer.Ordinal)
    {
        "UiAutocompleteItem", "UiChartArea", "UiChartAxisGrid", "UiChartAxisLine", "UiChartAxisMark", "UiChartAxisTick",
        "UiChartBar", "UiChartCursor", "UiChartGroup", "UiChartLine", "UiChartPie", "UiChartPoint", "UiChartStack", "UiChartSvg",
        "UiChartZeroLine", "UiColumn", "UiDatePickerButton", "UiDatePickerInput", "UiFilter", "UiMockupCode", "UiPagination",
        "UiPillboxInput", "UiPillboxOption", "UiPillboxOptionCreate", "UiPillboxOptionEmpty", "UiPillboxSearch",
        "UiPillboxTrigger", "UiSelectButton", "UiSelectInput", "UiSelectMultiple", "UiSelectOptionCreate", "UiSelectOptionEmpty",
        "UiSelectSearch", "UiTextRotate", "UiToast", "UiToastGroup",
    };

    // A default as the table's cells wrote it before: heavier than any plain utility.
    private const string Weighted = "first:ps-0";

    private static readonly Lazy<CompiledSheet> Sheet = new(() => CompiledSheet.Parse(KitConsumer.Compile(
        KitConsumer.Import,
        string.Join(' ', Guarded.Concat(TableOnly).SelectMany(guard => guard.Utilities).Append(Weighted).Distinct(StringComparer.Ordinal)))));

    [Fact]
    public void An_apps_utility_beats_the_kits_default_on_every_component_but_the_known_ones()
    {
        var found = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var report = new StringBuilder();

        foreach (var type in KitRenders.Components())
        {
            var lost = KitRenders.Elements(type).SelectMany(element => Lost(element, Guarded)).Distinct().ToList();
            if (lost.Count > 0)
            {
                found[KitRenders.Name(type)] = string.Join(", ", lost.Select(l => l.Family).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
                report.AppendLine(KitRenders.Name(type));
                lost.ForEach(l => report.Append("  ").Append(l.Family).Append(": ").Append(l.Kit).Append(" beats ").AppendLine(l.App));
            }
        }

        var listed = string.Join('\n', found.Select(pair => $"[\"{pair.Key}\"] = \"{pair.Value}\","));
        var known = string.Join('\n', Known.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"[\"{pair.Key}\"] = \"{pair.Value}\","));
        File.WriteAllText(Path.Combine(Artifacts(), "kit-class-overrides.txt"), listed + "\n\n" + report);

        Assert.True(
            known == listed,
            "Where an app's utility loses to the kit's default has changed. Write the default the app could not override as "
            + "`[:where(&)]:…`, or make Known this list (each rule that won is in artifacts/kit-class-overrides.txt):\n" + listed);
    }

    [Fact]
    public void Every_component_that_takes_a_class_is_seen_carrying_it_but_the_known_ones()
    {
        var unseen = string.Join(", ", KitRenders.Components()
            .Where(type => KitRenders.Elements(type).Count == 0)
            .Select(type => '"' + KitRenders.Name(type) + '"')
            .Distinct(StringComparer.Ordinal));

        var known = string.Join(", ", Unseen.Order(StringComparer.Ordinal).Select(name => '"' + name + '"'));

        Assert.True(known == unseen, "The components rendered alone with a class that show it on no element are now:\n" + unseen);
    }

    [Theory]
    [InlineData(typeof(UiTable))]
    [InlineData(typeof(UiTableColumns))]
    [InlineData(typeof(UiTableColumn))]
    [InlineData(typeof(UiTableRows))]
    [InlineData(typeof(UiTableRow))]
    [InlineData(typeof(UiTableCell))]
    public void A_table_part_yields_to_an_apps_class_in_everything_columns_are_laid_out_with(Type part)
    {
        var elements = KitRenders.Elements(part);

        var lost = elements.SelectMany(element => Lost(element, [.. Guarded, .. TableOnly])).Distinct().ToList();

        Assert.NotEmpty(elements);
        Assert.Empty(lost);
    }

    [Fact]
    public void The_resolver_tells_a_default_that_weighs_nothing_from_one_that_outweighs_the_app()
    {
        var weightless = new StyledElement("td", new HashSet<string>(StringComparer.Ordinal) { "[:where(&)]:py-3" }, new Dictionary<string, string>(StringComparer.Ordinal));
        var weighted = new StyledElement("td", new HashSet<string>(StringComparer.Ordinal) { Weighted }, new Dictionary<string, string>(StringComparer.Ordinal));

        var yields = Lost(weightless, Guarded).ToList();
        var holds = Lost(weighted, Guarded).ToList();

        Assert.Empty(yields);
        Assert.Contains(holds, l => l is { Family: "padding", App: "ps-96" });
    }

    // Every (family, kit rule, app utility) where the kit's rule still sets the property with the app's class beside it.
    private static IEnumerable<(string Family, string Kit, string App)> Lost(StyledElement element, (string Family, string Property, string[] Utilities)[] guards)
    {
        foreach (var (family, property, utilities) in guards)
        {
            if (Sheet.Value.Winner(element, property) is not { } kit)
            {
                continue;
            }

            foreach (var utility in utilities)
            {
                var app = Sheet.Value.Winner(Only(utility), property);
                Assert.True(app is not null && app.Selector.SubjectClasses().Contains(utility, StringComparer.Ordinal), $"the sheet has no .{utility} that sets {property}.");

                if (app.Declarations[property].Value != kit.Declarations[property].Value && !app.Beats(kit, property))
                {
                    yield return (family, kit.Text, utility);
                }
            }
        }
    }

    private static string Artifacts() => Directory.CreateDirectory(Path.Combine(RepoRoot.FullPath, "artifacts")).FullName;

    private static StyledElement Only(string utility) =>
        new("div", new HashSet<string>(StringComparer.Ordinal) { utility }, new Dictionary<string, string>(StringComparer.Ordinal));
}
