---
name: add-diagnostic
description: Add a new RASK0xx compile-time diagnostic to the Rask Roslyn generators/analyzers. Use when introducing a new warning or error in src/Rask.Generators. Defines the DiagnosticDescriptor, wires DiagnosticHelp.Link, documents it in docs/diagnostics.md (TOC row + section), and adds a generator test.
---

# add-diagnostic

## 1. Pick the next ID
**Do not trust the number written here** — this line has gone stale twice, and both times the id was
already taken on a branch base that was ahead of the checkout it was picked against. Run the grep, and
run it again before you merge.

At the last edit the highest allocated was RASK070, so the next free is **RASK071**; RASK063/065 are
RESERVED for Rask.Blazor and unimplemented, and RASK030/032/034/042/047/048/049/050 are retired and
never recycled. **FOUR** assemblies allocate in this space, and RS1019 only checks one compilation:

```bash
grep -rhoE '"RASK[0-9]{3}"' src/Rask.Generators src/Rask.Batteries.Generators \
  src/Rask.Api.Generators src/Rask.Generators.Shared | sort -u | tail
```
Grep **both** analyzer assemblies plus the shared sources. RS1019 only sees duplicate descriptor ids
*within* one assembly, so an id reused across them compiles clean and ships twice.

## 2. Define the descriptor
In the owning generator/analyzer under `src/Rask.Generators/` (factory rules in
`ComponentFactoryGenerator.cs`, route rules in `RoutesGenerator.cs`, scoped-asset rules in
`ComponentScoped{Css,Js}Generator.cs`, plus `Analyzers/`). Mirror the existing pattern:
```csharp
private static readonly DiagnosticDescriptor Rask023 = new(
    "RASK023",
    "Short title",
    "Message with {0} format args",
    "Rask.Generators",
    DiagnosticSeverity.Warning,   // Error | Warning | Hidden
    isEnabledByDefault: true,
    helpLinkUri: DiagnosticHelp.Link("RASK023"));   // see DiagnosticHelp.cs
```
Report it with `context.ReportDiagnostic(Diagnostic.Create(Rask023, location, args))`.

## 3. Document it — `docs/diagnostics.md`
- Add a TOC table row (ID · Severity · Summary) in the table near the top.
- Add a `## RASK023` section (the anchor must match `DiagnosticHelp.Link` →
  `#rask023`): what triggers it, severity, fix, and a code example. Match the existing entries.

## 4. Test it — `tests/Rask.Generators.Tests`
Add a generator test asserting the diagnostic is/ isn't produced for the trigger and the
non-trigger case.

## 5. Finish
Run the **`rask-ship`** gate. `docs/diagnostics.md` is the user-facing index; descriptors are
the source of truth — keep them in sync.
