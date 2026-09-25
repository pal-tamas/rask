using Rask.Data;
using Rask.Query;
using Rask.SQLite.Browser;

namespace Rask.Site.DataDemo;

/// <summary>
///     Notes, in a database inside the tab: a form that saves one, a list that refreshes itself when it does, and a
///     search box whose matches come back ranked with the matched words marked.
/// </summary>
/// <remarks>
///     Nothing here loads data by hand. Both lists are <c>Rask.Query</c> queries keyed <c>QueryKey.For&lt;Note&gt;</c>,
///     so the save — which reports that it wrote a <see cref="Note" /> — refetches them, and the search follows the box.
/// </remarks>
public sealed partial class App(NotesReady ready, BrowserSqliteOwnership ownership) : Component
{
    // No Tailwind build here: the kit's sheet carries the components, and these few rules lay the page out.
    private const string Layout = """
        *,*::before,*::after{box-sizing:border-box}
        body{margin:0;background:var(--color-base-100);color:var(--color-base-content);
          font:15px/1.5 system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}
        h1,h2,p,ul{margin:0}
        .notes{max-width:44rem;margin:0 auto;padding:1rem;display:flex;flex-direction:column;gap:1.25rem}
        .notes-lede{opacity:.75;font-size:.875rem}
        .notes-form{display:flex;flex-direction:column;gap:.75rem}
        .notes-form .btn{align-self:flex-start}
        .notes .fieldset,.notes .floating-label,.notes .input,.notes .textarea{width:100%}
        .notes textarea{font:inherit}
        .notes h2{font-size:1rem;font-weight:600}
        .notes-list{padding:0;list-style:none}
        .notes-list>li{display:flex;flex-direction:column;gap:.125rem;padding:.625rem 0;
          border-bottom:1px solid color-mix(in oklab,var(--color-base-content) 12%,transparent);overflow-wrap:anywhere}
        .notes-list>li:last-child{border-bottom:0}
        .note-body{opacity:.8;font-size:.875rem}
        .notes mark{background:color-mix(in oklab,var(--color-warning) 45%,transparent);color:inherit;border-radius:.2em}
        .notes-foot{opacity:.65;font-size:.75rem}
        """;

    private NoteModel _draft = new();
    private int _form;
    private string _search = "";
    private string? _saveError;
    private bool? _owner;

    /// <inheritdoc />
    protected override Component? HeadAssets =>
    [
        Title["Notes in the browser — a Rask full-stack demo"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Style[Raw.Value(UiStylesheet.Css)],
        Style[Raw.Value(Layout)],
    ];

    /// <summary>Turns the kit's theme on for the whole document; without the scope every kit colour resolves to nothing.</summary>
    protected override Component Shell(Component head, Component body) =>
        Html.Lang("en").Attributes((UiStylesheet.ThemeScopeAttribute, ""))[head, Body[body]];

    /// <inheritdoc />
    protected override async Task OnMount() => _owner = await ownership.Resolved;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var term = _search.Trim();

        // Every note, newest first. Keyed by the aggregate, so a save of a Note refetches it.
        var notes = QueryClient.Query(QueryKey.For<Note>("recent"), async ct =>
        {
            await ready.Task;
            return await Note.Read.OrderByDescending(n => n.CreatedAt).Take(50).ToListAsync(ct);
        });

        // The search: follows the box, and pauses while it is empty (a null input fetches nothing).
        var hits = QueryClient.Query(QueryKey.For<Note>("search"), term.Length == 0 ? null : term,
            async (text, ct) =>
            {
                await ready.Task;
                return await Note.Read.Search(text)
                    .Take(20)
                    .Select(n => new Hit(n.Id, FullText.Highlight(n.Title), FullText.Snippet(n.Body, 12)))
                    .ToListAsync(ct);
            });

        return Main.Class("notes")[
            Div[
                Ui.Heading.Level(1).Size(Ui.Size.Lg)["Notes"],
                P.Class("notes-lede")[
                    "A SQLite database inside this browser tab, written through EF Core and Rask.Data. "
                    + "Add a note, search for it, reload the page — it is still here."
                ]
            ],
            _owner == false
                ? Ui.Alert.Id("not-owner").Tone(Ui.Tone.Warning)[
                    "This demo is open in another tab, which owns its database. Close that tab and reload this one "
                    + "to see your notes."]
                : null,
            notes.Error is { } failed
                ? Ui.Alert.Id("status").Tone(Ui.Tone.Error)["The database did not open: " + failed.Message]
                : null,

            Form.Model(_draft).Key(_form).OnSubmit(AddAsync).Class("notes-form")[f => [
                Ui.Input.Bind(() => _draft.Title).Id("note-title").Label("Title").MaxLength(120),
                Ui.Textarea.Bind(() => _draft.Body).Id("note-body").Label("Note").Rows(3),
                Ui.Button.Id("add-note").Type(Ui.ButtonType.Submit).Tone(Ui.Tone.Primary)
                    .Disabled(f.Submitting).Loading(f.Submitting)["Add note"],
                _saveError is null ? null : Ui.Alert.Tone(Ui.Tone.Error)[_saveError]
            ]],

            Ui.Input.Value(_search).Id("search").Label("Search notes").Icon(Ui.IconName.Search)
                .Type(InputType.Search).Placeholder("Try “offline”, or a word from your note")
                .OnInput(v => _search = v ?? ""),

            term.Length > 0 ? Results(term, hits) : Recent(notes),

            P.Class("notes-foot")[
                "Nothing leaves your browser: the database is saved to IndexedDB every two seconds and restored "
                + "before the page reads it."
            ]
        ];
    }

    private static Component Recent(Query<List<NoteRead>> notes) =>
        Section.Aria("label", "Your notes")[
            H2["Your notes"],
            notes.Data is not { } rows
                ? Ui.Loading.Text("Opening the database…")
                : Ul.Id("notes").Class("notes-list")[rows.Select(n => Li.Key(n.Id).Class("note")[
                    Strong.Class("note-title")[n.Title],
                    n.Body.Length > 0 ? Span.Class("note-body")[n.Body] : null
                ])]
        ];

    private static Component Results(string term, Query<List<Hit>> hits) =>
        Section.Aria("label", "Search results")[
            H2.Id("results-heading")[hits.Data is { } found ? $"{found.Count} {(found.Count == 1 ? "match" : "matches")} for “{term}”" : $"Searching for “{term}”…"],
            hits.Data is { Count: 0 }
                ? P.Id("no-hits").Class("note-body")["Nothing matches every word. The last word also matches as a prefix."]
                : Ul.Id("hits").Class("notes-list")[(hits.Data ?? []).Select(h => Li.Key(h.Id).Class("hit")[
                    Ui.Highlight.Text(h.Title ?? "").Class("hit-title"),
                    Ui.Highlight.Text(h.Excerpt ?? "").Class("note-body")
                ])]
        ];

    private async Task AddAsync(NoteModel note)
    {
        try
        {
            // Refreshes the two queries above: the save reports that it wrote a Note.
            await Note.CreateAsync(note, cancellationToken: CancellationToken);

            _saveError = null;
            _draft = new();
            _form++;   // a fresh form: the fields and their touched state start over
        }
        catch (Exception error)
        {
            _saveError = "The note was not saved: " + error.Message;
        }
    }

    /// <summary>One search match: the title with its matched words marked, and the best passage of the body.</summary>
    public sealed record Hit(Guid Id, string? Title, string? Excerpt);
}
