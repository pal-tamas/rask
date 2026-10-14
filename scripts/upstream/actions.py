#!/usr/bin/env python3
"""Move every action the scaffolded deploy workflow uses (`rask deploy --github`) to its newest major.

The workflow is a string in src/Rask.Cli/Scaffolding/GitHubActionsWorkflow.cs, which Dependabot cannot
read; Rask's own workflows are Dependabot's. A major is what a workflow pins (`@v6`), so that is what moves.

Usage:  scripts/upstream/actions.py
Exit:   0 when it could tell (the diff is the report) · 1 the repo changed shape
"""
import re
import sys

from upstream import ShapeChanged, fetch_json, follow, read, substitute

WORKFLOW = "src/Rask.Cli/Scaffolding/GitHubActionsWorkflow.cs"
USES = re.compile(r"uses: ([\w.-]+/[\w.-]+)@v(\d+)\b")


def step():
    used = dict(USES.findall(read(WORKFLOW)))
    if not used:
        raise ShapeChanged(f"{WORKFLOW}: no `uses: owner/action@vN`")
    moved = []
    for action, major in sorted(used.items()):
        tag = fetch_json(f"https://api.github.com/repos/{action}/releases/latest")["tag_name"]
        latest = re.match(r"v(\d+)", tag)
        if latest and int(latest.group(1)) > int(major):
            substitute(WORKFLOW, rf"(uses: {re.escape(action)}@v)\d+\b", rf"\g<1>{latest.group(1)}")
            moved.append(f"{action} v{major} -> v{latest.group(1)}")
    return ", ".join(moved)


if __name__ == "__main__":
    sys.exit(follow("scaffolded actions", step))
