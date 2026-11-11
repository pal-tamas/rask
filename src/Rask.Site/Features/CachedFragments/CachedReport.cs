namespace Rask.Site.Features;

/// <summary>
///     The expensive part: an aggregate drawn as a table. Its <c>OnMount</c> is where a real report would query; here it
///     counts, so the page can show whether it was drawn or handed the kept markup.
/// </summary>
public sealed partial class CachedReport : Component
{
    private int _draw;

    /// <summary>Where each draw is counted.</summary>
    public ReportDraws? Draws { get; set; }

    protected override Task OnMount()
    {
        if (Draws is { } draws)
        {
            _draw = ++draws.Count;
        }

        return Task.CompletedTask;
    }

    protected override Component? Render() =>
        Table.Id("cached-report").Class("text-sm")[
            Caption.Class("text-left text-zinc-500")[$"Report, draw #{_draw}"],
            Tbody[
                Regions.Select(r => Tr.Key(r.Name)[Td.Class("pr-6")[r.Name], Td[Format.Number(r.Total)]])
            ]
        ];

    private static readonly (string Name, decimal Total)[] Regions =
    [
        ("North", 12_400m),
        ("South", 9_870m),
        ("East", 15_210m)
    ];
}
