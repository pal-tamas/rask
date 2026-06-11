---
name: cut-release
description: Cut a Rask release. Use when publishing a new version to NuGet/GitHub. Promotes the CHANGELOG [Unreleased] section to a dated version, then tags vX.Y.Z so MinVer derives the version and release.yml runs the unit gate + sharded E2E + packs and pushes the NuGet packages and GitHub release.
---

# cut-release

Versioning is **MinVer from git tags** (`Directory.Build.props` → `<MinVerTagPrefix>v</…>`).
There is no version number to bump in a file — **the tag is the version**.

## 1. Pre-flight
- On `main`, clean tree, CI green.
- Run the **`rask-ship`** gate once more (format → warnings-as-errors → tests).
- Decide the SemVer bump from the `[Unreleased]` changes (breaking→major, feature→minor, fix→patch).

## 2. Promote the CHANGELOG
In `CHANGELOG.md`, rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` (keep its
`### Added/Changed/Fixed/...` subsections), and add a fresh empty `## [Unreleased]` above it.
Commit:
```bash
git add CHANGELOG.md && git commit -m "Release vX.Y.Z"
```

## 3. Tag + push (triggers release.yml)
```bash
git tag vX.Y.Z
git push origin main
git push origin vX.Y.Z
```
`release.yml` (on `push: tags: v*`) runs the unit gate, the sharded E2E matrix, then packs the
six NuGets (`Rask.Server`, `Rask.Wasm`, `Rask.Wasm.Hosting`, `Rask.Validation.DataAnnotations`,
`Rask.Validation.FluentValidation`, `Rask.Templates`), pushes them to nuget.org, and creates the
GitHub release. Watch it:
```bash
gh run watch
```

## 4. Verify
- GitHub release created with the six `.nupkg` assets.
- Packages visible on nuget.org at version `X.Y.Z`.
- To undo a mistaken tag **before** publish completes: `git push --delete origin vX.Y.Z`.
