using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SkeletonTableExample : Component
{
    protected override Component? Render() =>
        Ui.SkeletonGroup.Shimmer[
            Ui.Table.Class("mb-0")[
                Thead[Tr[Th["Customer"], Th["Date"], Th["Status"], Th["Amount"]]],
                Tbody[Enumerable.Range(1, 5).Select(Row)]
            ]
        ];

    // The first line of each row a different width, as a column of real names would be.
    private static Component Row(int order) =>
        Tr.Key(order)[
            Td[
                Div.Class("flex items-center gap-2")[
                    Ui.Skeleton.Class("size-5 rounded-full"),
                    Div.Class("flex-1")[
                        Ui.SkeletonLine.Style(string.Create(CultureInfo.InvariantCulture, $"width:{50 + ((order * 37) % 51)}%"))
                    ]
                ]
            ],
            Td[Ui.SkeletonLine],
            Td[Ui.SkeletonLine],
            Td[Ui.SkeletonLine]
        ];
}
