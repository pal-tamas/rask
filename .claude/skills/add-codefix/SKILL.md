---
name: add-codefix
description: Add a Roslyn CodeFixProvider (IDE lightbulb quick-fix) for an existing RASK0xx diagnostic. Use when a diagnostic has a single obvious mechanical fix. Implements the provider in src/Rask.Generators.CodeFixes, adds an apply-and-compare test in tests/Rask.Generators.Tests, notes the quick-fix in docs/diagnostics.md.
---

# add-codefix

Quick-fixes live in **`src/Rask.Generators.CodeFixes`** — a SEPARATE assembly from `Rask.Generators`
because code fixes reference `Microsoft.CodeAnalysis.Workspaces`, which an analyzer assembly must not
(RS1038 / csc load failure). It contains no analyzers/generators, only `CodeFixProvider`s, and is packed
into `analyzers/dotnet/cs/` by `Rask.Server` and `Rask.Wasm` (build-order-only ProjectReference, so
`-warnaserror` is unaffected).

**Only add a fix when there is ONE unambiguous mechanical edit.** Skip diagnostics whose fix depends on
intent (e.g. a `Key:` value) or context (e.g. removing a statement vs. an expression-lambda body).

## 1. Implement the provider
`src/Rask.Generators.CodeFixes/{Name}CodeFixProvider.cs`. Derive from the shared
`RaskCodeFixProvider<TNode>` base (it owns the find-node/register/apply skeleton) — mirror
`ImgMissingAltCodeFixProvider` / `RequiredFactoryParamCodeFixProvider`:
```csharp
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FooCodeFixProvider))]
[Shared]
public sealed class FooCodeFixProvider : RaskCodeFixProvider<TNodeSyntax>
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("RASK0xx");
    protected override string Title => "…";
    protected override string EquivalenceKey => "RASK0xx_…";

    // Optional: withhold the fix when applying it would trade this diagnostic for a worse one
    // (e.g. RequiredFactoryParamCodeFixProvider suppresses when `required` would trip RASK002).
    protected override Task<bool> CanFixAsync(CodeFixContext context, TNodeSyntax node) => …;

    protected override Task<Document> FixAsync(Document document, TNodeSyntax node, CancellationToken ct) =>
        ReplaceNodeAsync(document, node, /* rewritten node */, ct);
}
```
The base finds the nearest enclosing `TNode` at the diagnostic span (analyzer diagnostics point at the
flagged node; generator diagnostics like RASK001 point at the declaration via `MakeLocation`). Keep edits
trivia-correct (e.g. a new modifier needs `WithTrailingTrivia(Space)`). Use `CanFixAsync` with the
`SemanticModel` when the fix is only valid under some symbol condition.

## 2. Test it — `tests/Rask.Generators.Tests/CodeFixProviderTests.cs`
Use `CodeFixHarness`: `ApplyAnalyzerFixAsync(analyzer, provider, "RASK0xx", source)` for analyzer
diagnostics, `ApplyGeneratorFixAsync(generator, provider, "RASK0xx", source)` for generator ones. Assert
the rewritten text `Contains` the fixed form, covering each argument/declaration shape.

## 3. Document it — `docs/diagnostics.md`
Add the ID to the "IDE quick-fix" sentence in the intro, and append "(**quick-fix available** …)" to that
diagnostic's **Fix:** line.

## 4. Finish
Run the **`rask-ship`** gate. Verify packaging survives: `dotnet pack src/Rask.Server` and confirm the
codefix DLL is in `analyzers/dotnet/cs/`. The IDE lightbulb itself can only be smoke-tested manually —
the unit tests prove the transform.
