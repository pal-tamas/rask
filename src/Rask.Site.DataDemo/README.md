# Rask.Site.DataDemo — the full-stack demo on rask.sh

A small browser-WASM app that shows the whole Rask data stack running **inside the browser tab**:

- a `Note : Aggregate<Guid>` (Rask.Data) in a real SQLite database, through EF Core;
- `Note.CreateAsync(model)` from a kit form, and a list that refreshes itself because the save tells
  `Rask.Query` it wrote a `Note`;
- `Note.Read.Search(text)` with `FullText.Highlight` / `FullText.Snippet`, rendered by `Ui.Highlight`;
- `Rask.SQLite.Browser` keeping the database across reloads (IndexedDB snapshots every two seconds).

It is served at **`https://rask.sh/demos/data/`** and embedded as a lazy iframe on the data, full-text-search and
query guides through the site's `data-notes` demo key (`src/Rask.Site/Features/Data/DataNotesDemo.cs`).

## Why a separate app

EF Core, Rask.Data and a native SQLite add about 2.4 MB brotli and ~55 trim warnings to a bundle. In the site that
would be paid by every visitor; here it is paid only by a reader who scrolls a guide to the demo. The site's own
publish stays at zero IL warnings.

## Build, run, publish

```bash
dotnet run --project src/Rask.Site.DataDemo                  # Debug, served at / (the path base is publish-only)
dotnet publish src/Rask.Site.DataDemo -c Release             # what pages.yml does
scripts/run-data-demo-e2e-local.sh                            # publish + Playwright journeys under /demos/data/
```

- **Native linking is required.** Publish without `-p:WasmBuildNative=false` (so the `wasm-tools` workload is
  needed): without it `e_sqlite3` is not linked in and every database call fails at runtime.
- **`<RaskPathBase>/demos/data</RaskPathBase>`** rewrites the published `<base href>`; `.github/workflows/pages.yml`
  copies the publish's `wwwroot` into the site artifact at `demos/data/`.
- **Trimmed, with EF Core's three assemblies rooted** (`TrimmerRootAssembly` in the csproj) and only EF Core's own
  `IL2026`/`IL2104` suppressed. Rask.Data and Rask.SQLite.EntityFrameworkCore are trim-safe
  ([#1132](https://github.com/pal-tamas/rask/issues/1132)), so they need nothing — the recipe in
  [docs/sqlite.md](../../docs/sqlite.md#sqlite-in-the-browser-wasm).

## Wiring a Rask server host does for you

Rask.Data has no browser wiring yet (#1132), so `Program.cs` and `App.cs` spell out what a Rask server host would:
`AddRaskData<NotesDb>()` plus the two context factories, `Db.Configure(services)` once the container exists (in
`NotesDatabase`), an `IDataChanges` that invalidates the Rask.Query queries about what a save wrote
(`QueryDataChanges`), and `Db.UseScope(services)` around the write so the save can find it.
