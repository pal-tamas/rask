---
name: cut-release
description: Cut a Rask release. Use when publishing a new version to NuGet/GitHub. Promotes the CHANGELOG [Unreleased] section to a dated version, then tags vX.Y.Z so MinVer derives the version and release.yml builds, packs and pushes the NuGet packages and GitHub release. release.yml runs no tests, so the local gates must be run before tagging.
---

# cut-release

Versioning is **MinVer from git tags** (`Directory.Build.props` → `<MinVerTagPrefix>v</…>`).
There is no version number to bump in a file — **the tag is the version**.

**Order matters: land the CHANGELOG on `main` via a PR FIRST, then tag the merged commit.**
`main` is a **protected branch** (no direct pushes — `git push origin main` is rejected by a
hook), and a **commitlint** check enforces Conventional Commits on every commit. So the release
changelog must go through a PR with a Conventional title, exactly like any other change. Tagging
afterward keeps the tag on a real `main` commit (don't tag a local commit you couldn't push).

## 1. Pre-flight
- On `main`, clean tree, CI green.
- Run the **`rask-ship`** gate once more (format → warnings-as-errors → tests).
- Decide the SemVer bump from the `[Unreleased]` changes (breaking→major, feature→minor, fix→patch).
  Pre-1.0, a new feature is still a minor bump (e.g. 0.8.0 → 0.9.0).
- **Leave `PublicAPI.Shipped.txt` empty.** The public-API baselines
  (`src/*/PublicAPI/<tfm>/`, see `docs/api-style.md`) stay entirely in `PublicAPI.Unshipped.txt`
  until 1.0. Promoting them is a claim that the surface is frozen, which pre-1.0 Rask does not make —
  and it would turn every deliberate rename into a shipped-API removal to argue with. At 1.0, promote
  unshipped → shipped in the release PR, once.

## 2. Promote the CHANGELOG on a release branch
In `CHANGELOG.md`, rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` (keep its
`### Added/Changed/Fixed/...` subsections), and add a fresh empty `## [Unreleased]` above it.
Commit on a branch with a **Conventional Commit** message (plain `Release vX.Y.Z` FAILS commitlint):
```bash
git checkout -b release/vX.Y.Z
git add CHANGELOG.md && git commit -m "chore(release): vX.Y.Z"
git push -u origin release/vX.Y.Z
```

## 3. PR → merge to main
```bash
gh pr create --base main --head release/vX.Y.Z --title "chore(release): vX.Y.Z" --body "…"
# wait for the 14 checks to go green, then:
gh pr merge --squash --delete-branch
```
Squash lands it as `chore(release): vX.Y.Z (#NN)` on `main` (matches prior releases).

## 4. Tag the merged commit + push (triggers release.yml)
```bash
git checkout main && git pull --ff-only        # fast-forward to the merged release commit
git tag vX.Y.Z
git push origin vX.Y.Z                          # push ONLY the tag — main is already up to date
```
`release.yml` (on `push: tags: v*`) builds, then packs the
NuGets (`Rask.Server`, `Rask.Wasm`, `Rask.Wasm.Hosting`, `Rask.Validation.DataAnnotations`,
`Rask.Validation.FluentValidation`, `Rask.Bootstrap`, `Rask.WebPush`, `Rask.Cli`, …), pushes them
to nuget.org, and creates the
GitHub release. Watch it (`run watch` on the bare run id, not a job, exits on the run's conclusion):

> **`release.yml` runs NO tests** — not the unit gate, not E2E. It restores, builds, packs and
> pushes. (This skill used to claim it ran the unit gate and a sharded E2E matrix; those jobs went
> with `ci.yml`/`e2e.yml`.) Nothing between your tag and nuget.org will catch a regression, and a
> push to nuget.org is permanent. **Run `scripts/run-unit-local.sh` and `scripts/run-e2e-local.sh`
> before you tag.**
```bash
gh run list --workflow=release.yml -L 1     # grab the run id
gh run watch <run-id> --exit-status
```

## 5. Verify
- GitHub release created with the eight `.nupkg` assets (`gh release view vX.Y.Z`).
- Packages visible on nuget.org at version `X.Y.Z`.
- To undo a mistaken tag **before** publish completes: `git push --delete origin vX.Y.Z`. Once
  `release.yml` has pushed to nuget.org, the version is permanent (nuget rejects a re-push of the
  same version) — bump to the next patch instead of retrying `X.Y.Z`.
