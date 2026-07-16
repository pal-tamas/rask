namespace Rask.Example.Shared.Features;

// A Bootstrap list group — items carrying Active/Disabled/Color state, a linked (Href) action item that
// gets .list-group-item-action, plus the Numbered (ordered, auto-numbered <ol>) and Flush (borderless)
// list variants.
public sealed class BsListGroupDemo : Component
{
    protected override Component? Render() =>
        Div(Class: "row g-3")[
            Div(Class: "col-md-6")[
                BsListGroup()[
                    BsListGroupItem(Active: true)["Active item"],
                    BsListGroupItem()["A second item"],
                    BsListGroupItem(Disabled: true)["A disabled item"],
                    BsListGroupItem(Color: BsColor.Success)["A tinted item"],
                    BsListGroupItem(Href: "#")["A linked action item"]
                ]
            ],
            Div(Class: "col-md-6 d-flex flex-column gap-3")[
                BsListGroup(Numbered: true)[
                    BsListGroupItem()["First"],
                    BsListGroupItem()["Second"],
                    BsListGroupItem()["Third"]
                ],
                BsListGroup(Flush: true)[
                    BsListGroupItem()["Flush one"],
                    BsListGroupItem()["Flush two"]
                ]
            ]
        ];
}
