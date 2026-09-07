---
name: cut-release
description: Cut a Rask release. Use when publishing a new version to NuGet/GitHub. Promotes the CHANGELOG [Unreleased] section to a dated version, then tags vX.Y.Z so MinVer derives the version and release.yml builds, packs and pushes the NuGet packages and GitHub release. release.yml runs no tests, so the local gates must be run before tagging.
---

# cut-release

Versioning is **MinVer from git tags** (`Directory.Build.props` → `<MinVerTagPrefix>v</…>`).
There is no version number to bump in a file — **the tag is the version**.

**Order matters: land the CHANGELOG on `main` FIRST, then tag that commit.** Tagging a local commit
you have not pushed puts the tag on something `main` does not have.

Commit **directly to `main`** — that is the standing policy for the owner's own work, and PRs are for
external contributions. `main` still carries a "require a pull request" rule, but `enforce_admins` is
off, so an admin push is accepted. A **commitlint** check enforces Conventional Commits, so a bare
`Release vX.Y.Z` FAILS (`type-empty` / `subject-empty`) — use `chore(release): vX.Y.Z`.

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

## 2. Promote the CHANGELOG
In `CHANGELOG.md`, rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD` (keep its
`### Added/Changed/Fixed/...` subsections), and add a fresh empty `## [Unreleased]` above it.
Commit with a **Conventional Commit** message (plain `Release vX.Y.Z` FAILS commitlint):
```bash
git add CHANGELOG.md && git commit -m "chore(release): vX.Y.Z"
git push origin main
```

## 3. Tag that commit + push (triggers release.yml)
```bash
git pull --ff-only                             # make sure nothing landed in between
git tag vX.Y.Z
git push origin vX.Y.Z                          # push ONLY the tag — main is already up to date
```
`release.yml` (on `push: tags: v*`) builds, packs every project that is not `IsPackable=false`,
pushes them to nuget.org, and creates the GitHub release.

Deliberately NOT enumerated here. This list said eight packages, named `Rask.Bootstrap` (no longer
packable), and predated a dozen projects — a hand-kept list of a set the build already knows is one
that rots quietly. Ask the tree instead:

```bash
for f in src/*/*.csproj; do grep -q "<IsPackable>false" "$f" || basename "$f" .csproj; done
```

Watch the run (`run watch` on the bare run id, not a job, exits on the run's conclusion):

> **`release.yml` runs NO tests** — not the unit gate, not E2E. It restores, builds, packs and
> pushes. (This skill used to claim it ran the unit gate and a sharded E2E matrix; those jobs went
> with `ci.yml`/`e2e.yml`.) Nothing between your tag and nuget.org will catch a regression, and a
> push to nuget.org is permanent. **Run `scripts/run-unit-local.sh` and `scripts/run-e2e-local.sh`
> before you tag.**
```bash
gh run list --workflow=release.yml -L 1     # grab the run id
gh run watch <run-id> --exit-status
```

## 4. Verify
- GitHub release created, carrying one `.nupkg` per packable project (`gh release view vX.Y.Z`);
  compare against the loop in step 3 rather than against a number written down here.
- Packages visible on nuget.org at version `X.Y.Z`.
- To undo a mistaken tag **before** publish completes: `git push --delete origin vX.Y.Z`. Once
  `release.yml` has pushed to nuget.org, the version is permanent (nuget rejects a re-push of the
  same version) — bump to the next patch instead of retrying `X.Y.Z`.
