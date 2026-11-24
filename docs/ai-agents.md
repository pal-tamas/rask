# Building Rask apps with AI assistants

Rask ships first-class guidance for AI coding tools, so an assistant can scaffold and extend a
Rask app correctly without you re-explaining the conventions.

## What's included

- **`llms.txt`** (repo root) — the index AI tools read first. One dense entry per guide carries the rules
  that make Rask code compile (the chain not `new`, the children indexer, required steps versus optional
  setters, the root rendering into `<body>`, routing and lifecycle, scoped CSS/TypeScript, callbacks, forms,
  auth) and links the guide behind each. A generated project ships no `AGENTS.md` of its own — point your
  assistant at `llms.txt`.
- **`AGENTS.md`** (repo root) — for an assistant working **on Rask itself**: the gate, the repo's
  workflows, how a change lands. It is not app guidance.
- **The published set on rask.sh**, for an assistant that reads the web rather than a checkout:
  [`https://rask.sh/llms.txt`](https://rask.sh/llms.txt) indexes every guide with a one-line summary,
  [`https://rask.sh/llms-full.txt`](https://rask.sh/llms-full.txt) is every app-building guide in one
  file, and each guide's Markdown sits at its page's address plus `.md` — for example
  [`https://rask.sh/docs/guides/cqrs.md`](https://rask.sh/docs/guides/cqrs.md). They are generated from the
  same `docs/` at every site publish, with the links rewritten to resolve on the site.
- **The `docs/` set** — a task guide for each subsystem (getting-started, elements & the DSL, routing,
  lifecycle, composition (every element event as MDN's type, keyboard values as the generated `Keys`/`Codes`
  constants), subscriptions, forms, js-interop, browser APIs, authentication, data access, HTTP & files,
  PWA, CQRS, diagnostics, testing, accessibility — typed `Aria*` steps and `AriaRole` constants generated from
  the WAI-ARIA spec, so an assistant writes `.AriaExpanded(open)` rather than guessing at `.Aria("expanded", "true")`
  strings, … — the full curated list is in the on-site guides index) plus the
  Tailwind, compiled at build time (`docs/tailwind.md`: utilities scanned from your own C# source, zero-JS
  interactivity, typed utility classes). Each guide embeds its examples as live demos, so the source
  a user reads on GitHub and the running showcase stay in lockstep.

## The one way to write an app

An assistant writes every Rask app in one style ([domain modelling](domain.md)), never "the old way" beside it:

- **Every write is an aggregate's own method**, run through its generated entry (`Supply.Change(id, version, …)`),
  one aggregate per transaction. No service layer, no application or domain service, no context injected into a page
  or a handler, no `SaveChanges` in app code.
- **Every property is a value object or a typed id**: `public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 200);`,
  `public readonly partial record struct VehicleId(int Value);` — the rule is said once, in the type, made through
  `Create`, never `new`.
- **A refusal is `Refuse`** under its field; a rule over the whole aggregate is its `Validate()`.
- **The form is the operation's**: `Form.For(Supply.Change, id)`, with no rule repeated on the form.
- **Another aggregate changes from a domain event** (`Raise` + a one-line handler calling that aggregate's entry);
  two in one `Db.Transaction` only as [the last resort](domain.md#the-transaction-boundary), with a comment saying why.
- **No SQL and no provider API** — `InTextOrder()`, `Db.Lock`, `.Covering(…)`, a value object's `Scale`
  ([code written for one database](data.md#code-written-for-one-database)).
- **Tests run on SQLite in memory**: `TestDatabase.Start<AppDbContext>()`, `Test.Refuses(…).At("Field")`,
  `Test.Concurrently`, `Test.Make<T>()` ([testing the domain](testing.md#testing-the-domain)).

## The current tenant and user

An assistant that reaches for "the current tenant" or "the current user" should read the app's own rows,
`Current.Tenant` and `Current.User`, not query them by id. They exist once the tenant aggregate says `ITenant` and
the user aggregate says `IUser` (`Authenticatable` already does), are typed as the aggregate's read face
(`TenantRead`, `UserRead`), are read-only, and cost no query — the framework reads them ahead of each request, live
event and job. `Current.Tenant` is no longer a `Guid`: compare `Current.Tenant?.Id`, and to change the tenant load
the aggregate (`await Tenant.Find(Current.RequiredTenant.Id)`) and save it. See
[your tenant row](multi-tenancy.md#your-tenant-row-currenttenant). For the ids alone, read `Current.UserId` and
`Current.TenantId` — typed as the aggregate's key, so an app whose users are `int`s gets an `int?` — never
`int.Parse` a claim by hand ([numbered users](data.md#an-app-whose-users-are-numbered)).

An administrator (a user of no tenant) who switches partner calls `await auth.ActInTenant(id, returnUrl)` —
never a hand-written "chosen partner" cookie or `GET /partner/{id}` endpoint. The choice lives on the sign-in's
session, so `Current.Tenant` answers it on every later request, reconnect and live event, and sign-out drops it
([acting in a tenant](multi-tenancy.md#acting-in-a-tenant)). An app with its own sign-in maps
`app.MapRaskTenantChoice(find, onChosen)` and writes the chosen tenant into its cookie or token in `onChosen`; the
same `Auth.ActInTenant` call then reaches it ([with your own sign-in](multi-tenancy.md#with-your-own-sign-in)).

## Errors an app means the user to read

An assistant should never print `ex.Message` to a visitor, nor write a `catch` on every page to show one. When the
app refuses something on purpose — a closed invoice, a full trailer — it throws
`new RefusedException("…")`, and Rask says that sentence where the person is reading: the handler's toast, the
form's messages, the error page, or the `detail` of a 400 problem on an endpoint. Every other exception stays
hidden, and a request that is not a page answers an RFC 9457 problem by default, with a reference in place of the
message; the app gives its own exceptions a status with `server.Answer<T>(status)`. See
[a refusal is shown](lifecycle.md#a-refusal-is-shown-refusedexception). A refusal about one field of a form is
`Refuse.When(condition, () => Parts[i].Quantity, "…")`, shown under that field
([refusing an operation](domain.md#refusing-an-operation-refuse)); a test asserts it with
`Test.Refuses(…).At("Parts[2].Quantity")`.

## Rules live on the aggregate

An assistant should not re-check a rule in a page or a handler before saving. A value object's rule, the aggregate's
`Validate()` and its `ValidateDelete()` (and a property's DataAnnotations attribute, where an app has one) are asked
by **every** save — an operation's entry, `Save()`, `Create(model)`, `Delete` — and a refusal reaches the form as a
`RaskValidationException` with each message under its field. A stored row is asked only about the properties the
save changes, so old data that a rule tightened since still saves for its other fields. See
[what every save asks](domain.md#what-every-save-asks).

## How to use it

1. Scaffold: `rask new MyApp`.
2. Point your assistant at Rask's `llms.txt` — the repo-root file, or `https://rask.sh/llms.txt` if it reads the web.
3. Ask for features in plain language — the assistant follows the conventions and links to
   `docs/diagnostics.md` when it hits a `RASKxxx` compile diagnostic.
4. Ship it: `rask deploy --host user@box --domain app.example.com` builds and runs it on a single
   host over SSH with automatic HTTPS.

## Keeping it accurate

The repo-root `llms.txt` and this guide are part of the public API surface: when a user-facing
behavior changes, they're updated in the same commit (see `docs/development-workflow.md`). GitHub is
the single source of truth — these files are committed, not local-only.
