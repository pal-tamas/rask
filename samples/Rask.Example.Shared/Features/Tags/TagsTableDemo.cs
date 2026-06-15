namespace Rask.Example.Shared.Features;

public sealed class TagsTableDemo : Component
{
    protected override RenderResult Render() => Table(Class: "table table-sm mb-0")[
        Thead()[Tr()[Th()["#"], Th()["Tag"]]],
        Tbody()[
            Tr()[Td()["1"], Td()[Code()["Div"]]],
            Tr()[Td()["2"], Td()[Code()["Span"]]]
        ]
    ];
}
