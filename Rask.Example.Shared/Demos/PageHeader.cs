namespace Rask.Example.Shared.Demos;

internal static class PageHeader
{
    public static Component Render(string title, string lead) =>
        Div(Class: "mb-4 pb-3 border-bottom", Children:
        [
            H1(Class: "h2 fw-bold mb-2", Children: [title]),
            P(Class: "lead text-secondary mb-0", Children: [lead])
        ]);
}
