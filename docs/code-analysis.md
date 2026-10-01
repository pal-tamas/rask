# Code analysis & analyzers

Rask builds with **warnings-as-errors** and a full analyzer set on everything under `src/` — the code that ships.
Every finding is a build error, so a green build means the shipped code is analyzer-clean.

| Analyzer | What it guards | Rule prefix |
|---|---|---|
| .NET analyzers at `latest-recommended` | correctness, globalization, performance, reliability | `CA` |
| **Meziantou.Analyzer** | culture, string comparison, async, regex safety, API shape | `MA` |
| **Roslynator.Analyzers** | C# correctness and simplification | `RCS` |
| **SonarAnalyzer.CSharp** | bugs, security hotspots, code smells | `S` |
| **Microsoft.CodeAnalysis.BannedApiAnalyzers** | APIs this repo does not call (`BannedSymbols.txt`) | `RS0030` |
| **Microsoft.CodeAnalysis.PublicApiAnalyzers** | the tracked public surface (`PublicAPI/<tfm>/`) | `RS0016`/`RS0017` |
| Code style from `.editorconfig` | formatting and naming | `IDE` |

The generator projects additionally run the Roslyn author rules (`EnforceExtendedAnalyzerRules`).

Tests and benchmarks keep the SDK's default set, which keeps the one-minute commit and push hooks inside budget.

## Where it is wired

- `Directory.Packages.props` — one version per analyzer package.
- `Directory.Build.targets` — the `PackageReference`s, for `src/` only, all `PrivateAssets="all"`: an app built on
  Rask gets Rask's own analyzers and none of these.
- `Directory.Build.props` — `AnalysisLevel` `latest-recommended` for `src/`, with code style and naming left to the
  severities in `.editorconfig`.
- `BannedSymbols.txt` — each banned API with the reason and what to call instead:
  the clock is read through `TimeProvider`, never `DateTime.Now`/`DateTimeOffset.Now`/`DateTime.Today`; no
  `Thread.Sleep`, no `Environment.Exit`, no `GC.Collect`.

## The policy: fix it

A finding is fixed, not silenced. A rule is turned off only where the code genuinely cannot satisfy it — a false
positive, or a shape the framework's design requires — and then at that **one site**, with the reason on the line:

```csharp
#pragma warning disable S2077 // an identifier cannot be a parameter; it is quoted, with its quotes doubled
command.CommandText = $"SELECT \"Key\" FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
#pragma warning restore S2077
```

Never for a whole project or path, so the rule keeps guarding every new line. Two exceptions, both in
`.editorconfig`: `MA0004` (`ConfigureAwait`) is off in `Rask.Core`, `Rask.Ui`, `Rask.Blazor` and `Rask.Site`, whose
component and runtime code runs on Rask's lifecycle context or Blazor's renderer, where an `await` must resume; and
`CA1716` (a name that is a Visual Basic keyword) is off everywhere, because Rask is a C# framework and a name like
`Select`, `Option` or `Get` is the HTML tag or the repo's verb.

## Design

The analyzers catch lines; design is held by review (`rask-review`), to one standard across the codebase —
**SOLID and Clean Code**:

- **Single responsibility** — one reason to change per type, and per file. A generator that reads symbols,
  reports diagnostics and emits source is three types; a command that plans, executes and prints is three.
- **Open/closed** — extend through the seams that exist (chain groups, the runtimes table, battery wiring)
  rather than growing a `switch` in a central method.
- **Liskov / interface segregation** — a derived component honours its base's contract; a new interface
  is as small as its callers need.
- **Dependency inversion** — services arrive through the constructor, never `new`ed or located.
- **Clean Code** — small methods named for what they do, at most seven parameters (a record past that),
  no copied helper (reuse it, or move it somewhere shared), no dead code.

Splitting a large type goes in two steps: partial files by responsibility (`Type.Responsibility.cs`, a pure
move), then an internal type where the seam is a unit worth testing alone. The public surface stays as it
was unless the change is an API decision; render-hot-path moves bring `run-benchmarks` evidence.

## Adding an analyzer or raising a level

Because every finding is an error, adding a rule set turns its findings into build errors at once:

1. Add the package (central version + the `src/` reference), build with
   `-p:TreatWarningsAsErrors=false`, and tally the findings by rule.
2. Fix them. Where a fix changes a public name or shape, that is an API decision — record it in
   `PublicAPI/<tfm>/` and the CHANGELOG, and follow [Public API style](api-style.md).
3. Run the `rask-ship` gate, including benchmarks when a fix touches the render hot path.
