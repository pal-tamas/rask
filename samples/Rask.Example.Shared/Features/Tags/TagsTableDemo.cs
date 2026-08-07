namespace Rask.Example.Shared.Features;

public sealed partial class TagsTableDemo : Component
{
    protected override Component? Render() => Table(Class: "table table-sm mb-0")[
        Thead()[Tr()[Th()["#"], Th()["Tag"]]],
        Tbody()[
            Tr()[Td()["1"], Td()[Code()["Div"]]],
            Tr()[Td()["2"], Td()[Code()["Span"]]]
        ]
    ];
}
