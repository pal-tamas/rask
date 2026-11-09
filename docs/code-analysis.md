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

Tests and benchmarks keep the SDK's default set.

## Strict mode for your app: `<RaskStrict>`

None of the analyzer packages above reach an app built on Rask. What an app can switch on is `<RaskStrict>`, a
quality gate made only of what the .NET SDK and Rask already ship — no third-party analyzer package. A new app has it
already: `rask new` writes `<RaskStrict>errors</RaskStrict>` into its `Directory.Build.props`, and every template builds
clean under it. An app made before that writes it in its own `Directory.Build.props`, so its test projects get it too:

```xml
<!-- before: nothing — nullable is whatever each project says, warnings never fail the build -->

<!-- after: Directory.Build.props, beside the .slnx -->
<Project>
  <PropertyGroup>
    <RaskStrict>warnings</RaskStrict>   <!-- day one; `errors` once the list is worked down -->
  </PropertyGroup>
</Project>
```

| | `warnings` | `errors` (or `true`) |
|---|---|---|
| `Nullable` `enable` (overrides a project's own `disable`) | ✓ | ✓ |
| .NET analyzers at `AnalysisLevel` `latest-recommended` (unless the app set a level) | ✓ | ✓ |
| `EnforceCodeStyleInBuild`: the `.editorconfig`'s IDE rules report in `dotnet build` | ✓ | ✓ |
| A documentation file, so IDE0005 (an unused `using`) runs in the build; CS1591 (no XML comment) stays off | ✓ | ✓ |
| [RASK116](diagnostics.md#rask116), a test named as a sentence, in every `*.Tests` / `IsTestProject` project | ✓ | ✓ |
| Off: CA1716 (a Visual Basic keyword such as `Shared` in a name), and CA1707 (underscores) in a test project | ✓ | ✓ |
| `TreatWarningsAsErrors`: every warning above, and Rask's own RASK warnings, fail the build | | ✓ |

Unset or `false` changes nothing; any other word stops the build and names the two levels. Start at `warnings`: the
build stays green and the list shows up in the IDE and in `dotnet build`. Move to `errors` once it is empty. A project
that is not ready yet sets `<RaskStrict>false</RaskStrict>` in its own csproj, which wins over the
`Directory.Build.props`. Write the switch in `Directory.Build.props` rather than a csproj: the documentation file is
settled before a csproj is read, so in a csproj alone IDE0005 reports only in the editor.

What it looks like in an app that turned nullable off and never ran the analyzers:

```csharp
public string Customer(int id) { string name = null; return name; }
// before: builds clean
// warnings: warning CS8600: Converting null literal or possible null value to non-nullable type.
// errors:   error CS8600: …  — the build stops
```

```csharp
[Fact] public void AddItem() { … }                     // ⚠ RASK116: 'AddItem' does not read as a sentence: fewer than three words
[Fact] public void Add_puts_the_item_in_the_cart() { … } // reads as what it proves
```

The formatting and style rules come from the app's `.editorconfig`, never from MSBuild: `rask new` writes one into every
app (`.editorconfig` at the root), and an app made before it copies that file from a fresh `rask new` app.

## Where it is wired

- `Directory.Packages.props` — one version per analyzer package.
- `Directory.Build.targets` — the `PackageReference`s, for `src/` only, all `PrivateAssets="all"`: an app built on
  Rask gets Rask's own analyzers and none of these.
- `Directory.Build.props` — `AnalysisLevel` `latest-recommended` for `src/`, with code style and naming left to the
  severities in `.editorconfig`.
- `Directory.Build.targets` also cuts CI's analyzer build by project: `-p:RaskAnalyzerShard=K` analyses only the
  projects `scripts/lib/analyzer-shards.txt` deals to shard K (an unlisted project is in shard 1) — the
  `analyzers K/N` jobs; a build without it analyses every project. It turns no rule off. Each of those jobs also checks the
  formatting of the same projects' files (`scripts/lib/format-scope.sh`; shard 1 takes the files in no project's
  directory), so a change to the rules is formatted in four parts, not one. On such a change a shard runs
  `dotnet format whitespace` and `style`, not `analyzers`: its own analyzer build has just enforced those rules.
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
`.editorconfig`: `MA0004` (`ConfigureAwait`) is off in `Rask.Core`, `Rask.Ui` and `Rask.Site`, whose
component and runtime code runs on Rask's lifecycle context, where an `await` must resume; and
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
