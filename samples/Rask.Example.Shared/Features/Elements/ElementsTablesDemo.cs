namespace Rask.Example.Shared.Features;

// Tables: table, caption, colgroup/col, thead/tbody/tfoot, tr, th (scope), td (colspan).
public sealed partial class ElementsTablesDemo : Component
{
    protected override Component? Render() => Table.Class($"{Ui.Table} text-sm [&_td]:border [&_th]:border mb-0")[
        Caption.Class("caption-top")["Quarterly results"],
        Colgroup[Col.Span(1).Class("bg-slate-100"), Col.Span(2)],
        Thead[
            Tr[Th.Scope("col")["Region"], Th.Scope("col")["Q1"], Th.Scope("col")["Q2"]]
        ],
        Tbody[
            Tr[Th.Scope("row")["North"], Td["10"], Td["12"]],
            Tr[Th.Scope("row")["South"], Td["8"], Td["15"]]
        ],
        Tfoot[
            Tr[Th.Scope("row")["Total"], Td.Colspan(2).Class("text-right font-bold")["45"]]
        ]
    ];
}
