#!/usr/bin/env python3
"""Per-project analyzer seconds out of a detailed MSBuild log, for dealing scripts/lib/analyzer-shards.txt.

An analyzer shard (scripts/run-unit-local.sh, RASK_UNIT_PART=analyzers-K) builds with -p:ReportAnalyzer=true
and a binary log, replays that log at detailed verbosity and pipes it here. The compiler prints
"Total analyzer execution time: N seconds." once per compilation (one per target framework), under the
project instance prefix ("12:3>") MSBuild gives every line of a parallel build; the line that starts an
instance names its project. Prints the projects slowest first, and their total.

Usage: analyzer_report.py <replayed detailed log>
"""

import re
import sys
from collections import defaultdict
from pathlib import Path

PREFIX = re.compile(r"^\s*(\d+(?::\d+)?)>")
# `1>Project "a.csproj" on node 1 (default targets).` and
# `1:15>Project "a.csproj" (1:15) is building "b.csproj" (1:16) on node 2 (Build target(s)).`
STARTS = re.compile(r'Project "([^"]+)" (?:\((?:\d+(?::\d+)?)\) is building "([^"]+)" \((\d+(?::\d+)?)\) )?on node')
TOTAL = re.compile(r"Total analyzer execution time: ([0-9.,]+) seconds")


def main() -> int:
    projects: dict[str, str] = {}
    seconds: defaultdict[str, float] = defaultdict(float)
    current = ""
    with Path(sys.argv[1]).open(encoding="utf-8", errors="replace") as log:
        for line in log:
            prefix = PREFIX.match(line)
            if prefix:
                current = prefix.group(1)
                start = STARTS.search(line)
                if start:
                    if start.group(2):
                        projects[start.group(3)] = start.group(2)
                    else:
                        projects.setdefault(current, start.group(1))
            total = TOTAL.search(line)
            if total and current:
                name = Path(projects.get(current, current)).stem
                seconds[name] += float(total.group(1).replace(",", "."))

    if not seconds:
        print("    (no analyzer ran in this build)")
        return 0
    for name, secs in sorted(seconds.items(), key=lambda item: -item[1]):
        print(f"    {secs:8.1f}s  {name}")
    print(f"    {sum(seconds.values()):8.1f}s  in all, {len(seconds)} project(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
