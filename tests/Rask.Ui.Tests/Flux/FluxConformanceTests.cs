using System.Reflection;
using System.Text.Json;

namespace Rask.UiTests.Flux;

/// <summary>
///     Every Rask.Ui component that mirrors a Flux UI component takes what Flux documents for it.
/// </summary>
/// <remarks>
///     <para>
///     <c>flux.snapshot.json</c> is Flux's reference — every component, its props, the values each accepts —
///     read from fluxui.dev's docs by <c>scripts/flux/refresh.mjs</c>. This holds each built component to it: a
///     documented prop with no property of that name, or a documented value with no enum member, fails here.
///     So does a refresh that brings a prop Flux added since.
///     </para>
///     <para>
///     What Flux says with a Livewire or Alpine attribute (<c>wire:model</c>, <c>x-on:click</c>) is said in
///     Rask with <c>Bind</c>/<c>Value</c> and a <c>Callback</c>, and is skipped by rule. Anything else that
///     does not translate is named in <see cref="NotTranslated" /> with the reason, one line each.
///     </para>
/// </remarks>
public sealed class FluxConformanceTests
{
    /// <summary>Flux part → the Rask.Ui type that mirrors it. A component joins this when it is built.</summary>
    private static readonly Dictionary<string, Type> Built = new(StringComparer.Ordinal)
    {
        ["flux:field"] = typeof(UiField),
        ["flux:label"] = typeof(UiLabel),
        ["flux:description"] = typeof(UiDescription),
        ["flux:error"] = typeof(UiError),
        ["flux:fieldset"] = typeof(UiFieldset),
        ["flux:legend"] = typeof(UiLegend),
        ["flux:heading"] = typeof(UiHeading),
        ["flux:link"] = typeof(UiLink),
        ["flux:text"] = typeof(UiText),
        ["flux:accordion"] = typeof(UiAccordion),
        ["flux:accordion.item"] = typeof(UiAccordionItem),
        ["flux:accordion.heading"] = typeof(UiAccordionHeading),
        ["flux:accordion.content"] = typeof(UiAccordionContent),
        ["flux:callout"] = typeof(UiCallout),
        ["flux:callout.heading"] = typeof(UiCalloutHeading),
        ["flux:callout.link"] = typeof(UiCalloutLink),
        ["flux:callout.text"] = typeof(UiCalloutText),
        ["flux:button"] = typeof(UiButton),
        ["flux:button.group"] = typeof(UiButtonGroup),
        ["flux:badge"] = typeof(UiBadge),
        ["flux:badge.close"] = typeof(UiBadgeClose),
        ["flux:icon.*"] = typeof(UiIcon),
        ["flux:separator"] = typeof(UiSeparator),
        ["flux:progress"] = typeof(UiProgress),
        ["flux:skeleton"] = typeof(UiSkeleton),
        ["flux:skeleton.line"] = typeof(UiSkeletonLine),
        ["flux:skeleton.group"] = typeof(UiSkeletonGroup),
        ["flux:table"] = typeof(UiTable),
        ["flux:table.columns"] = typeof(UiTableColumns),
        ["flux:table.column"] = typeof(UiTableColumn),
        ["flux:table.rows"] = typeof(UiTableRows),
        ["flux:table.row"] = typeof(UiTableRow),
        ["flux:table.cell"] = typeof(UiTableCell),
        ["flux:card"] = typeof(UiCard),
        ["flux:card.header"] = typeof(UiCardHeader),
        ["flux:card.heading"] = typeof(UiCardHeading),
        ["flux:card.subheading"] = typeof(UiCardSubheading),
        ["flux:card.actions"] = typeof(UiCardActions),
        ["flux:card.body"] = typeof(UiCardBody),
        ["flux:card.footer"] = typeof(UiCardFooter),
        ["flux:card.bleed"] = typeof(UiCardBleed),
        ["flux:toast"] = typeof(UiToast),
        ["flux:toast.group"] = typeof(UiToastGroup),
        ["flux:input"] = typeof(UiInput<>),
        ["flux:input.group"] = typeof(UiInputGroup),
        ["flux:input.group.prefix"] = typeof(UiInputGroupPrefix),
        ["flux:input.group.suffix"] = typeof(UiInputGroupSuffix),
        ["flux:textarea"] = typeof(UiTextarea<>),
        ["flux:select"] = typeof(UiSelect<>),
        ["flux:select.option"] = typeof(UiSelectOption),
        ["flux:select.group"] = typeof(UiSelectGroup),
        ["flux:select.option.create"] = typeof(UiSelectOptionCreate),
        ["flux:select.option.empty"] = typeof(UiSelectOptionEmpty),
        ["flux:select.button"] = typeof(UiSelectButton),
        ["flux:select.input"] = typeof(UiSelectInput),
        ["flux:select.search"] = typeof(UiSelectSearch),
        ["flux:autocomplete"] = typeof(UiAutocomplete),
        ["flux:autocomplete.item"] = typeof(UiAutocompleteItem),
        ["flux:pillbox"] = typeof(UiPillbox<>),
        ["flux:pillbox.option"] = typeof(UiPillboxOption),
        ["flux:pillbox.option.create"] = typeof(UiPillboxOptionCreate),
        ["flux:pillbox.option.empty"] = typeof(UiPillboxOptionEmpty),
        ["flux:pillbox.search"] = typeof(UiPillboxSearch),
        ["flux:pillbox.trigger"] = typeof(UiPillboxTrigger),
    };

    /// <summary><c>part/prop</c> or <c>part/prop=value</c> → why Rask.Ui does not carry it.</summary>
    private static readonly Dictionary<string, string> NotTranslated = new(StringComparer.Ordinal)
    {
        ["flux:error/bag"] = "Laravel's named error bags. A Rask form has one edit context, and Ui.Error reads that one.",
        ["flux:error/deep"] = "Laravel's dotted paths (fields.*). A Rask field is the member of the object that owns it: Ui.Error.For(() => order.Lines[0].Name).",
        ["flux:heading/size=2xl"] = "an identifier cannot start with a digit: Ui.HeadingSize.Xxl",
        ["flux:text/color=default"] = "no colour is an unset Color; Ui.Color holds Tailwind's hues only",
        ["flux:text/size=base"] = "the heading page's name for the text page's `default`: Ui.TextSize.Default",
        ["flux:button/as-an-input"] = "A section of the button page that shows flux:input drawn as a button; it is the input's to mirror.",
        ["flux:badge/variant=pill"] = "deprecated by Flux itself in favour of the `rounded` prop: Ui.Badge.Rounded()",
        ["flux:badge/variant=rounded"] = "not a value: the docs' deprecation note for `pill` names the `rounded` prop, and the snapshot read it as an option",
        // Sections of the icon page rather than props, recorded here so the omission is a decision.
        ["flux:icon.*/lucide-icons"] = "`php artisan flux:icon` copies Lucide SVGs into a Laravel project as Blade files; Ui.IconName is a closed, generated set.",
        ["flux:icon.*/custom-icons"] = "A Blade file under resources/views/flux/icon. In Rask a custom icon is an ordinary component drawing its own Svg.",
        ["flux:table/pagination:scroll-to"] = "Paginate takes the pager itself, not a paginator the table draws one from: where a page change scrolls to is that pager's own prop",
        ["flux:input/mask:dynamic"] = "An Alpine expression ($money($input)) evaluated in the browser on every keystroke. Rask.Ui ships no script; Mask takes the static pattern.",
        ["flux:select/enter-on-closed"] = "Flux's listbox button ignores Enter while its list is shut. Ui.Select's is a native <button popovertarget>, which Enter presses: it opens. Waiting for a runtime hook that contains Enter (and the arrows' page scroll) on a closed button[role=combobox][aria-haspopup=listbox].",
        ["flux:select/scroll-lock"] = "While a list is open Flux's script sets overflow:hidden, pointer-events:none and scrollbar-gutter:stable on <html>. A native popover does not lock the page behind it; waiting for a runtime hook that does while a [popover][data-rask-popover-open] is shown.",
        ["flux:select.option/avatar:*"] = "Props forwarded to Flux's avatar. Ui.Avatar is not Flux's yet; an option draws the extra-small round avatar Flux draws there.",
        ["flux:select.option.create/modal"] = "Opens a Flux modal by its name through Flux's script. The row's OnClick is the page's to answer, and opening a modal is one answer.",
        ["flux:autocomplete/copyable"] = "The input's own copy button, which the input does not have yet: see flux:input/copyable.",
        ["flux:autocomplete/mask:dynamic"] = "The input's: see flux:input/mask:dynamic.",
        ["flux:pillbox/keys-on-closed"] = "Flux's trigger takes Space and the arrows without the page behind it moving. Ui.Pillbox's is a <div tabindex=0 role=combobox>, which opens on them in C# — and the browser scrolls the page as well. Waiting for the runtime hook flux:select/enter-on-closed waits for, widened: contain Space, ArrowDown and ArrowUp on a collapsed [role=combobox][aria-haspopup=listbox] or [role=button][aria-haspopup=listbox] that is not a text input.",
        ["flux:pillbox/scroll-lock"] = "The same as flux:select/scroll-lock: Flux takes the page's scroll and pointer away while the list is open, which is also why nothing on its trigger hovers then.",
        ["flux:pillbox.option.create/modal"] = "Opens a Flux modal by its name through Flux's script. The row's OnClick is the page's to answer, and opening a modal is one answer.",
        ["flux:input/copyable"] = "Copies in the click's own call stack (Alpine). Rask.Ui ships no script and the runtime has no clipboard hook yet (data-rask-copy); a handler round trip loses the user activation the clipboard asks for.",
    };

    private static readonly BindingFlags Public = BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy;

    [Fact]
    public void The_snapshot_holds_the_whole_catalogue()
    {
        var parts = Parts().Select(part => part.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(53, Snapshot.RootElement.GetProperty("pages").GetArrayLength());
        Assert.All(Built.Keys, part => Assert.Contains(part, parts));
    }

    [Fact]
    public void Every_built_component_takes_what_Flux_documents()
    {
        var missing = new List<string>();

        foreach (var (part, props) in Parts().Where(part => Built.ContainsKey(part.Name)))
        {
            foreach (var prop in props.Where(prop => !IsDirective(prop.Name) && !NotTranslated.ContainsKey($"{part}/{prop.Name}")))
            {
                var property = Built[part].GetProperty(Pascal(prop.Name), Public | BindingFlags.IgnoreCase);
                if (property is null)
                {
                    missing.Add($"{part}/{prop.Name}: no {Built[part].Name}.{Pascal(prop.Name)}");
                    continue;
                }

                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (!type.IsEnum)
                {
                    continue;
                }

                missing.AddRange(prop.Options
                    .Where(option => !NotTranslated.ContainsKey($"{part}/{prop.Name}={option}"))
                    .Where(option => !Enum.GetNames(type).Contains(Pascal(option), StringComparer.OrdinalIgnoreCase))
                    .Select(option => $"{part}/{prop.Name}={option}: no {type.Name}.{Pascal(option)}"));
            }
        }

        Assert.True(missing.Count == 0, "Flux documents these and Rask.Ui does not take them:\n  " + string.Join("\n  ", missing));
    }

    private static JsonDocument Snapshot { get; } = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(RepoRoot.FullPath, "tests", "Rask.Ui.Tests", "Flux", "flux.snapshot.json")));

    private static IEnumerable<(string Name, List<(string Name, string[] Options)> Props)> Parts() =>
        from page in Snapshot.RootElement.GetProperty("pages").EnumerateArray()
        from part in page.GetProperty("parts").EnumerateArray()
        select (
            part.GetProperty("name").GetString()!,
            part.TryGetProperty("props", out var props)
                ? props.EnumerateArray().Select(prop => (
                    prop.GetProperty("name").GetString()!,
                    prop.TryGetProperty("options", out var options)
                        ? options.EnumerateArray().Select(option => option.GetString()!).ToArray()
                        : [])).ToList()
                : []);

    // wire:model, x-model, :accent — a framework directive rather than a prop of the component.
    private static bool IsDirective(string prop) =>
        prop.StartsWith("wire:", StringComparison.Ordinal) || prop.StartsWith("x-", StringComparison.Ordinal);

    // icon:trailing -> IconTrailing, tooltip:position -> TooltipPosition, 2xl -> 2xl is left to the enum's own spelling.
    private static string Pascal(string name) =>
        string.Concat(name.Split('-', ':', '.', ' ').Where(word => word.Length > 0).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
