namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PopoverEventExample : Component
{
    private string _answer = "going";

    protected override Component? Render() =>
        // Centred on a phone: a panel centred under a trigger at the page's edge would hang off it.
        Div.Class("flex max-sm:justify-center")[
            Ui.Dropdown.Bottom.Center[
                Button.Type(ButtonType.Button)
                    .Class("flex w-54 items-center gap-2 rounded-lg bg-zinc-100 p-2 text-start hover:bg-zinc-200 "
                           + "dark:bg-zinc-700 dark:hover:bg-zinc-600")[
                    Div.Class("w-0.5 self-stretch rounded-full bg-zinc-800 dark:bg-zinc-200"),
                    Div[Ui.Heading["Team sync"], Ui.Text.Class("mt-1")["10:00 AM"]]
                ],
                // data-open:flex: an open panel says so, so its layout applies only while it is up.
                Ui.Popover.Class("w-80 p-0 data-open:flex flex-col")[
                    Div.Class("p-4")[
                        Ui.Heading["Team sync"],
                        Ui.Text.Class("mt-2")["Let's review the progress made last week and define the priorities for the next"],
                        Ui.RadioGroup.Value(_answer).Id("popover-answer").Segmented.Class("mt-3").OnChange(v => { _answer = v; })[
                            Ui.Radio.Value("going").Label("Going"),
                            Ui.Radio.Value("not-going").Label("Not going"),
                            Ui.Radio.Value("maybe").Label("Maybe")
                        ]
                    ],
                    Ui.Separator.Key("s1"),
                    Detail(Ui.IconName.Users, Ui.Heading["Guests"], "Invite",
                        Div.Class("mt-2 flex items-center gap-3")[
                            Ui.AvatarGroup.Class("dark:*:ring-zinc-700")[
                                Ui.Avatar.Xs.Circle().Name("Mara Lind"),
                                Ui.Avatar.Xs.Circle().Name("Theo Brandt"),
                                Ui.Avatar.Xs.Circle().Name("Nia Okafor")
                            ],
                            Ui.Text["1 maybe, 1 awaiting"]
                        ]),
                    Ui.Separator.Key("s2"),
                    Detail(Ui.IconName.Clock,
                        Div.Class("flex gap-2")[
                            Ui.Heading["10:00 AM"],
                            Ui.Icon.Name(Ui.IconName.ArrowRight).Micro.Class("text-zinc-400"),
                            Ui.Heading["11:00 AM"]
                        ],
                        "Edit",
                        Div.Class("mt-2 flex gap-3")[
                            Ui.Text["May 29 2025"],
                            Ui.Text["·"],
                            Div.Class("flex items-center gap-1.5")[
                                Ui.Icon.Name(Ui.IconName.ArrowPathRoundedSquare).Micro.Class("text-zinc-400"),
                                Ui.Text["Every weekday"]
                            ]
                        ]),
                    Ui.Separator.Key("s3"),
                    Detail(Ui.IconName.MapPin, Ui.Heading["Location"], "Edit",
                        Ui.Text.Class("mt-2")["Paris HQ, 13 rue de la Prairie 75018"])
                ]
            ]
        ];

    // One block of the event: an icon, and beside it a heading with a quiet link at its far edge, over a detail.
    private static Component Detail(Ui.IconName icon, Component heading, string link, Component detail) =>
        Div.Class("flex gap-2 p-4")[
            Ui.Icon.Name(icon).Micro.Class("text-zinc-400"),
            Div.Class("flex-1")[
                Div.Class("flex items-center justify-between")[
                    heading,
                    Ui.Text[Ui.Link.As(Ui.LinkAs.Button).Variant(Ui.LinkVariant.Subtle)[link]]
                ],
                detail
            ]
        ];
}
