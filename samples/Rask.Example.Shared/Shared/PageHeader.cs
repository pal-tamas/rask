namespace Rask.Example.Shared;

internal static class PageHeader
{
    public static Component Render(string title, string lead) =>
        Div(Class: "mb-4 pb-3 border-bottom")[
            H1(Class: "h2 fw-bold mb-2")[title],
            P(Class: "lead text-secondary mb-0")[lead]
        ];
}
