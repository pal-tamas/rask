#!/usr/bin/env bash
# Verify the Linux half of the `.test` dev host against a real Linux machine.
#
# `DevHostLinuxE2ETests` installs a certificate authority into the system trust store, rewrites
# /etc/hosts and sets a sysctl. That is not something to run on a developer's own box, and it is also
# the only way to find out whether any of it works — so it runs here, in a container that is thrown away
# afterwards.
#
# What it proves, end to end: an authority trusted through the distribution's CA anchors and through
# NSS, a hostname the resolver answers, an unprivileged process bound to port 443, and finally `curl`
# completing a TLS handshake to https://appname.test with no --cacert and nothing told about the
# authority. Only a correctly installed system trust makes that last step succeed.
#
# Usage: scripts/run-devhost-linux-local.sh
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
image="rask-devhost-linux"

docker_cli="${DOCKER:-docker}"
if ! command -v "$docker_cli" >/dev/null 2>&1; then
  echo "devhost gate: no '$docker_cli' on PATH. Install Docker Desktop, Colima or podman." >&2
  exit 1
fi

if ! "$docker_cli" info >/dev/null 2>&1; then
  echo "devhost gate: '$docker_cli' is installed but its daemon is not answering." >&2
  exit 1
fi

echo "==> Building $image"
"$docker_cli" build -t "$image" -f "$repo_root/scripts/devhost/Dockerfile" "$repo_root/scripts/devhost"

# The source is COPIED IN rather than bind-mounted, and that is not incidental.
#
# The container runs as an ordinary user with passwordless sudo, because that is the only way the
# `sudo -n` paths in LinuxDevHostPlatform get exercised at all — and the only way binding port 443 is a
# real assertion about the sysctl rather than a privilege root already had. An unprivileged container
# user cannot write to a mount owned by the host's uid, and parts of this build write inside the source
# tree, so a mount would fail before reaching the test. Copying gives the container user a tree it owns.
#
# `git ls-files` picks the payload, which excludes bin/, obj/ and node_modules/ for free — and keeps a
# macOS/arm64 build from travelling into a Linux container that would then try to reuse it.
echo "==> Packing the working tree"
tarball="$(mktemp -t rask-devhost-XXXXXX).tar.gz"
trap 'rm -f "$tarball"' EXIT

# samples/ and benchmarks/ are dropped: nothing under tests/Rask.Cli.Tests references them, and the meta
# samples commit their own build output — hashed filenames that a later rebuild renames, so `git
# ls-files` routinely names files that are no longer on disk and tar stops on the first one.
#
# Names git knows about are then filtered down to files that actually exist. A tracked-but-absent file
# is not a reason to fail a gate about certificates, and this is the portable way to skip them:
# --ignore-failed-read is GNU tar only, and macOS ships bsdtar.
#
# COPYFILE_DISABLE=1 stops macOS's tar writing an AppleDouble "._name" companion beside every file. They
# land in the archive as siblings of real sources, and Linux has no idea they are metadata — the C#
# compiler picks up ._Foo.cs alongside Foo.cs and fails with "is a binary file instead of a text file".
git -C "$repo_root" ls-files --cached --others --exclude-standard -z \
  | grep -zv '^samples/' \
  | grep -zv '^benchmarks/' \
  | while IFS= read -r -d '' file; do
      [ -e "$repo_root/$file" ] && printf '%s\0' "$file"
    done \
  | COPYFILE_DISABLE=1 tar -czf "$tarball" -C "$repo_root" --null -T -

echo "==> Running the Linux dev-host gate"
# --privileged for the sysctl: the container is the machine under test and has to be able to change
# itself. Nothing here reaches the host.
"$docker_cli" run --rm --privileged -i "$image" '
  set -euo pipefail
  mkdir -p "$HOME/repo"
  tar xzf - -C "$HOME/repo"
  cd "$HOME/repo"
  dotnet test tests/Rask.Cli.Tests/Rask.Cli.Tests.csproj \
    --filter "FullyQualifiedName~DevHostLinuxE2ETests" \
    --logger "console;verbosity=normal"
' < "$tarball"

echo "==> Linux dev-host gate passed."
