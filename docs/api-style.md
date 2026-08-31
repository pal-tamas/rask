# Public API style

Rask's promise is that you can read a Rask program aloud. That is a property of the **names**, not of
the engine, and names drift unless something holds them. This page is what holds them, and
[the public-API gate](#the-gate) is what makes it stick.

It governs every public member of a shipped package, plus `Rask.Core` and `Rask.Html` — those two are
`IsPackable=false` only because they are bundled into the host packages, and their surface is the one
every component author writes against. It does **not** govern the source generators, their code fixes,
or the MSBuild tasks: the compiler and MSBuild construct those by name, and nobody writes code against
them.

## The rules

### 1. A short noun, with the mechanism left out

The name is the shortest thing that says what it is. Mechanism suffixes go — `Queue`, `Store`,
`ConnectionFactory` — and so does the `Rask` prefix, which the namespace already carries.

```csharp
IMail    IJobs    ICache    ILogs    ISqlite    IWebPush
```

not `IMailQueue`, `IJobQueue`, `ILogStore`, `IRaskSqliteConnectionFactory`.

That the mail is queued is true, and it is a fact the caller never acts on — a queue that became a
direct SMTP write would leave every call site correct and every name a lie. The guarantee belongs in
the doc comment, where it can be stated properly, not smuggled into a noun.

**A noun that names a role rather than a mechanism stays, however long.** `IDispatcher` is not
`IDispatchQueue`; it is the thing that dispatches, which is exactly what you want to know.
`IQueryClient` is the client side of the query pipeline — a role, not a plumbing detail. The rule is
against mechanism, not against length, and `Queue`/`Store`/`ConnectionFactory` fail it because they
describe how the thing is built rather than what it is for.

### 2. The verb .NET already uses, not a nicer one

When the BCL has a word for the operation, that is the word — a reader who knows C# should not have to
learn Rask's synonym for something they already do.

```csharp
await cache.GetOrAddAsync("products", LoadProducts, ct);   // as ConcurrentDictionary / IMemoryCache
await cache.RemoveAsync("products", ct);
```

not `RememberAsync` / `ForgetAsync`. Those read beautifully in isolation and cost every reader a
translation step, forever, for one moment of charm.

**Unless it stutters against its own parameter.** `ILogs.QueryAsync(LogQuery query)` says "query" twice
and its type a third time; `SearchAsync(LogQuery)` is what the operator at `/_rask` is actually doing.
Plain English wins where the borrowed word has stopped carrying information.

### 3. One concept, one verb, everywhere

A verb means the same thing in every package. Ask for data with `QueryAsync`, tell the system to do
something with `SendAsync`, announce that something happened with `PublishAsync` — in `Rask.Cqrs`, and
in anything that comes later.

This is the rule the codebase broke worst. One `DispatchAsync` did three jobs, distinguished only by
parameter type, and the same operations were called `MutateAsync`, `FetchAsync` and `Query` one package
over — four words for two ideas, so moving between two first-party packages meant relearning both.

**Different semantics earn a different verb.** `Rask.Query`'s `Query(...)` deliberately does *not*
share the mediator's `QueryAsync`: it returns an observable `Query<T>` that re-renders its component,
not a `Task<T>` you await once. Two names because they are two things — which is the rule, not an
exception to it. The test is whether a caller could swap one for the other and be right.

### 4. Every awaitable ends in `Async` and takes a cancellation token

```csharp
Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
```

The suffix is the .NET convention every C# developer and analyzer already reads, and the token is
always the **last** parameter, always defaulted. Defaulted so the common call site stays short; present
so a background loop, a timeout or a shutdown drain has somewhere to put its lifetime. An awaitable
method with no token is a method that cannot be cancelled — say so deliberately or don't ship it.

### 5. At most one `Action<TOptions>?`, and it goes last

Two option delegates in a row cannot be told apart at a call site:

```csharp
// don't
AddRaskSqlite(services, connectionString,
    Action<SqlitePragmaOptions>? configure = null,
    Action<SqliteBusyRetryOptions>? configureRetry = null)
```

Nothing about `AddRaskSqlite(cs, null, o => …)` says which one you configured, and swapping them
compiles. One options type, one delegate — nest the second set of knobs as a property.

### 6. No bare `bool` parameter

```csharp
UseRaskSqlite(cs, null, null, true)   // true what?
```

A `bool` in a parameter list is a value with no name at the call site. It becomes a property on the
options type, where it keeps the name it was given.

### 7. Configuration is optional, or the method doesn't exist

Every `Add*` takes `Action<TOptions>? configure = null`. A required configure delegate means the
method cannot express its own default, which means there isn't one — and a package with no working
default is a package that fails on first use.

**`AddRaskWebPush` is the standing exception**, and the reasoning is worth stating because it is the
line the rule stops at. Sending needs a VAPID key pair, which cannot have a default: a generated one
would silently break every existing subscription on restart, and an absent one turns every send into a
runtime failure. Making the delegate required means you cannot forget what the signature will not let
you omit — a compile error rather than a startup one. The rule holds everywhere a default is
*possible*; where none is, prefer the compiler over a good error message.

### 8. Machinery is hidden

Anything the user does not write but can see — generated intermediates, chain stages, collection
builders — carries `[EditorBrowsable(EditorBrowsableState.Never)]`. If a doc page has to tell readers
to ignore a name in their completion list, hide the name instead of writing the paragraph.

Hiding is not removing: an `[EditorBrowsable(Never)]` member is still callable, so it stays in the
baseline. The attribute fixes what a developer *sees*; the baseline records what they *can reach*.

### 9. An exception names a call that compiles

The message a developer reads at 2am is API surface. It must name the real method, in the real shape:

```csharp
// don't — Context<T>(value)[…] is not a thing you can type
throw new InvalidOperationException($"Wrap an ancestor in Context<{typeof(T).Name}>(value)[ … ].");

// do
throw new InvalidOperationException($"No {typeof(T).Name} in scope. Provide one with Context.Provide<{typeof(T).Name}>(value)[ … ] on an ancestor.");
```

And one problem gets **one** spelling. Two messages for one cause means searching for the text you saw
finds half the story.

### 10. Registration verbs follow ASP.NET, not us

`Add*` on `IServiceCollection`, `Use*` on `IApplicationBuilder` or an options builder, `Map*` on
`IEndpointRouteBuilder`. These are borrowed words with settled meanings; a framework that redefines
them makes every reader translate. Most apps never type them at all — [`RaskApp`](one-person-framework.md)
wires the batteries — but the escape hatch obeys the same law as everything else.

## The vocabulary

What the rules above settled, so a new package has one place to look rather than a precedent to guess at.

| Idea | Noun | Verbs |
|---|---|---|
| Transactional email | `IMail` | `SendAsync`, `ScheduleAsync` |
| Background work | `IJobs` | `EnqueueAsync`, `ScheduleAsync` |
| Cache | `ICache` | `GetAsync`, `SetAsync`, `GetOrAddAsync`, `RemoveAsync` |
| Mediator | `IDispatcher` | `QueryAsync`, `SendAsync`, `PublishAsync` |
| Cached reads | `IQueryClient` | `Query`, `MutateAsync`, `Invalidate` |
| Durable log | `ILogs` | `SearchAsync` |
| SQLite connections | `ISqlite` | `InImmediateTransactionAsync` |
| Web Push | `IWebPush` | `SubscribeAsync` (browser), `SendAsync` (server) |

`IWebPush` is deliberately one name on both sides of the wire, in two namespaces
(`Rask.Core.Browser` subscribes, `Rask.WebPush` sends) over one shared `PushSubscription`. A file that
needs both aliases one; no file does today.

## The gate

`Microsoft.CodeAnalysis.PublicApiAnalyzers` tracks every public member in a checked-in
`PublicAPI/<tfm>/PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` pair, wired up in
`Directory.Build.targets` for every project the rules govern.

- **RS0016** — a public member missing from the files.
- **RS0017** — a file entry with no member behind it.

Both are compiler warnings, and the repo builds warnings-as-errors, so **a public-surface change that
isn't recorded fails the build**. That is the whole point: adding, renaming or removing anything public
shows up as a diff in a text file, in the same PR, where a reviewer reads it as English rather than
reconstructing it from a thousand lines of implementation.

Generated public API is recorded too. Rask hand-writes almost none of the surface a user types —
`Div.Class("panel")`, `HomePage.Url()` and every chain step are emitted by the generators — so a gate
that skipped generated code would skip the part people actually call. It also cannot be skipped
cleanly: path-scoped `.editorconfig` severity does not reach generator-produced trees, so an exclusion
would have to be a project-wide `NoWarn`, which is a hole rather than a rule.

The files are per target framework because `Rask.Core`, `Rask.Html`, `Rask.Wasm`, `Rask.SQLite.Browser`
and `Rask` build for `net10.0` and `net10.0-browser` with genuinely different surfaces. Every project
uses the same layout, including the ones with a single framework today — so a project that gains a
second one gets an empty baseline to fill rather than a gate that quietly starts contradicting itself.

`RS0026` and `RS0027` are off (`.editorconfig`). They protect callers of a *frozen* API from a
source-breaking recompile, and Rask is pre-1.0 and breaks deliberately — and since rule 4 puts a
defaulted `CancellationToken` on every awaitable, any interface with overloads trips RS0026 by
construction. They get another look at 1.0.

A missing baseline is an error, not a pass. `Directory.Build.targets` includes the two files only if
they exist, which is what lets a new project be added before its surface is written down — and is also
exactly how this gate would come to pass by not running. `RaskVerifyPublicApiBaseline` fails the build
of any tracked project that has no baseline for the framework being compiled, and
`scripts/tests/public-api-gate.test.sh` proves all of it by breaking the gate on purpose: an
unrecorded member, an entry with nothing behind it, and a deleted baseline each have to fail, against
a control that requires the clean tree to be green.

### Working with it

Pre-1.0, everything lives in `PublicAPI.Unshipped.txt` and `PublicAPI.Shipped.txt` is empty. Nothing
here is frozen yet, and saying otherwise in a filename would be a claim the project has not earned;
the [release process](development-workflow.md#versioning--releases) promotes unshipped to shipped
at 1.0, once.

Adding a member fails the build until you record it. The signature the analyzer wants is in the RS0016
message verbatim — copy it into `PublicAPI.Unshipped.txt`, or take the IDE's "Add to public API"
quick-fix. Removing or renaming one fails as RS0017 until you delete the old line.

Neither is busywork you route around. A member that is hard to write down is usually hard to use, and
the line you are about to add is the sentence a stranger will read.

## When a rule and a caller disagree

The caller wins. These rules exist to make call sites read well, so a rule that makes one read worse
has hit a case its author didn't have. Say so in the PR and change the rule here in the same commit —
a law with undocumented exceptions is worse than no law, because the next person can't tell an
exception from a mistake.

## Related

- [Code analysis & analyzers](code-analysis.md) — the rest of the build's analyzer configuration.
- [Development workflow](development-workflow.md) — where this sits in the definition of done.
- [Diagnostics](diagnostics.md) — the RASK0xx errors that enforce the framework's own rules.
