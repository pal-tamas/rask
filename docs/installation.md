# Installing Rask

One command, on a machine with nothing on it:

```bash
curl -sSL https://rask.sh/rask.sh | sh
```

```powershell
# Windows
irm https://rask.sh/rask.ps1 | iex
```

That installs the `rask` CLI **and the dependencies the CLI actually shells out to**, then runs
[`rask doctor`](cli.md) so you can see the result. Everything lands in your home directory: no
`sudo`, no elevated prompt, no distro package manager, and nothing written outside `$HOME`.

Already have the .NET 10 SDK and want only the tool? `dotnet tool install -g Rask.Cli` is still the
whole story — the script exists for the case where you don't.

## What it installs

| | What | Why | If it's already there |
|---|---|---|---|
| **Always** | **.NET 10 SDK** into `~/.dotnet` | `rask` is a `net10.0` [global tool](https://learn.microsoft.com/dotnet/core/tools/global-tools), and every command shells out to `dotnet` | left alone — any SDK 10 or newer satisfies it |
| **Always** | **`Rask.Cli`** as a global tool | the `rask` command itself | updated instead of installed |
| **Always** | **`dotnet-ef`** | `rask db add` / `update` / `list` / `drop` | left alone |
| **Always** | **`wasm-tools` workload** | every `net10.0-browser` build — the WASM templates, and `--wasm` on a server app | left alone |
| **Always** | **Node.js LTS** into `~/.local/share/rask/node` | `rask new --template react\|vue\|svelte\|solid\|lit\|preact\|angular` and the meta framework templates (`nuxt\|nextjs\|sveltekit\|solidstart\|tanstack-start\|analog`), and `rask dev`'s dev server. The meta lane also needs node **at runtime**, not just at build time. | left alone if `node --version` is ≥ 24.15 (the Active LTS line the scaffolders track) |
| **Never** | Docker | `rask deploy`, `rask db backup --remote` | detected and reported only |

Docker is deliberately not installed. Putting a container runtime on someone's workstation is a big,
hard-to-reverse change, and only two commands need it — the script tells you the one line to run if
you want it.

The .NET SDK is only installed **when there isn't one**. An SDK you installed with `brew`, `apt`, or
the Microsoft installer is detected and used as-is; the script never touches it, and never changes
which `dotnet` your other projects resolve.

Node is downloaded from `nodejs.org` and its **SHA-256 is verified against the published
`SHASUMS256.txt`** before anything is unpacked.

## Options

```bash
curl -sSL https://rask.sh/rask.sh | sh -s -- --prerelease
```

The `-s --` is how you pass an option through a pipe: `sh` reads the script from stdin, and
everything after `--` becomes the script's arguments.

| Option | Environment equivalent | Effect |
|---|---|---|
| `--version <v>` | `RASK_INSTALL_PACKAGE` pins the package | install a specific `Rask.Cli` version |
| `--prerelease` | | install the latest [nightly](development-workflow.md) prerelease |
| `--no-sdk` | | never install the .NET SDK, even when none is found |
| `--no-ef` | | skip `dotnet-ef` (`rask db` installs it on first use anyway) |
| `--no-wasm-tools` | | skip the workload — a server-only app never needs it |
| `--no-node` | | skip Node — only the SPA templates need it |
| `--no-path` | | never write to a shell profile |
| `--dry-run` | | print every step and change nothing |
| `--quiet` | | print only errors and the final summary |
| `--help` | | the option list |

On Windows the same options are PowerShell switches (`-Prerelease`, `-NoNode`, `-DryRun`, …). `iex`
cannot pass arguments, so use a script block when you need one:

```powershell
& ([scriptblock]::Create((irm https://rask.sh/rask.ps1))) -Prerelease
```

### Install locations

Every path is overridable from the environment, on both scripts:

| Variable | Default (Unix) | Default (Windows) |
|---|---|---|
| `RASK_INSTALL_DOTNET_ROOT` | `$DOTNET_ROOT`, else `~/.dotnet` | `%USERPROFILE%\.dotnet` |
| `RASK_INSTALL_PREFIX` | `~/.local/share/rask` | `%LOCALAPPDATA%\rask` |
| `RASK_INSTALL_DOTNET_CHANNEL` | `10.0` | `10.0` |
| `RASK_INSTALL_NODE_MIN` | `24.15.0` | `24.15.0` |
| `RASK_INSTALL_PACKAGE` | `Rask.Cli` | `Rask.Cli` |

## Upgrading

Re-run the same one-liner. The script is idempotent: it updates the tool rather than failing on an
existing install, rewrites its `PATH` block instead of appending a second copy, and leaves every
dependency that is already current untouched.

`dotnet tool update -g Rask.Cli` still works and is faster if the tool is all you want to move.

## Uninstalling

```bash
dotnet tool uninstall -g Rask.Cli
rm -rf ~/.local/share/rask          # the Node the installer unpacked, if any
```

Then delete the block between `# >>> rask installer >>>` and `# <<< rask installer <<<` from your
shell profile. Leave `~/.dotnet` alone unless you are sure nothing else uses it.

On Windows: `dotnet tool uninstall -g Rask.Cli`, remove `%LOCALAPPDATA%\rask`, and drop the two rask
entries from your user `Path`.

## Troubleshooting

**`rask: command not found` right after installing.** The script writes to a shell profile, which
your *current* shell has already read. Open a new terminal, or run the `export PATH=…` line the
script printed.

**`rask` says "You must install .NET to run this application" — but `dotnet` works.** A .NET global
tool is an *apphost*, and an apphost does not look on `PATH` for a runtime: it reads `DOTNET_ROOT`,
then the registered location, then the default install directory. With the SDK in `~/.dotnet` it
needs `DOTNET_ROOT` set, which the installer writes into your profile alongside the `PATH` lines. If
you passed `--no-path`, set it yourself:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
```

The installer only writes `DOTNET_ROOT` when it actually installed an SDK into `~/.dotnet` — setting
it on a machine whose SDK lives elsewhere would break the SDK that was working.

**"installing the .NET SDK needs `bash`."** Microsoft's `dotnet-install.sh` is a bash script, even
though `rask.sh` itself is POSIX `sh`. Nearly every system has bash; a minimal container may not.
Install it, or install the SDK yourself and re-run with `--no-sdk`.

**Which profile does it write?** `~/.zshrc` for zsh, `~/.bashrc` for bash on Linux, `~/.bash_profile`
for bash on macOS (a Terminal tab there is a login shell, which never reads `.bashrc`),
`~/.config/fish/config.fish` for fish, and `~/.profile` otherwise. `--no-path` skips it entirely.

**"could not install the wasm-tools workload."** You have a machine-wide .NET SDK, so its workload
directory is root-owned. Run `sudo dotnet workload install wasm-tools` (Windows: an Administrator
terminal). The script never elevates on your behalf. Only browser-wasm builds need it — a server app
builds fine without.

**Behind a proxy, or offline.** The script needs `nuget.org`, `dot.net` and `nodejs.org`. With an
SDK and Node already installed, `--no-sdk --no-node` reduces that to `nuget.org` alone.

**Reviewing it before running it.** It is a single POSIX `sh` file with no dependencies, served from
this repository — read it at [`rask.sh`](https://github.com/pal-tamas/rask/blob/main/rask.sh), or:

```bash
curl -sSL https://rask.sh/rask.sh -o rask.sh
less rask.sh
sh rask.sh --dry-run     # prints every step, changes nothing
sh rask.sh
```

`--dry-run` is exact rather than approximate: every command that would change anything on your
machine goes through one wrapper, and that wrapper prints instead of running.

> **A note on `curl | sh`.** Piping a download into a shell has a real hazard: a connection that
> drops halfway leaves `sh` executing *half a script*. `rask.sh` closes that structurally — every
> statement lives inside a function and the file's last line is the only thing that calls one, so a
> truncated read defines some functions and runs nothing. A test asserts that property on the file
> itself, and the same reasoning is why the script downloads Microsoft's `dotnet-install.sh` to a
> file before running it rather than piping it onward.

## Without the script

Nothing here is magic, and the manual path stays supported:

```bash
# 1. the .NET 10 SDK — https://dot.net
# 2. the tool
dotnet tool install -g Rask.Cli
# 3. what your project needs
dotnet tool install -g dotnet-ef       # rask db
dotnet workload install wasm-tools     # browser-wasm builds
# 4. Node 24 LTS — https://nodejs.org   (SPA templates only)
```

`rask doctor` reports what it finds either way.

## Next

- [Getting started](getting-started.md) — the first project, end to end.
- [The `rask` CLI](cli.md) — every command.
- [Deployment](deployment.md) — `rask deploy` onto a bare box.
