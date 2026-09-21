using Microsoft.EntityFrameworkCore;
using Rask.Core;
using Rask.Data;

namespace Rask.SQLite.Browser.Fixture.Wasm;

/// <summary>
///     Three searches behind three buttons, and the matches with their marked terms. The E2E asserts the titles, the
///     <c>&lt;mark&gt;</c> each highlight becomes, and the snippet — every part of the full-text surface, run in the browser.
/// </summary>
public sealed partial class App(IDbContextFactory<ArticleContext> contexts, SchemaReady schema) : Component
{
    private string _status = "loading";
    private List<(int Id, string? Title, string? Excerpt)> _hits = [];

    protected override Component? HeadAssets => [
        Title["Full-text search in the browser"],
        Meta.Charset("utf-8"),
    ];

    protected override async Task OnMountAsync()
    {
        try
        {
            await schema.Task;
            _status = "ready";
        }
        catch (Exception error)
        {
            _status = "error: " + error;
        }
    }

    protected override Component? Render() =>
        Main[
            H1["Full-text search in the browser"],
            P.Id("status")[_status],
            Button.Id("search-sqlite").OnClick(() => SearchAsync("sqlite"))["sqlite"],
            // The last word also matches as a prefix.
            Button.Id("search-prefix").OnClick(() => SearchAsync("offline stor"))["offline stor"],
            Button.Id("search-nothing").OnClick(() => SearchAsync("zebra"))["zebra"],
            Ul.Id("hits")[_hits.Select(h => Li.Key(h.Id).Class("hit")[
                Strong.Class("title")[Marked(h.Title)],
                " — ",
                Span.Class("excerpt")[Marked(h.Excerpt)]
            ])]
        ];

    private async Task SearchAsync(string text)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync();
            var hits = await db.Articles.Search(text)
                .Select(a => new { a.Id, Title = FullText.Highlight(a.Title), Excerpt = FullText.Snippet(a.Body, 6) })
                .ToListAsync();
            _hits = hits.Select(h => (h.Id, h.Title, h.Excerpt)).ToList();
            _status = $"searched '{text}': {_hits.Count}";
        }
        catch (Exception error)
        {
            _status = "error: " + error;
        }
    }

    // The database marks each matched term with FullText.MatchStart/MatchEnd; a page shows them as <mark>.
    private static List<Component> Marked(string? text)
    {
        var parts = new List<Component>();
        foreach (var (segment, index) in (text ?? string.Empty).Split(FullText.MatchStart).Select((s, i) => (s, i)))
        {
            var end = segment.IndexOf(FullText.MatchEnd, StringComparison.Ordinal);
            if (index == 0 || end < 0)
            {
                if (segment.Length > 0)
                {
                    parts.Add(segment);
                }

                continue;
            }

            parts.Add(Mark[segment[..end]]);
            if (end + 1 < segment.Length)
            {
                parts.Add(segment[(end + 1)..]);
            }
        }

        return parts;
    }
}
