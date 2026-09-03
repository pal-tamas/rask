#!/usr/bin/env bash
# A gate's claim on this machine, expressed as a process.
#
#   argv:  bash <path>/lane-claim.sh <owner-pid> <cost> <phase>
#
# The ARGV IS THE CLAIM. This script does no work; it exists so that `ps -o command=` in another
# worktree can read how many slots the owning gate is taking and for what. The body's only job is to
# make the claim die with its owner.
#
# Why a process rather than a file, when scripts/lib/machine-lane.sh could just as easily have written
# /tmp/rask-lane/<pid>: because a file survives kill -9 and a laptop sleep and then wedges every gate
# on the machine until someone works out what to delete. That is the reasoning scripts/lib/
# e2e-concurrency.sh has always given for detecting gates by process, and it applies with more force
# here -- this claim gates BUILDS, not just the browser suite, so a stale one would slow every commit
# on the box indefinitely, silently, and in a way whose cause is a file nobody knows about.
#
# Self-healing twice over. A reader ignores any claim whose owner `ps` no longer knows about, so a
# kill -9'd gate stops counting immediately, before this process has even noticed. And this process
# polls its owner and exits on its own within RASK_LANE_MARKER_POLL seconds of the owner dying, so an
# orphan does not linger either. Neither path needs anyone to clean anything up.
#
# Why a claim is advertised at all, rather than the reader inferring the phase from the gate's own
# process tree (the gate's `dotnet test` child is, after all, visible): because a gate that is WAITING
# for slots has no such child yet. It would read as still building, its juniors would under-count it,
# and one of them would start work that the waiter is about to need the whole machine for. The claim
# has to state the REQUEST, not the current activity -- otherwise "exclusive" is only exclusive once
# it is too late to matter.

owner="${1:-}"

if [ -z "$owner" ]; then
  echo "lane-claim.sh: no owner pid given" >&2
  exit 2
fi

trap 'exit 0' TERM INT

# The sleep is BACKGROUNDED and waited on, which looks like a pointless detour and is not.
#
# Bash runs trap handlers only between foreground commands, so with a plain `sleep N` in the loop the
# TERM above does not arrive until that sleep finishes on its own. rask_lane_release then blocks for up
# to a full poll on every gate exit and on every claim that replaces another. Measured on this machine
# with the poll set to 30s: release took 28,982ms. `wait` IS interruptible by a trap, so backgrounding
# the sleep and waiting on it makes the handler run immediately -- which is what the release path has
# always assumed, and what an earlier comment here wrongly claimed was already true.
while kill -0 "$owner" 2>/dev/null; do
  sleep "${RASK_LANE_MARKER_POLL:-5}" &
  wait $!
done
