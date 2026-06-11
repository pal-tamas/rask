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
- ✅ Require status checks to pass: `unit`, the `e2e` shards, and `commitlint`.
- ✅ Require branches to be up to date before merging.
- ✅ Do not allow bypassing the above settings (so even pushes must go through PRs).
- ✅ Restrict who can push to matching branches → only @pal-tamas (blocks direct pushes/merges).

Apply via CLI (example):
```bash
gh api -X PUT repos/pal-tamas/rask/branches/main/protection \
  -H "Accept: application/vnd.github+json" \
  -f required_pull_request_reviews.require_code_owner_reviews=true \
  -F required_pull_request_reviews.required_approving_review_count=1 \
  -F enforce_admins=true \
  -f 'required_status_checks.checks[][context]=unit' \
  -f 'required_status_checks.checks[][context]=commitlint' \
  -F required_status_checks.strict=true \
  -F restrictions.users[]=pal-tamas -F 'restrictions.teams[]' -F 'restrictions.apps[]'
```

## Secrets used by workflows
- `NUGET_API_KEY` — nuget.org push (used by `release.yml` and `nightly.yml`).
- `GITHUB_TOKEN` — provided automatically (GitHub Packages + releases).
