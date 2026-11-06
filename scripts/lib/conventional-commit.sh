# shellcheck shell=sh
# The Conventional Commits rule, in ONE place: .githooks/commit-msg applies it to every local commit
# and .github/workflows/commitlint.yml to every commit of a pull request, plus its title (a squash
# merge turns the title into the commit on main). The allowed types and the header limit are written
# here and nowhere else — the hook, the workflow and their error messages all read these two values.
#
# It replaced commitlint (a Node tool run by a GitHub action), which this repository no longer needs:
# nothing in it builds, runs or lints with Node. The rules kept are the ones commitlint.config.ts
# stated: the type list, the 100-character header, and a subject that is not UPPER CASE, PascalCase
# or Start Case. Plus config-conventional's no trailing full stop.
#
# POSIX sh: .githooks/commit-msg is `#!/bin/sh`. Nothing here may be a bashism.

RASK_COMMIT_TYPES="feat fix perf refactor docs test build ci chore revert"
RASK_COMMIT_MAX_HEADER=100

# rask_commit_header_error <header> — 0 when the header is acceptable (merge, revert, fixup and
# squash headers git writes itself always are). Otherwise prints why, one or more lines, and returns 1.
rask_commit_header_error() {
  _ch_header="$1"

  case "$_ch_header" in
    "Merge "* | "Revert "* | "fixup! "* | "squash! "*) return 0 ;;
  esac

  _ch_length=$(printf '%s' "$_ch_header" | wc -m | tr -d ' ')
  if [ "$_ch_length" -gt "$RASK_COMMIT_MAX_HEADER" ]; then
    printf 'the header is %s characters; the limit is %s. Say it shorter and put the rest in the body.\n' \
      "$_ch_length" "$RASK_COMMIT_MAX_HEADER"
    return 1
  fi

  _ch_types=$(printf '%s' "$RASK_COMMIT_TYPES" | tr ' ' '|')
  if ! printf '%s' "$_ch_header" | grep -Eq "^($_ch_types)(\([a-z0-9._-]+\))?!?: [^ ]"; then
    printf '%s\n' \
      'it is not a Conventional Commit: type(scope): subject' \
      "type is one of: $(printf '%s' "$RASK_COMMIT_TYPES" | sed 's/ /, /g')" \
      'e.g. "feat(forms): add RadioGroup disabled state"'
    return 1
  fi

  _ch_subject="${_ch_header#*: }"
  case "$_ch_subject" in
    *.)
      printf 'the subject ends with a full stop.\n'
      return 1
      ;;
  esac

  # commitlint's subject-case [never, upper-case, pascal-case, start-case]. A subject that merely
  # STARTS with a capital ("Ui.Button keeps its tone") is fine; one that is shouting, one CamelWord,
  # or Every Word Capitalised is not.
  if printf '%s' "$_ch_subject" | grep -q '[A-Za-z]' &&
    ! printf '%s' "$_ch_subject" | grep -q '[a-z]'; then
    printf 'the subject is UPPER CASE.\n'
    return 1
  fi
  if printf '%s' "$_ch_subject" | grep -Eq '^([A-Z][a-z0-9]*)+$'; then
    printf 'the subject is PascalCase; write it as a sentence.\n'
    return 1
  fi
  if printf '%s' "$_ch_subject" | grep -Eq '^[A-Z][A-Za-z0-9]*( [A-Z][A-Za-z0-9]*)+$'; then
    printf 'the subject is Start Case; write it as a sentence.\n'
    return 1
  fi

  return 0
}
