#!/usr/bin/env python3
"""Decide whether a `dotnet test` run proved what it claims, from the TRX files it wrote (#1308).

xUnit v3 runs each test assembly in a process of its own, which the VSTest adapter starts and reads.
When a test kills that process (a stack overflow, Environment.FailFast, Environment.Exit, an
exception on a background thread) the adapter prints `[FATAL ERROR] Xunit.Sdk.TestPipelineException`
and the tests that had not reported yet are simply never heard of again: the console still says
`Passed! ... 707` for a suite of 903. VSTest does turn that error into exit code 1, but only the exit
code says so, and an exit code is the first thing a pipe, a wrapper or a hurried reader loses.

So this reads the run's own record instead of trusting one number. For each TRX file under the
results directory it fails when:

  - the run's outcome is anything but Completed/Passed/Warning (Failed, Aborted, Error, ...);
  - the run logged an Error, or any message that names a crashed or aborted host;
  - fewer test results came back than the adapter discovered test cases to run;
  - the adapter's discovery count is missing, so the count above cannot be checked at all.

The discovery count is xUnit's own diagnostic line, "Discovered: <assembly> (N test cases to be
run)", which the adapter prints only with `xUnit.DiagnosticMessages=true` — the gate passes it, and
VSTest keeps it in the TRX file's StdOut. It counts the assembly's test cases BEFORE a VSTest
`--filter` applies, so an assembly the run filters on purpose is named with --count-exempt (an ERE
over the assembly name) and keeps every check but that one. A theory whose data cannot be
enumerated at discovery is one test case and several results, which is why the check is `<`.

    test_verdict.py <results-dir> [--count-exempt ERE]

Exit 0 when every TRX file accounts for every test, 1 otherwise (and when there is no TRX file at all:
a run that wrote no results proved nothing).
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
GOOD_OUTCOMES = {"Completed", "Passed", "Warning"}
DISCOVERED = re.compile(r"Discovered:\s+(\S+)\s+\((\d+) test cases? to be run\)")
HOST_DIED = re.compile(r"\[FATAL ERROR\]|Test host process crashed|test run was aborted|Test process crashed", re.I)


def problems_in(trx: Path, count_exempt: re.Pattern[str] | None) -> tuple[str, list[str]]:
    """The assembly a TRX file is about, and every reason it does not prove a green run."""
    try:
        summary = ET.parse(trx).getroot().find("t:ResultSummary", NS)
    except ET.ParseError as error:
        return trx.name, [f"the TRX file does not parse ({error}) — the run was cut short"]
    if summary is None:
        return trx.name, ["the TRX file has no ResultSummary — the run was cut short"]

    problems: list[str] = []
    stdout = summary.findtext("t:Output/t:StdOut", default="", namespaces=NS)
    discovered = DISCOVERED.findall(stdout)
    assembly = discovered[0][0] if discovered else trx.parent.name

    outcome = summary.get("outcome", "")
    if outcome not in GOOD_OUTCOMES:
        problems.append(f"the run's outcome is {outcome or 'missing'}")

    for info in summary.findall("t:RunInfos/t:RunInfo", NS):
        text = (info.findtext("t:Text", default="", namespaces=NS) or "").strip()
        first_line = text.splitlines()[0] if text else ""
        if info.get("outcome") == "Error" or HOST_DIED.search(text):
            problems.append(f"{info.get('outcome', 'message')}: {first_line}")

    counters = summary.find("t:Counters", NS)
    results = int(counters.get("total", "0")) if counters is not None else 0
    if not discovered:
        problems.append(
            "the xUnit adapter never reported how many test cases it discovered "
            "(is xUnit.DiagnosticMessages=true passed?), so nothing proves every test ran"
        )
    elif count_exempt is None or not count_exempt.search(assembly):
        expected = sum(int(count) for _, count in discovered)
        if results < expected:
            problems.append(
                f"{results} result(s) for {expected} discovered test case(s) — "
                f"{expected - results} never ran; the test host died mid-run"
            )

    return assembly, problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("results", type=Path)
    parser.add_argument("--count-exempt", default="")
    args = parser.parse_args()
    exempt = re.compile(args.count_exempt) if args.count_exempt else None

    trx_files = sorted(args.results.rglob("*.trx")) if args.results.is_dir() else []
    if not trx_files:
        print(f"test verdict: no TRX file under {args.results} — the run wrote no results, so it proved nothing.",
              file=sys.stderr)
        return 1

    failed = 0
    for trx in trx_files:
        assembly, problems = problems_in(trx, exempt)
        for problem in problems:
            print(f"test verdict: {assembly}: {problem}", file=sys.stderr)
        failed += bool(problems)

    if failed:
        print(f"test verdict: FAILED — {failed} of {len(trx_files)} assembly run(s) did not account for "
              "every test, whatever the summary line above says.", file=sys.stderr)
        return 1
    print(f"==> Test verdict: {len(trx_files)} assembly run(s), every discovered test reported a result.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
