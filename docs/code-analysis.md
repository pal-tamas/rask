# Code analysis & analyzers

Rask builds with **warnings-as-errors** and analyzers on (`Directory.Build.props`):
`TreatWarningsAsErrors`, `EnableNETAnalyzers`, `EnforceCodeStyleInBuild`. The SDK's .NET
analyzers (CAxxxx) and code-style (IDExxxx, severities from `.editorconfig`) run on every build.
`AnalysisLevel` is left at the SDK default because the repo builds clean there; raising it is a
deliberate, per-PR cleanup (see below).

## Recommended additional analyzers

Adopt these for a published, perf-sensitive framework with source generators. Add via central
package management (`Directory.Packages.props`) + a `PackageReference … PrivateAssets="all"` in
`Directory.Build.props`.

| Analyzer | Why |
|---|---|
| **Roslynator.Analyzers** | Broad, high-signal C# rules + refactorings. Best single add. |
| **Meziantou.Analyzer** | Correctness/perf rules the SDK misses (culture, async, allocations). |
| **SonarAnalyzer.CSharp** | Bug & code-smell detection, cognitive-complexity. |
| **Microsoft.CodeAnalysis.PublicApiAnalyzers** | Track the public API surface of the NuGets (`PublicAPI.Shipped/Unshipped.txt`) so breaking changes are caught — pairs with SemVer/MinVer. |
| **Microsoft.CodeAnalysis.BannedApiAnalyzers** | Enforce "use standard libs / safe defaults": ban `DateTime.Now`, culture-less `ToString`, etc. (directly addresses the CA1305 class of findings). |
| **Microsoft.CodeAnalysis.Analyzers** (RS rules) | Author-correctness for the `Rask.Generators` projects specifically. |

## Adoption procedure (important)

Because the build is **warnings-as-errors**, adding an analyzer turns its findings into build
**errors** immediately. So adopt **one analyzer per PR**:
1. Add the package; build; triage the findings.
2. Fix the real ones; suppress intentional ones in `.editorconfig` with a `# justification`
   comment (e.g. CA1051 on `RenderFrame`'s deliberately-public hot-path fields).
3. Run the `rask-ship` gate (incl. benchmarks if a fix touches the render hot path).

A ready starter set (already-clean default mode) can be promoted later via
`<AnalysisLevel>latest-recommended</AnalysisLevel>` — that surfaces ~33 CA findings today, so it
gets its own PR.
