# Repository administration

One-time GitHub settings that back the committed governance files. These are repo **settings**
(not files), so apply them in the GitHub UI or with `gh`. Goal: **contributions are open** —
anyone can open issues and PRs — but **only the owner (@pal-tamas) can merge**, for now.

## Enable community features
- **Settings → General → Features:** enable **Issues** and **Discussions**.
- **Settings → General → Pull Requests:** enable **Automatically delete head branches**, and
  allow **Squash merging** (Conventional-Commit titles → clean history).

## Protect `main` (only the owner merges)
**Settings → Branches → Add branch ruleset** (or classic protection) for `main`:
- ✅ Require a pull request before merging
  - ✅ Require approvals (1)
  - ✅ **Require review from Code Owners** ← with `.github/CODEOWNERS` (`* @pal-tamas`) this means
    no PR merges without the owner's approval.
- ✅ Require status checks to pass: see below — in practice this list stays **empty**.
- ✅ Require branches to be up to date before merging.
- ✅ Do not allow bypassing the above settings (so even pushes must go through PRs).
- ✅ Restrict who can push to matching branches → only @pal-tamas (blocks direct pushes/merges).

### Why the required-checks list is empty

**This repo gates locally, not in CI.** The unit suite, the browser E2E journeys, the CLI build gate
and the payload-bytes gates all run from `.githooks/pre-commit` / `.githooks/pre-push`, so nothing
leaves the machine unproven. `unit` and the `e2e` shards were listed here as required checks for a
long time and could never have engaged — they do not run in CI at all. See
[development-workflow.md](development-workflow.md).

`ci.yml` still runs `benchmarks` on every PR, as a second opinion rather than a gate. It rode red
through three merges without stopping anyone ([#919](https://github.com/pal-tamas/rask/issues/919)),
which is what a non-required check on a repo with no required checks does. The fix was to give it
teeth locally — `scripts/run-benchmarks-local.sh`, wired into pre-push — not to start requiring it.

If a required check is ever added, read the live state back: `contexts: []` means nothing is enforced,
whatever this file claims.

```bash
gh api repos/pal-tamas/rask/branches/main/protection \
  --jq '{checks: .required_status_checks.checks, strict: .required_status_checks.strict,
         enforce_admins: .enforce_admins.enabled}'
```

Add one with the **required-status-checks sub-resource**, never `PUT …/protection` — the latter
replaces the whole object, so a partial body silently drops `enforce_admins`, conversation resolution
and the force-push/deletion blocks:

```bash
gh api -X PATCH repos/pal-tamas/rask/branches/main/protection/required_status_checks \
  --input - <<'JSON'
{"strict": true, "checks": [{"context": "benchmarks"}]}
JSON
```

Mind the name: `benchmarks` is the `ci.yml` job. **`benchmarks-full` is a different job**, in
`nightly.yml`, running the noisy BenchmarkDotNet timing suites — deliberately not a gate, and currently
red ([#922](https://github.com/pal-tamas/rask/issues/922)). Requiring that one would block `main`.

The reviews-and-restrictions half, set once (example):
```bash
gh api -X PUT repos/pal-tamas/rask/branches/main/protection \
  -H "Accept: application/vnd.github+json" \
  -f required_pull_request_reviews.require_code_owner_reviews=true \
  -F required_pull_request_reviews.required_approving_review_count=1 \
  -F enforce_admins=true \
  -F required_status_checks.strict=true \
  -F restrictions.users[]=pal-tamas -F 'restrictions.teams[]' -F 'restrictions.apps[]'
```

## Secrets used by workflows
- `NUGET_API_KEY` — nuget.org push (used by `release.yml` and `nightly.yml`).
- `GITHUB_TOKEN` — provided automatically (GitHub Packages + releases).
