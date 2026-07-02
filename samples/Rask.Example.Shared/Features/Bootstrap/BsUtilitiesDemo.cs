namespace Rask.Example.Shared.Features;

// Typed Bootstrap utility classes from Rask.Bootstrap: each group is a static class of typed tokens
// (Shadow/Border/Margin/Padding/Display/Flex/Rounded/Txt/Sizing/Position/Bg), composed into a Class
// string with Bs.Join — responsive variants take a Bp breakpoint. No stringly-typed class names.
public sealed class BsUtilitiesDemo : Component
{
    private static Component Tile(string label, string? extra = null) =>
        Div(Class: Bs.Join(Bg.BodyTertiary, Border.All, Rounded.Default, Padding.All(3), Txt.Center(), extra))[label];

    protected override Component? Render() =>
    [
        Div(Class: Bs.Join(Display.Flex(), Flex.Column(), Flex.Gap(4)))[

            // Shadow
            Section("Shadow", Div(Class: Bs.Join(Display.Flex(), Flex.Wrap(), Flex.Gap(3)))[
                Div(Class: Bs.Join(Bg.White, Rounded.Default, Padding.All(3), Shadow.Sm))["Shadow.Sm"],
                Div(Class: Bs.Join(Bg.White, Rounded.Default, Padding.All(3), Shadow.Default))["Shadow.Default"],
                Div(Class: Bs.Join(Bg.White, Rounded.Default, Padding.All(3), Shadow.Lg))["Shadow.Lg"]
            ]),

            // Spacing (margin / padding)
            Section("Spacing", Div(Class: Bs.Join(Display.Flex(), Flex.Align(BsAlign.Start), Flex.Gap(3)))[
                Div(Class: Bs.Join(Bg.Color(BsColor.Primary), Rounded.Default, Padding.All(1)))[
                    Div(Class: Bs.Join(Bg.White, Padding.All(3)))["Padding.All(3) inside Padding.All(1)"]
                ],
                Tile("Margin.Top(4)", Margin.Top(4))
            ]),

            // Display + Flex
            Section("Display & Flex",
                Div(Class: Bs.Join(Display.Flex(), Flex.Justify(BsJustify.Between), Flex.Align(BsAlign.Center),
                    Border.All, Rounded.Default, Padding.All(2)))[
                    Span()["justify-content-between"],
                    BsBadge(Color: BsColor.Secondary)["align-items-center"]
                ]),

            // Text / typography
            Section("Text",
                Div(Class: Bs.Join(Display.Flex(), Flex.Column(), Flex.Gap(1)))[
                    P(Class: Bs.Join(Txt.Center(), Font.Bold, Margin.Bottom(0)))["Txt.Center + Font.Bold"],
                    P(Class: Bs.Join(Txt.End(), Txt.Color(BsColor.Danger), Margin.Bottom(0)))["Txt.End + Txt.Color(Danger)"],
                    P(Class: Bs.Join(Txt.Uppercase, Txt.Muted, Font.Size(6), Margin.Bottom(0)))["Txt.Uppercase + Txt.Muted"]
                ]),

            // Rounded + Background
            Section("Rounded & Background", Div(Class: Bs.Join(Display.Flex(), Flex.Wrap(), Flex.Gap(2)))[
                Span(Class: Bs.Join(Bg.Color(BsColor.Primary), Txt.Color(BsColor.Light), Rounded.Pill, Padding.X(3), Padding.Y(2)))["Rounded.Pill"],
                Span(Class: Bs.Join(Bg.Color(BsColor.Success), Txt.Color(BsColor.Light), Rounded.Default, Padding.X(3), Padding.Y(2)))["Rounded.Default"],
                Span(Class: Bs.Join(Bg.Color(BsColor.Dark), Txt.Color(BsColor.Light), Rounded.None, Padding.X(3), Padding.Y(2)))["Rounded.None"]
            ]),

            // Sizing
            Section("Sizing", Div(Class: Bs.Join(Display.Flex(), Flex.Column(), Flex.Gap(2)))[
                Div(Class: Bs.Join(Bg.Color(BsColor.Info), Txt.Color(BsColor.Light), Padding.All(2), Sizing.W(25)))["Sizing.W(25)"],
                Div(Class: Bs.Join(Bg.Color(BsColor.Info), Txt.Color(BsColor.Light), Padding.All(2), Sizing.W(50)))["Sizing.W(50)"],
                Div(Class: Bs.Join(Bg.Color(BsColor.Info), Txt.Color(BsColor.Light), Padding.All(2), Sizing.W(100)))["Sizing.W(100)"]
            ]),

            // Responsive breakpoints
            Section("Responsive (Bp)",
                Div(Class: Bs.Join(Bg.BodyTertiary, Border.All, Rounded.Default, Padding.All(3),
                    Txt.Center(), Txt.Center(Bp.Md)))[
                    "Bs.Join(Txt.Center(), Display.Flex(Bp.Lg), Margin.Bottom(4, Bp.Md)) → text-center d-lg-flex mb-md-4"
                ])
        ]
    ];

    private static Component Section(string title, Component body) =>
        Div()[
            H6(Class: Bs.Join(Txt.Uppercase, Txt.Muted, Font.Bold, Margin.Bottom(2)))[title],
            body
        ];
}
