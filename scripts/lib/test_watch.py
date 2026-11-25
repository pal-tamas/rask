#!/usr/bin/env python3
"""Run a `dotnet test` command with a deadline, and stop it at its first failing test.

scripts/lib/dotnet-test.sh runs every gate's `dotnet test` through this. It streams the command's output
through unchanged and ends the command's whole process tree (it runs in a session of its own) when:

  - the run outlives --deadline seconds. --blame-hang-timeout names a hung TEST, but not every hang is
    one: a test process the hang dump left behind, a fixture's teardown, a child server still holding the
    pipe all keep `dotnet test` waiting with no test in flight, and until 2026-11-25 that ate the CI job's
    whole 40 minutes and GitHub kept no log of it. The `[Long Running Test]` lines xUnit prints above
    (xUnit.LongRunningTestSeconds) name what was still running.
  - with --fail-fast, a test failed or a host died. The lines that follow it for a few seconds (its
    message and stack) are let through first; the rest of the run is not waited for.

    test_watch.py [--deadline SECONDS] [--fail-fast] -- <command...>

Exit status: the command's own; 124 when the deadline ended it; 1 when --fail-fast ended it.
SIGTERM/SIGINT end the command's tree too, so a caller
that stops this stops everything under it.
"""

from __future__ import annotations

import argparse
import os
import re
import signal
import subprocess
import sys
import threading
import time

# A failed test in VSTest's console logger (`  Failed Some.Test [12 ms]`), xUnit's own `[FAIL]` line, and
# the ways a host dies — the same signals scripts/lib/test_verdict.py reads back from the TRX files.
FAILED = re.compile(r"^\s+Failed \S.*\[[^\]]*\d ?(?:ms|s|m|h)\]\s*$|\[FAIL\]\s*$|\[FATAL ERROR\]|Test Run Aborted|Test host process crashed|test run was aborted", re.I)
GRACE_SECONDS = 5
# What a command that has exited may leave holding the output pipe (a test process, a server a test
# started) is ended this long after it, so the stream reaches its end.
STRAGGLER_SECONDS = 10


def end_tree(process: subprocess.Popen[bytes]) -> None:
    """TERM the command's session, then KILL whatever is left of it a few seconds later."""
    for sig, wait in ((signal.SIGTERM, 5.0), (signal.SIGKILL, 0.0)):
        try:
            os.killpg(process.pid, sig)
        except ProcessLookupError:
            return
        try:
            process.wait(timeout=wait or None)
            # The leader is gone; children it left in the group still get the KILL below.
        except subprocess.TimeoutExpired:
            pass
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--deadline", type=float, default=0, help="seconds; 0 waits for ever")
    parser.add_argument("--fail-fast", action="store_true")
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command:
        parser.error("no command to run")

    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, start_new_session=True)
    ended_by: list[str] = []
    stop_at: list[float] = []

    def stop(reason: str) -> None:
        if not ended_by:
            ended_by.append(reason)
            end_tree(process)

    def on_signal(signum: int, _frame: object) -> None:
        stop(f"signal {signum}")
        sys.exit(128 + signum)

    signal.signal(signal.SIGTERM, on_signal)
    signal.signal(signal.SIGINT, on_signal)

    def guard() -> None:
        began = time.monotonic()
        while process.poll() is None:
            now = time.monotonic()
            if args.deadline and now - began > args.deadline:
                stop("deadline")
                return
            if stop_at and now >= stop_at[0]:
                stop("fail-fast")
                return
            time.sleep(0.5)

    def guard_then_stragglers() -> None:
        guard()
        time.sleep(STRAGGLER_SECONDS)
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except (ProcessLookupError, PermissionError):
            pass

    threading.Thread(target=guard_then_stragglers, daemon=True).start()

    assert process.stdout is not None
    out = sys.stdout.buffer
    for raw in process.stdout:
        out.write(raw)
        out.flush()
        if args.fail_fast and not stop_at and FAILED.search(raw.decode("utf-8", "replace")):
            stop_at.append(time.monotonic() + GRACE_SECONDS)
    status = process.wait()

    if ended_by == ["deadline"]:
        minutes = args.deadline / 60
        print(f"test-watch: the run passed its {minutes:g}-minute deadline and was ended. A test it was still"
              " running is named by a [Long Running Test] line above.", flush=True)
        return 124
    if ended_by == ["fail-fast"]:
        print("test-watch: a test failed (above); the rest of the run was stopped (RASK_TEST_FAIL_FAST=0 runs it all).",
              flush=True)
        return 1
    # Popen reports a command a signal ended as minus the signal; a shell says 128 plus it.
    return 128 - status if status < 0 else status


if __name__ == "__main__":
    sys.exit(main())
