---
name: rename-public-member
description: Rename a public type member (method, property) across the whole repository in one pass. Use for any rename of a Rask-owned public name — dropping an Async suffix, aligning a verb with docs/api-style.md. Runs scripts/tools/RaskRename (a Roslyn symbol rename over Rask.slnx), then the public-API baselines, the templates test, docs and CHANGELOG.
---

# rename-public-member

**Ask first.** A rename is a decision the owner makes, even for an internal helper. This playbook is how
to carry one out once it is agreed, not a licence to start one.

## 1. Rename
```bash
dotnet run --project scripts/tools/RaskRename -c Release -- Rask.Wasm.WasmHostBuilder.RunAsync Run --dry-run
dotnet run --project scripts/tools/RaskRename -c Release -- Rask.Wasm.WasmHostBuilder.RunAsync Run
```
The first argument is namespace, type, member. Loading the solution takes about half a minute. An
interface member takes its implementations with it, and overloads go together.

- It **stops** when the type already has a member with the new name (a generated member, a sync twin).
  Do not pick a different word yourself: keep the old name and say so.
- It rewrites `Type.Old` in the templates, docs, `llms.txt` and the package `NUGET.md` files — never in
  `CHANGELOG.md`, which keeps history as it was.
- It lists every other `.Old` / `Old(` in those files as `look at path:line`. Those may be someone else's
  method of the same name (`HttpClient.SendAsync`), so read each and edit by hand.
- A name matched by **string** is invisible to it: an analyzer that switches on a method name
  (`RootShellAnalyzer` did, on `"RunAsync"`), a test that stubs it in a source string. Grep `src/` and
  `tests/` for the old name in quotes.

## 2. Build once
```bash
dotnet build Rask.slnx -c Release -warnaserror
```
The only errors should be RS0016/RS0017: the public-API baselines want the new name. Fix them from the
message or with the IDE quick-fix, for **both** target frameworks, then build again. Never
`git checkout --` a `PublicAPI.Unshipped.txt` afterwards.

## 3. What no build covers
- Templates: `TemplatesCompileTests` (unit gate) compiles their C#. A template project file or MSBuild
  target that names the member still needs `scripts/run-cli-build-e2e.sh`.
- `docs/api-style.md` if the rename changes a rule or the vocabulary table.

## 4. Ship
CHANGELOG `[Unreleased]` with the before → after call site, under a breaking heading when the member
shipped. Then `rask-ship` and `land-on-main`.
