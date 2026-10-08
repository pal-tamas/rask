using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>fluxui.dev/components/tabs, example by example.</summary>
/// <remarks>
///     <para>
///     Flux's page sets each example in a centred 384px column and shrinks most of them to their tabs, which
///     <see cref="FluxParity.Row" /> does here; the three that fill a width say which.
///     </para>
///     <para>
///     Its docs demos hand the panels a padding of their own — none, where a demo shows only the tabs — and
///     those are the <see cref="AppUtilities" /> below, under names of this page's own: what an app's own
///     Tailwind build would emit for the classes these examples pass to <c>Class</c> (<c>px-4</c>,
///     <c>pt-6!</c>).
///     </para>
/// </remarks>
public sealed partial class TabsParity : FluxParity
{
    private const string AppUtilities =
        "<style>.parity-px-4{padding-inline:1rem}.parity-px-2{padding-inline:.5rem}.parity-w-full{width:100%}"
        + ".parity-pt-0{padding-top:0}.parity-pt-6{padding-top:1.5rem}.parity-mt-2{margin-top:.5rem}</style>";

    private static readonly string[] Seven =
        ["Profile", "Account", "Billing", "Security", "Notifications", "Integrations", "API"];

    public override string Page => "tabs";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Shrunk(
            Raw.Value(AppUtilities),
            Ui.TabGroup[
                Ui.Tabs[
                    Ui.Tab.Name("profile")["Profile"],
                    Ui.Tab.Name("account")["Account"],
                    Ui.Tab.Name("billing")["Billing"]
                ],
                Bare("profile"),
                Bare("account"),
                Bare("billing")
            ]));

        yield return ("findable-tabs", Div.Style("width:510px;margin:0 auto")[
            Ui.TabGroup.Findable()[
                Ui.Tabs[
                    Ui.Tab.Name("profile").Selected()["Profile"],
                    Ui.Tab.Name("account")["Account"],
                    Ui.Tab.Name("billing")["Billing"]
                ],
                Found("profile", "Profile", "Manage your public profile and personal information.", selected: true),
                Found("account", "Account", "Update your email address, password, and security settings."),
                Found("billing", "Billing", "Invoice INV-1042 is due on September 1.")
            ]
        ]);

        yield return ("with-icons", Shrunk(
            Ui.TabGroup[
                Ui.Tabs[
                    Ui.Tab.Name("profile").Icon(Ui.IconName.User)["Profile"],
                    Ui.Tab.Name("account").Icon(Ui.IconName.Cog6Tooth)["Account"],
                    Ui.Tab.Name("billing").Icon(Ui.IconName.Banknotes)["Billing"]
                ],
                Bare("profile"),
                Bare("account"),
                Bare("billing")
            ]));

        yield return ("padded-edges", Shrunk(
            Ui.TabGroup[
                Ui.Tabs.Class("parity-px-4")[
                    Ui.Tab.Name("profile")["Profile"],
                    Ui.Tab.Name("account")["Account"],
                    Ui.Tab.Name("billing")["Billing"]
                ],
                Bare("profile"),
                Bare("account"),
                Bare("billing")
            ]));

        yield return ("scrollable-tabs", Div.Style("width:384px;margin:0 auto")[
            Ui.TabGroup[
                Ui.Tabs.Scrollable()[Seven.Select(Named)],
                Seven.Select(name => Bare(name.ToLowerInvariant()))
            ]
        ]);

        yield return ("scrollable-tabs", Div.Style("width:384px;margin:0 auto")[
            Ui.TabGroup[
                Ui.Tabs.Scrollable().ScrollableFade()[Seven.Select(Named)],
                Seven.Select(name => Bare(name.ToLowerInvariant()))
            ]
        ]);

        // Flux's demo stretches this one across its column, less the column's own 48px of padding a side.
        yield return ("segmented-tabs", Div.Style("width:288px;margin:0 auto")[
            Ui.Tabs.Segmented.Class("parity-w-full")[
                Ui.Tab["List"],
                Ui.Tab["Board"],
                Ui.Tab["Timeline"]
            ]
        ]);

        yield return ("segmented-with-icons", Shrunk(
            Ui.Tabs.Segmented[
                Ui.Tab.Icon(Ui.IconName.ListBullet)["List"],
                Ui.Tab.Icon(Ui.IconName.Squares2x2)["Board"],
                Ui.Tab.Icon(Ui.IconName.CalendarDays)["Timeline"]
            ]));

        yield return ("small-segmented-tabs", Shrunk(
            Ui.Tabs.Segmented.Size(Ui.TabsSize.Sm)[
                Ui.Tab["Demo"],
                Ui.Tab["Code"]
            ]));

        yield return ("pill-tabs", Shrunk(
            Ui.Tabs.Pills[
                Ui.Tab["List"],
                Ui.Tab["Board"],
                Ui.Tab["Timeline"]
            ]));

        // `@foreach($tabs …)` and `wire:click="addTab"`: the page's own list and its own handler. The panels
        // keep the padding the component gives them, which is the only example on the page that shows it.
        yield return ("dynamic-tabs", Shrunk(
            Ui.TabGroup[
                Ui.Tabs[
                    Ui.Tab.Name("tab-1")["Tab #1"],
                    Ui.Tab.Name("tab-2")["Tab #2"],
                    Ui.Tab.Icon(Ui.IconName.Plus).Action()["Add tab"]
                ],
                Ui.TabPanel.Name("tab-1"),
                Ui.TabPanel.Name("tab-2")
            ]));
    }

    // Flux's demo column centres a block that is as wide as what it holds; the component sits in that block
    // rather than being the flex item itself.
    private static Component Shrunk(params Component[] items) => Row(Div[items]);

    private static Component Named(string label) => Ui.Tab.Name(label.ToLowerInvariant())[label];

    // `<flux:tab.panel name="…">...</flux:tab.panel>` as the demos render it: empty, and with no padding.
    private static UiTabPanel Bare(string name) => Ui.TabPanel.Name(name).Class("parity-pt-0");

    private static Component Found(string name, string heading, string text, bool selected = false) =>
        Ui.TabPanel.Name(name).Selected(selected).Class("parity-px-2 parity-pt-6")[
            Ui.Heading[heading],
            Ui.Text.Class("parity-mt-2")[text]
        ];
}
