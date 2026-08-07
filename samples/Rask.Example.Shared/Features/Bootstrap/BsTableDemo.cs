namespace Rask.Example.Shared.Features;

// A Bootstrap table — the typed style toggles (Striped/Hover/Bordered/…) map to the .table-* classes.
// Children are the usual core Thead/Tbody/Tr/Th/Td markup. For typed columns, sorting and paging, reach
// for BsDataGrid<T> instead (see the data-grid guide).
public sealed partial class BsTableDemo : Component
{
    protected override Component? Render() =>
        BsTable(Striped: true, Hover: true, Bordered: true)[
            Thead()[
                Tr()[Th()["#"], Th()["Name"], Th()["Role"]]
            ],
            Tbody()[
                Tr()[Td()["1"], Td()["Ada Lovelace"], Td()["Engineer"]],
                Tr()[Td()["2"], Td()["Grace Hopper"], Td()["Admiral"]],
                Tr()[Td()["3"], Td()["Alan Turing"], Td()["Codebreaker"]]
            ]
        ];
}
