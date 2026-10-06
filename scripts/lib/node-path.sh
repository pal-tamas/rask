# Puts Node on PATH for a gate started from a shell that never ran nvm's init.
#
# A gate started by an IDE, an agent or `env -i` inherits no nvm shim, so `node --version` fails and
# the islands build stops with RASKISLAND001 on a machine that has Node installed. This looks where nvm
# keeps its versions and takes the newest; whether that one is new enough stays the build's question
# (RaskExternalMinimumNode), so there is one bar and it is not restated here.
#
# Sourced, never executed. RASK_NVM_DIR exists for the test.

# Prints the bin directory of the newest Node under an nvm root, or nothing.
rask_newest_nvm_node() {
  local versions="${1:-${RASK_NVM_DIR:-${NVM_DIR:-$HOME/.nvm}}}/versions/node"
  [ -d "$versions" ] || return 0

  # Newest first, and the first one that really holds a node: a half-removed version leaves its
  # directory behind.
  local version
  for version in $(ls "$versions" 2>/dev/null | sed -n 's/^v\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)$/\1/p' \
    | sort -t. -k1,1nr -k2,2nr -k3,3nr); do
    if [ -x "$versions/v$version/bin/node" ]; then
      echo "$versions/v$version/bin"
      return 0
    fi
  done
}

rask_ensure_node() {
  command -v node >/dev/null 2>&1 && return 0

  local bin
  bin="$(rask_newest_nvm_node)"
  if [ -n "$bin" ]; then
    PATH="$bin:$PATH"
    export PATH
    echo "node-path: node was not on PATH — using $bin"
  else
    echo "node-path: node is not on PATH and no nvm install was found; the islands build will say RASKISLAND001."
  fi
}
