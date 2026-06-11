---
name: check-nuget-updates
description: Check for outdated NuGet dependencies in the Rask repo and propose safe bumps. Use periodically or before a release. Reads central package management (Directory.Packages.props), reports outdated/vulnerable/deprecated packages, and updates versions behind the rask-ship gate.
---

# check-nuget-updates

Versions are centrally managed in `Directory.Packages.props` (CPM). Dependabot also opens PRs
weekly (`.github/dependabot.yml`) — this skill is the manual/on-demand path.

## 1. Scan
```bash
dotnet restore Rask.slnx
dotnet list Rask.slnx package --outdated
dotnet list Rask.slnx package --vulnerable --include-transitive
dotnet list Rask.slnx package --deprecated
```

## 2. Triage
- **Security/vulnerable** → bump now (highest priority).
- **Patch/minor** → safe to bump; batch them.
- **Major** → bump deliberately, read release notes, expect API changes (esp. xunit, Playwright,
  Microsoft.CodeAnalysis.CSharp which the generators pin, ASP.NET 10.x stack).
- Keep the `Microsoft.Extensions.*` / `Microsoft.AspNetCore.*` family on the same version.

## 3. Apply + verify
Edit the `<PackageVersion>` entries in `Directory.Packages.props`, then run the **`rask-ship`**
gate (build is warnings-as-errors, so analyzer-rule changes from an SDK/analyzer bump surface
immediately). Note version bumps in `CHANGELOG.md`.
