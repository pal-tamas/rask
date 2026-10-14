using Rask.Core.Globalization;
using static Rask.Markup;

namespace Rask;

/// <summary>
///     The country half of <see cref="UiPhone" />: Flux's listbox button and the searchable list it opens.
/// </summary>
/// <remarks>
///     <para>
///     Drawn once, with no handler anywhere: the list is a native popover the button opens by itself, and the
///     phone engine (<c>Resources/phone/ui-phone.ts</c>) builds its rows from the library's countries the first
///     time it opens, filters them, moves the cursor and picks — in the browser, so none of it is a round trip.
///     The rows are not the render's (<c>data-rask-managed</c>); the button's copy of the chosen country is the
///     engine's once it runs (<c>data-rask-opaque</c>), and the render draws only where it starts.
///     </para>
///     <para>
///     The select family draws its own cursor in C# today; when its list moves into the runtime's list hook, this
///     one moves there too, and the engine keeps the number and the library's countries.
///     </para>
/// </remarks>
internal static class UiPhonePicker
{
    /// <summary>The picker for the field whose control is <paramref name="id" />, showing <paramref name="country" />.</summary>
    internal static Component Draw(string id, UiPhoneCountry? country, Ui.PhoneSize size, Ui.PhoneVariant variant, bool disabled)
    {
        var anchor = id + "-country";
        var panel = id + "-countries";
        var list = id + "-options";
        var label = RaskStrings.Get(RaskString.PhoneChangeCountry, "Change country");

        return Div.Class(UiPhoneLook.Country).Data(("ui-phone-country", null), ("ui-control", null), ("ui-select", null))[
            Button
                .Type(ButtonType.Button)
                .Role("combobox")
                .Disabled(disabled)
                .Aria(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["label"] = country is null ? label : $"{label}: {country.Name} (+{country.DialCode})",
                    ["controls"] = list,
                    ["haspopup"] = "listbox",
                    ["expanded"] = "false",
                })
                .Attributes(new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["data-country-label"] = label,
                    ["data-ui-group-target"] = null,
                    ["data-ui-select-button"] = null,
                    ["popovertarget"] = panel,
                    ["style"] = "anchor-name:--" + anchor,
                })
                .Class(UiPhoneLook.Button(size, variant))[
                Initial(country, size, variant),
                // The engine's from the moment it runs: it swaps the one shown for the other from its template.
                Div.Class(UiPhoneLook.Selected(size, variant)).Data("rask-opaque", "")[
                    Template.Attributes(("name", "placeholder"))[Globe()],
                    Template.Attributes(("name", "option"))[Picked(UiPhoneCountries.All[0], size)],
                    country is null ? Globe() : Picked(country, size)
                ],
                Ui.Icon.Name(Ui.IconName.ChevronDown).Mini.Class(UiPhoneLook.Chevron)
            ],
            Popup(panel, list, anchor, country, disabled)
        ];
    }

    private static Component Globe() =>
        Span.Class(UiPhoneLook.Globe).Data("ui-select-placeholder", "")[Ui.Icon.Name(Ui.IconName.GlobeAlt).Micro.Class("size-5")];

    private static Component Picked(UiPhoneCountry country, Ui.PhoneSize size) =>
        Div.Class(UiSelectLook.Picked).Data("value", country.Code.ToLowerInvariant())[Content(country, size)];

    // Flux's first drawing of the country, which its script hides at once: kept for the same markup.
    private static Component Initial(UiPhoneCountry? country, Ui.PhoneSize size, Ui.PhoneVariant variant) =>
        Span.Class(UiPhoneLook.Initial(size, variant)).Data("ui-phone-country-initial", "")[
            country is null ? null : Ui.Flag.Xs.Country(country.Code).Class(UiPhoneLook.Flag(size)),
            country is null ? null : Span["+" + country.DialCode]
        ];

    private static Component Popup(string panel, string list, string anchor, UiPhoneCountry? country, bool disabled) =>
        Div
            .Id(panel)
            .Popover(Popover.Auto)
            .Class(UiPhoneLook.Popup)
            .Attributes(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["data-ui-options"] = null,
                // While the list is open the page behind it neither scrolls nor takes the pointer, as on Flux.
                ["data-rask-lock"] = null,
                ["style"] = UiAnchor.Under(anchor, null, null),
                ["data-rask-place"] = UiAnchor.Place(null, null),
            })[
            Search(disabled),
            Div.Id(list).Role("listbox").TabIndex(-1).Aria("multiselectable", "true").Class(UiSelectLook.SearchedList)[
                Div.Class(UiListboxLook.Empty).Data("ui-listbox-empty", "")[RaskStrings.Get(RaskString.SelectEmpty, "No results found")],
                // The row every country's is made from, by the engine.
                Template.Data("ui-phone-option", "")[Row(null, list)],
                // The chosen country's row, as Flux draws it before its list is opened: the engine takes it in.
                country is null ? null : Row(country, list)
            ]
        ];

    private static Component Search(bool disabled) =>
        Div.Class(UiSelectLook.Search).Data("ui-select-search", "")[
            Div.Class(UiSelectLook.SearchIcon)[Ui.Icon.Name(Ui.IconName.MagnifyingGlass).Micro],
            Input.Of<string>()
                .Type(InputType.Text)
                .Placeholder(RaskStrings.Get(RaskString.SelectSearchPlaceholder, "Search..."))
                .Role("combobox")
                .Autocomplete("off")
                .Autofocus()
                .Disabled(disabled)
                // What is typed here finds a country; the number is the value (rask-leave.ts).
                .Attributes(new Dictionary<string, string?>(StringComparer.Ordinal) { [UiRuntimeMarks.Unguarded] = null })
                .Class(UiSelectLook.SearchInput),
            Div.Class(UiSelectLook.SearchClear)[
                Button
                    .Type(ButtonType.Button)
                    .TabIndex(-1)
                    .Class(UiSelectLook.SearchClearButton)
                    .Aria("label", RaskStrings.Get(RaskString.SelectSearchClear, "Clear command input"))
                    .Data("ui-button", "")[
                    Ui.Icon.Name(Ui.IconName.XMark).Micro
                ]
            ]
        ];

    private static Component Row(UiPhoneCountry? country, string list)
    {
        var row = Div.Role("option").Class(UiListboxLook.Option).Data("ui-option", "");
        row = country is null
            ? row.Aria("selected", "false")
            : row.Id(list + "-" + country.Code.ToLowerInvariant())
                .Attributes(("value", country.Code.ToLowerInvariant()))
                .Aria("selected", "true")
                .Data("selected", "");

        return row[Content(country ?? UiPhoneCountries.All[0], Ui.PhoneSize.Base)];
    }

    // What a row holds, and the button's copy of it: the tick's place, then the flag, the name and the code.
    private static Component Content(UiPhoneCountry country, Ui.PhoneSize size) =>
        Div.Class(UiListboxLook.OptionBody)[
            Div.Class(UiListboxLook.OptionLead)[
                Div.Class(UiListboxLook.Indicator)[Ui.Icon.Name(Ui.IconName.Check).Mini.Class(UiListboxLook.Check)]
            ],
            Div.Class(UiListboxLook.OptionText)[
                Div.Class(UiListboxLook.Words)[
                    Div.Class(UiPhoneLook.CountryRow(size))[
                        Ui.Flag.Xs.Country(country.Code).Class(UiPhoneLook.Flag(size)).AlsoMarked("ui-phone-country-flag"),
                        Span.Class(UiPhoneLook.Name).Data("country-name", "")[country.Name],
                        Span.Class(UiPhoneLook.Code).Data("country-code", "")["+" + country.DialCode],
                        Span.Class(UiPhoneLook.Iso).Data("country-iso", "").Aria("hidden", "true")[country.Code.ToLowerInvariant()]
                    ]
                ]
            ]
        ];
}
