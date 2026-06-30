namespace Rask.Example.Shared.Features;

// Tabs and accordion, both controlled and zero-JS. The active tab key and each accordion item's open
// flag live in this component; the nav/header handlers flip them and Rask re-renders.
public sealed class BsTabsDemo : Component
{
    private object _tab = "home";
    private bool _panel0 = true;
    private bool _panel1;

    protected override RenderResult Render() =>
    [
        Div(Class: "vstack gap-4")[
            BsTabs(Active: _tab, Tabs:
            [
                new(Key: "home", Title: "Home", OnSelect: () => _tab = "home",
                    Content: P(Class: "pt-3 mb-0")["The Home pane. Only the active pane is rendered."]),
                new(Key: "profile", Title: "Profile", OnSelect: () => _tab = "profile",
                    Content: P(Class: "pt-3 mb-0")["The Profile pane."]),
                new(Key: "contact", Title: "Contact", OnSelect: () => _tab = "contact",
                    Content: P(Class: "pt-3 mb-0")["The Contact pane."])
            ]),
            BsAccordion()[
                BsAccordionItem(Title: "What drives these?", Open: _panel0, OnToggle: () => _panel0 = !_panel0)[
                    P(Class: "mb-0")["Each item owns its open state — toggle it however you like (single- or multi-open)."]
                ],
                BsAccordionItem(Title: "Any JavaScript?", Open: _panel1, OnToggle: () => _panel1 = !_panel1)[
                    P(Class: "mb-0")["None. The .show/.collapse classes are toggled by the live runtime."]
                ]
            ]
        ]
    ];
}
