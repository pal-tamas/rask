namespace Rask.Site.Features;

// Tables: table, caption, colgroup/col, thead/tbody/tfoot, tr, th (scope), td (colspan).
public sealed partial class ElementsTablesDemo : Component
{
    protected override Component? Render() => Ui.Table.Class("[&_td]:border [&_th]:border mb-0")[
        Caption.Class("caption-top")["Quarterly results"],
        Colgroup[Col.Span(1).Class("bg-ui-well"), Col.Span(2)],
        Thead[
            Tr[Th.Scope(ThScope.Col)["Region"], Th.Scope(ThScope.Col)["Q1"], Th.Scope(ThScope.Col)["Q2"]]
        ],
        Tbody[
            Tr[Th.Scope(ThScope.Row)["North"], Td["10"], Td["12"]],
            Tr[Th.Scope(ThScope.Row)["South"], Td["8"], Td["15"]]
        ],
        Tfoot[
            Tr[Th.Scope(ThScope.Row)["Total"], Td.ColSpan(2).Class("text-right font-bold")["45"]]
        ]
    ];
}
