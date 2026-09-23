# Full-text search — ranked search over your own tables

> **In practice:** [Rask.Data](data.md) · [data grid](data-grid.md) · [Rask.SQLite](sqlite.md) · [cheat sheet](cheatsheet.md).

A search box over your own rows usually starts as `Where(p => p.Title.Contains(q))`, which is a
`LIKE '%q%'` scan of every row, matches `sql` inside `nosql`, cannot rank, and misses `kérés` when
someone types `keres`. Both databases Rask runs on ship a real full-text engine — SQLite's
[FTS5](https://sqlite.org/fts5.html) and PostgreSQL's `tsvector` — but EF Core supports neither. Rask adds
it: declare the index on the model, and search it from LINQ. The same declaration and the same query work on
**SQLite**, on **PostgreSQL**, and **in the browser**.

```csharp
builder.HasFullTextSearch(p => new { p.Title, p.Body });   // in the entity's configuration

var hits = await db.Set<Post>().Search(query).Where(p => p.Published).Take(20).ToListAsync();
var page = await Post.Read.Search(query).Take(20).ToListAsync();      // Rask.Data's read face
Ui.DataGrid.Data(Post.Read.Search(query).AsQueryable())                // a grid that sorts and pages in SQL
```

## What `Search(text)` does

`Search(text)` returns the rows containing every word of `text`, **best match first**, as an ordinary query:
`Where`, `Skip`/`Take`, `CountAsync` and projections keep composing, and a later `OrderBy` replaces the rank
order — so a data grid's column sort does the obvious thing. `ThenBy` after `Search` breaks ties within the
rank. It works on a read face, on a `ModelQuery` and on any EF Core `IQueryable` of an indexed entity.

**What the user types is words, not a query language.** Each word must appear (any order, any case,
diacritics ignored), the last word also matches as a prefix so results narrow while typing, and the engine's
own syntax — `OR`, `NOT`, `NEAR(…)`, `column:`, `&`, `|`, quotes, `*` — is just text. A stray `"` can never
become a syntax error, and a search box can never become a query someone did not mean to write. Text with no
word in it (empty, blank, punctuation) filters nothing, so an empty box lists everything.

## Showing why each row matched

Project the matched terms:

```csharp
var hits = await Post.Read.Search(query)
    .Select(p => new { p.Id, Title = FullText.Highlight(p.Title), Excerpt = FullText.Snippet(p.Body, 12) })
    .ToListAsync();

// in markup
Ui.Highlight.Text(hit.Excerpt)
```

`Highlight` returns the whole value and `Snippet` the best passage of up to `words` words, with each match
between `FullText.MatchStart` and `FullText.MatchEnd` (two private-use characters) rather than HTML — the text
is whatever a row holds, and rendering it as markup would let anyone who can write a row inject script.
`Ui.Highlight` encodes the text and wraps each match in `<mark>`. Calling either outside a `Search`, or on a
property the index does not cover, throws naming the fix.

## Choosing a tokenizer

| Option | Effect |
| --- | --- |
| `tokenizer: FullTextTokenizer.Unicode` | The default: Unicode word boundaries, case- and diacritic-insensitive. Right for any language. |
| `tokenizer: FullTextTokenizer.English` | Adds English stemming — `run` finds `running` and `runs`. Makes non-English matches worse. |

## Where it runs

| | Wiring | The index |
|---|---|---|
| SQLite | `UseRaskSqlite(...)`, or a plain `UseSqlite` plus `UseRaskFullTextSearch()` | an FTS5 table kept current by triggers |
| In the browser | `UseSqlite(BrowserSqlite.ConnectionString("app"))` plus `UseRaskFullTextSearch()` | the same FTS5 table, in the app's local database |
| PostgreSQL | `UseRaskPostgres(...)` | a stored generated `tsvector` column with a GIN index |
| SQL Server | — | refused at boot |

Wherever it runs, the index arrives through a **migration** (adding,
changing or removing `HasFullTextSearch` is a migration of its own), the database keeps it current, so raw SQL,
`ExecuteUpdate`, a bulk insert and another process are all searchable the moment they commit — and a database
created with `EnsureCreated` gets no index at all. On a provider that cannot build one — SQL Server, or a plain
`UseSqlite` without `UseRaskFullTextSearch()` — `AddRaskData<TContext>` **refuses to boot** a context that
declares an index, rather than letting the first search fail.

## SQLite

`UseRaskSqlite(...)` registers the migration SQL, the query translation and the `highlight`/`snippet`
functions. All of it is inert until an entity declares an index, and it composes with `STRICT` tables and
[non-overlapping ranges](data.md#non-overlapping-ranges). A search is a ranked join over the FTS5 index (its
`bm25` ranking): the index is scanned first and each match reaches its row by primary key, so the cost follows
the number of matches, not the size of the table.

Four things worth knowing:

- **The index is kept current by the database.** `AFTER INSERT/UPDATE/DELETE` triggers update it. The
  triggers call no function, so they run under the default `trusted_schema=OFF`.
- **It arrives via migrations — including on an existing table.** The migration that creates the index fills
  it from the rows already there.
- **Any migration that touches the table rebuilds the index.** SQLite rebuilds a table for most `ALTER`s,
  which drops its triggers, and a renamed column would leave the triggers writing a column that is gone; Rask
  re-creates and refills the index rather than guess which case it is in. On a very large table, that is a
  full read of the table once per such migration.
- **The index keeps its own copy of the text** (`{Table}_fts`), costing disk roughly the size of the indexed
  columns. An FTS5 *external content* table would store nothing twice, but it can only forget a row when told
  its old values — and SQLite fires no `AFTER DELETE` for the row an `INSERT OR REPLACE` replaces, so one raw
  `REPLACE` would leave it wrong for good. The copy is forgotten by row id, so REPLACE, upserts and key changes
  all stay correct. A single integer key (including a strongly-typed id stored as one) is the index's row id;
  any other key — a `Guid`, a string, a composite — goes through a small key map (`{Table}_fts_keys`), because
  SQLite's implicit rowid can change on `VACUUM`.

**Anywhere you configure a plain `UseSqlite`,** add `UseRaskFullTextSearch()` instead. It registers only what
search needs — none of `UseRaskSqlite`'s connection string, pragmas or retry. Called after `UseRaskSqlite`, it
keeps that call's choices, `STRICT` tables included, and calling it twice is harmless.

## In the browser

A WebAssembly app searches its local database the same way. FTS5 is compiled into the SQLite build that is
linked into the app, and [`Rask.SQLite.Browser`](sqlite.md#rasksqlitebrowser--keeping-a-browser-database)
keeps that database across reloads. Reference `Rask.SQLite.EntityFrameworkCore` for `UseRaskFullTextSearch()`:

```csharp
builder.Services.AddRaskBrowserSqlite("app");
builder.Services.AddDbContextFactory<AppDbContext>(o => o
    .UseSqlite(BrowserSqlite.ConnectionString("app"))
    .UseRaskFullTextSearch());
```

Apply migrations at startup (`Database.MigrateAsync()`): the index is built by a migration, and `EnsureCreated`
creates none. EF Core in the browser needs an untrimmed build (`PublishTrimmed=false`) — see
[SQLite in the browser](sqlite.md#sqlite-in-the-browser-wasm).

## PostgreSQL

The same `HasFullTextSearch`, `Search(text)`, `FullText.Highlight` and `Snippet` work through
`UseRaskPostgres` from [`Rask.Postgres`](data.md#postgresql):

- **The index is a stored generated `tsvector` column with a GIN index** — no triggers; PostgreSQL keeps it
  current itself, and Npgsql's own migrations create it.
- **A search is `@@ to_tsquery(…)` ranked by `ts_rank_cd`**, and the highlights come from `ts_headline` in the
  same markers `Ui.Highlight` reads.
- **`English` maps to PostgreSQL's `english` configuration; `Unicode` to `rask_unicode`** — `simple` with
  `unaccent` in front of it, so `keres` finds `kérés` as it does on SQLite. The first migration that needs it
  creates it, running `CREATE EXTENSION IF NOT EXISTS unaccent`, so the migrating role needs that right once.

One difference worth knowing: a snippet on PostgreSQL has no `…` at its cut ends.

