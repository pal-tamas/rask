# Rask.Example.EfCore

A Rask **Server** sample showing data persistence with **EF Core + SQLite** — a small product
catalogue with full CRUD (list, create, edit, delete).

```bash
dotnet run --project samples/Rask.Example.EfCore
# open the printed URL at /products
```

The full write-up is in [docs/data-access.md](../../docs/data-access.md). In short, it demonstrates:

- **`IDbContextFactory<CatalogDbContext>`**, not a scoped `DbContext`. A Rask live session is
  long-lived over a WebSocket, so each slice opens a short-lived context per operation
  (`await using var db = await dbContextFactory.CreateDbContextAsync(ct)`).
- **Vertical slice architecture** — code is grouped by use case under `Features/Catalog/`
  (`ListProducts`, `CreateProduct`, `EditProduct`), each slice self-contained with its own form and
  EF Core access. No repository/service layers. App-wide shared bits live in `Shared/`; the Catalog
  domain shared across slices lives in `Features/Catalog/Shared/`.
- **DDD tactical patterns** — `Product` is an aggregate root (private setters, `Create` / `Update`
  only); `Money`, `ProductName`, and `StockLevel` are value objects. Each value object **owns its
  validation rule**, reused verbatim by the inline form validators (`Validate: Money.Validate`) and
  enforced again on construction.
- **EF Core entity configuration** — mapping lives in `IEntityTypeConfiguration<Product>`
  (`ApplyConfigurationsFromAssembly`), with value converters for the value objects.
- **The SQLite decimal gotcha** — SQLite has no decimal type, so `Money` is stored as integer minor
  units (cents) in an `INTEGER` column: exact, sortable, lossless.
- **Built-in inline validation** — Rask.Core's `Validate:` lambdas, no validation package.

## Database file

The SQLite file (`raskExampleCatalog.db`, gitignored) is created and seeded on first run via
`EnsureCreatedAsync()`. Set `RASK_DB_PATH` to point at a different file (the E2E test uses this to
isolate each run). Delete the file to reset to the seed data.

## Tests

- `tests/Rask.Example.EfCore.Tests` — unit tests for the domain + EF/SQLite integration tests
  (round-trip + proof that price is stored as `INTEGER`).
- `tests/Rask.Examples.E2E.Tests/EfCoreCrudTests.cs` — a Playwright CRUD smoke test.
