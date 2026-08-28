<#
.SYNOPSIS
    Install the `rask` CLI and the dependencies it actually needs.

.DESCRIPTION
    The Windows twin of rask.sh, with the same behaviour and the same checks.

    `dotnet tool install -g Rask.Cli` is one line, but it only works on a box that already has the
    .NET 10 SDK, and it installs the tool and nothing else. The CLI shells out to more than that:
    `rask db` needs dotnet-ef, every browser-wasm build needs the wasm-tools workload, and the SPA
    templates need Node. Today each of those is discovered by failure. This script front-loads them.

    Everything is installed per-user. Nothing here needs an elevated prompt or writes outside the
    user profile, and an SDK already on the box is left exactly as it is. Docker is the deliberate
    exception: detected and reported, never installed — only `rask deploy` needs it.

    On truncation: PowerShell parses a script in full before executing any of it, so a connection
    dropped mid-`irm | iex` is a parse error that runs nothing. rask.sh has to earn the same
    property structurally, because sh executes as it reads.

.EXAMPLE
    irm https://pal-tamas.github.io/rask/rask.ps1 | iex

.EXAMPLE
    # `iex` cannot pass arguments, so use a script block when you need a flag.
    & ([scriptblock]::Create((irm https://pal-tamas.github.io/rask/rask.ps1))) -Prerelease

.NOTES
    Exit codes: 0 ok · 1 something failed · 2 bad arguments (matching the CLI's own error surface).
#>
param(
    # Install a specific Rask.Cli version instead of the latest stable.
    [string] $Version,
    # Install the latest nightly prerelease instead of the latest stable.
    [switch] $Prerelease,
    # Never install the .NET SDK, even when none is found.
    [switch] $NoSdk,
    # Skip the dotnet-ef tool (rask db installs it on first use anyway).
    [switch] $NoEf,
    # Skip the wasm-tools workload (needed by every browser-wasm build).
    [switch] $NoWasmTools,
    # Skip Node.js (needed by the SPA templates: react, vue, svelte, angular, ...).
    [switch] $NoNode,
    # Never write to the user PATH.
    [switch] $NoPath,
    # Print what would happen and change nothing.
    [switch] $DryRun,
    # Print only errors and the final summary.
    [switch] $Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # a progress bar over `irm | iex` is noise, and slow

# --- configuration ---------------------------------------------------------------------------
# Overridable from the environment, the same seam rask.sh exposes.

$DotnetChannel = if ($env:RASK_INSTALL_DOTNET_CHANNEL) { $env:RASK_INSTALL_DOTNET_CHANNEL } else { '10.0' }
$DotnetMajor = if ($env:RASK_INSTALL_DOTNET_MAJOR) { [int] $env:RASK_INSTALL_DOTNET_MAJOR } else { 10 }
$DotnetRoot = if ($env:RASK_INSTALL_DOTNET_ROOT) { $env:RASK_INSTALL_DOTNET_ROOT } else { Join-Path $env:USERPROFILE '.dotnet' }
$Prefix = if ($env:RASK_INSTALL_PREFIX) { $env:RASK_INSTALL_PREFIX } else { Join-Path $env:LOCALAPPDATA 'rask' }
$DotnetScriptUrl = if ($env:RASK_INSTALL_DOTNET_SCRIPT_URL) { $env:RASK_INSTALL_DOTNET_SCRIPT_URL } else { 'https://dot.net/v1/dotnet-install.ps1' }
$NodeDist = if ($env:RASK_INSTALL_NODE_DIST) { $env:RASK_INSTALL_NODE_DIST } else { 'https://nodejs.org/dist' }
$NodeMin = if ($env:RASK_INSTALL_NODE_MIN) { $env:RASK_INSTALL_NODE_MIN } else { '22.12.0' }
$Package = if ($env:RASK_INSTALL_PACKAGE) { $env:RASK_INSTALL_PACKAGE } else { 'Rask.Cli' }

$script:Warnings = @()

# --- output ----------------------------------------------------------------------------------

function Write-Say { param([string] $Text = '') if (-not $Quiet) { Write-Host $Text } }
function Write-Step { param([string] $Text) if (-not $Quiet) { Write-Host "==> $Text" } }
function Write-Detail { param([string] $Text) if (-not $Quiet) { Write-Host "    $Text" } }

# A dependency we could not install but that only some commands need. Collected and replayed in the
# summary, so a warning in step 3 is still visible after step 7 has scrolled past.
function Write-Warn {
    param([string[]] $Lines)
    $script:Warnings += $Lines[0]
    Write-Host "rask.ps1: $($Lines[0])" -ForegroundColor Yellow
    foreach ($line in $Lines | Select-Object -Skip 1) {
        Write-Host "          $line" -ForegroundColor Yellow
    }
}

function Stop-Install {
    param([int] $Code, [string[]] $Lines)
    Write-Host "rask.ps1: $($Lines[0])" -ForegroundColor Red
    foreach ($line in $Lines | Select-Object -Skip 1) {
        Write-Host "          $line" -ForegroundColor Red
    }
    exit $Code
}

# --- helpers ---------------------------------------------------------------------------------

# 0-or-better comparison tolerant of a leading `v` and a prerelease suffix: 10.0.100-preview.3
# compares as 10.0.100, which is what an SDK floor means here.
function Test-VersionGe {
    param([string] $Have, [string] $Want)
    if ([string]::IsNullOrWhiteSpace($Have)) { return $false }
    $normalise = {
        param($v)
        $v = $v.Trim().TrimStart('v')
        $v = ($v -split '-')[0]
        $parts = @($v -split '\.') + @('0', '0', '0')
        [version]::new([int] ($parts[0] -replace '\D', '0'), [int] ($parts[1] -replace '\D', '0'), [int] ($parts[2] -replace '\D', '0'))
    }
    try { return (& $normalise $Have) -ge (& $normalise $Want) } catch { return $false }
}

# The dotnet to use, by absolute path when we installed one. Same reasoning as rask.sh: prepending to
# PATH is not enough to guarantee the SDK we just installed is the one that runs, and handing a net10.0
# tool install to an older SDK fails with "Settings file 'DotnetToolSettings.xml' was not found in the
# package" — an error that says nothing about the version it ran under.
function Get-DotnetPath {
    foreach ($name in @('dotnet.exe', 'dotnet')) {
        $candidate = Join-Path $DotnetRoot $name
        if (Test-Path $candidate) { return $candidate }
    }
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $null
}

# Rask.Cli sets RollForward=Major, so any newer major runs the tool. Demanding exactly 10 would
# reinstall an SDK on every .NET 11 box.
function Test-DotnetSdk {
    $dotnet = Get-DotnetPath
    if (-not $dotnet) { return $false }
    try { $sdks = & $dotnet --list-sdks 2>$null } catch { return $false }
    foreach ($line in $sdks) {
        $version = ($line -split ' ')[0]
        $major = 0
        if ([int]::TryParse((($version -split '\.')[0]), [ref] $major) -and $major -ge $DotnetMajor) { return $true }
    }
    return $false
}

# True when the SDK to use is the per-user one, i.e. it is actually there.
#
# This gates DOTNET_ROOT, which is not the same thing as PATH. A global tool ships as an apphost, and
# an apphost does not search PATH for a runtime — it reads DOTNET_ROOT, then the registered location,
# then the default install dir. With the SDK anywhere else `dotnet` works perfectly while `rask` dies
# with "You must install .NET to run this application". Gated on the directory existing, because
# pointing DOTNET_ROOT at a path that is not there breaks a machine-wide SDK that was working.
function Test-LocalDotnet {
    (Test-Path (Join-Path $DotnetRoot 'dotnet.exe')) -or (Test-Path (Join-Path $DotnetRoot 'dotnet'))
}

# The directories this installer puts things in, in PATH order. Built with Join-Path rather than
# "$DotnetRoot\tools" so the separators are right on whatever PowerShell is running on — a hard-coded
# backslash silently produces a single unusable entry on Linux and macOS.
function Get-RaskPathEntries {
    @(
        $DotnetRoot
        (Join-Path $DotnetRoot 'tools')
        (Join-Path $Prefix 'node')
    )
}

# The installed tool, whatever this platform calls it.
function Get-RaskCommandPath {
    foreach ($name in @('rask.exe', 'rask')) {
        $candidate = Join-Path (Join-Path $DotnetRoot 'tools') $name
        if (Test-Path $candidate) { return $candidate }
    }
    $command = Get-Command rask -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    return $null
}

# Windows builds only, and only on Windows. PowerShell runs elsewhere, where OSArchitecture alone
# would happily name a win-x64 zip to download onto a Linux box.
function Get-NodeTriple {
    if (-not $IsWindows) { return $null }
    switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
        'X64' { 'win-x64' }
        'Arm64' { 'win-arm64' }
        'X86' { 'win-x86' }
        default { $null }
    }
}

# The index is newest-first; "lts" is a codename string on an LTS release and $false otherwise.
function Get-NodeLtsVersion {
    $index = Invoke-RestMethod -Uri "$NodeDist/index.json"
    foreach ($release in $index) {
        if ($release.lts -is [string]) { return $release.version.TrimStart('v') }
    }
    return $null
}

# --- steps -----------------------------------------------------------------------------------

function Step-Dotnet {
    Write-Step "Checking for the .NET $DotnetChannel SDK"

    if (Test-DotnetSdk) {
        Write-Detail "found $(& (Get-DotnetPath) --version 2>$null) — leaving it alone"
        return
    }
    if ($NoSdk) {
        Write-Warn @(
            "no .NET $DotnetChannel SDK found, and -NoSdk was passed.",
            '`rask` will not run until one is installed: https://dot.net')
        return
    }

    Write-Detail "not found — installing it into $DotnetRoot (per-user, no elevation)"
    if ($DryRun) { Write-Detail "(dry-run) dotnet-install.ps1 -Channel $DotnetChannel -InstallDir $DotnetRoot"; return }

    # Downloaded to a file and then run, never piped straight into the engine: a truncated download
    # would otherwise be executed as though it were the whole installer.
    $script = Join-Path ([System.IO.Path]::GetTempPath()) "rask-dotnet-install-$([guid]::NewGuid()).ps1"
    try {
        Invoke-WebRequest -Uri $DotnetScriptUrl -OutFile $script -UseBasicParsing
        if (-not (Test-Path $script) -or (Get-Item $script).Length -eq 0) {
            Stop-Install 1 @("downloaded an empty dotnet-install.ps1 from $DotnetScriptUrl.")
        }
        & $script -Channel $DotnetChannel -InstallDir $DotnetRoot -NoPath
    }
    finally {
        Remove-Item $script -Force -ErrorAction SilentlyContinue
    }
}

function Step-Rask {
    Write-Step "Installing $Package"

    $toolArgs = @('tool', 'install', '--global', $Package)
    if ($Version) { $toolArgs += @('--version', $Version) }
    if ($Prerelease) { $toolArgs += '--prerelease' }

    if ($DryRun) { Write-Detail "(dry-run) dotnet $($toolArgs -join ' ')"; return }

    & (Get-DotnetPath) @toolArgs > $null 2>&1
    if ($LASTEXITCODE -ne 0) {
        # Already installed is the common case on a re-run and on an upgrade, so fall back to
        # `update`. That makes this script idempotent and the upgrade path at once.
        Write-Detail 'already installed — updating instead'
        $toolArgs = @('tool', 'update', '--global', $Package)
        if ($Version) { $toolArgs += @('--version', $Version) }
        if ($Prerelease) { $toolArgs += '--prerelease' }
        & (Get-DotnetPath) @toolArgs > $null 2>&1
        if ($LASTEXITCODE -ne 0) {
            Stop-Install 1 @(
                "could not install or update $Package.",
                "Re-run with the output visible: dotnet tool install --global $Package")
        }
    }
}

function Step-Ef {
    if ($NoEf) { return }
    Write-Step 'Installing dotnet-ef (rask db)'

    & (Get-DotnetPath) ef --version > $null 2>&1
    if ($LASTEXITCODE -eq 0) { Write-Detail 'already installed'; return }
    if ($DryRun) { Write-Detail '(dry-run) dotnet tool install --global dotnet-ef'; return }

    & (Get-DotnetPath) tool install --global dotnet-ef > $null 2>&1
    if ($LASTEXITCODE -ne 0) {
        # Not fatal: the CLI installs it on first use, so `rask db` still works from a cold start.
        Write-Warn @(
            'could not install dotnet-ef.',
            '`rask db` will install it on first use, or: dotnet tool install -g dotnet-ef')
    }
}

function Step-WasmTools {
    if ($NoWasmTools) { return }
    Write-Step 'Installing the wasm-tools workload (browser-wasm builds)'

    $installed = & (Get-DotnetPath) workload list 2>$null | Select-String -Pattern '^wasm-tools' -Quiet
    if ($installed) { Write-Detail 'already installed'; return }
    if ($DryRun) { Write-Detail '(dry-run) dotnet workload install wasm-tools'; return }

    & (Get-DotnetPath) workload install wasm-tools > $null 2>&1
    if ($LASTEXITCODE -ne 0) {
        # Against a machine-wide SDK the workload directory is admin-owned. We print the command
        # rather than relaunching elevated: an installer that silently prompts for admin is a worse
        # trade than one that tells you what to run.
        Write-Warn @(
            'could not install the wasm-tools workload.',
            'Your .NET SDK is probably machine-wide, so the workload needs an elevated prompt:',
            '  dotnet workload install wasm-tools   (from an Administrator terminal)',
            'Only browser-wasm builds need it — a server app is unaffected.')
    }
}

function Step-Node {
    if ($NoNode) { return }
    Write-Step "Checking for Node.js >= $NodeMin (SPA templates)"

    $node = Get-Command node -ErrorAction SilentlyContinue
    if ($node) {
        $have = (& node --version 2>$null)
        if (Test-VersionGe $have $NodeMin) { Write-Detail "found $have — leaving it alone"; return }
    }

    $triple = Get-NodeTriple
    if (-not $triple) {
        Write-Warn @(
            "no Node.js build for this platform ($([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)) — skipping it.",
            'Only `rask new --template react|vue|svelte|...` needs Node. Outside Windows, use rask.sh.')
        return
    }

    $version = Get-NodeLtsVersion
    if (-not $version) {
        Write-Warn @("could not resolve the current Node LTS from $NodeDist/index.json — skipping it.")
        return
    }
    Write-Detail "installing Node $version ($triple) into $Prefix\node"
    if ($DryRun) { Write-Detail "(dry-run) download and unpack node-v$version-$triple.zip"; return }

    $name = "node-v$version-$triple.zip"
    $work = Join-Path ([System.IO.Path]::GetTempPath()) "rask-node-$([guid]::NewGuid())"
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    try {
        Invoke-WebRequest -Uri "$NodeDist/v$version/$name" -OutFile "$work\$name" -UseBasicParsing
        Invoke-WebRequest -Uri "$NodeDist/v$version/SHASUMS256.txt" -OutFile "$work\SHASUMS256.txt" -UseBasicParsing

        # Verify against the digest nodejs.org publishes. An unverified archive unpacked onto
        # someone's PATH is not something to ship.
        $want = (Get-Content "$work\SHASUMS256.txt" |
            Where-Object { ($_ -split '\s+')[-1] -eq $name } |
            ForEach-Object { ($_ -split '\s+')[0] } |
            Select-Object -First 1)
        if (-not $want) { Stop-Install 1 @("$name is not listed in SHASUMS256.txt for v$version.") }

        $have = (Get-FileHash "$work\$name" -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($have -ne $want.ToLowerInvariant()) {
            Stop-Install 1 @("checksum mismatch for $name.", "expected $want", "got      $have")
        }
        Write-Detail 'sha256 verified'

        $target = Join-Path $Prefix 'node'
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        Expand-Archive -Path "$work\$name" -DestinationPath $work -Force
        New-Item -ItemType Directory -Path $Prefix -Force | Out-Null
        Move-Item (Join-Path $work "node-v$version-$triple") $target
    }
    finally {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Step-Docker {
    Write-Step 'Checking for Docker (rask deploy)'
    if (Get-Command docker -ErrorAction SilentlyContinue) { Write-Detail 'found'; return }
    # Detected, never installed. The wording matches DockerProbe.cs:44-45.
    Write-Detail 'not found — only `rask deploy` and `rask db backup --remote` need it'
    Write-Detail '  winget install Docker.DockerDesktop'
}

function Step-Path {
    if ($NoPath) { return }
    Write-Step 'Putting rask on your PATH (user environment)'
    if ($DryRun) { Write-Detail '(dry-run) would add the rask directories to the user PATH'; return }

    # A 'User'-scope environment variable is a Windows registry concept. PowerShell runs on Linux and
    # macOS too — this script is reachable there, and the install gate exercises it there — where the
    # write silently does nothing, which is worse than saying so.
    if (-not $IsWindows) {
        Write-Warn @(
            'a per-user PATH cannot be set outside Windows — skipping.',
            'Add these to your shell profile, or use rask.sh, which does it for you:',
            "  $((Get-RaskPathEntries) -join ' ')")
        return
    }

    $sep = [IO.Path]::PathSeparator
    $current = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ($null -eq $current) { $current = '' }
    $entries = @($current -split $sep | Where-Object { $_ -ne '' })

    # Idempotent: only append what is missing, so a re-run does not grow the user PATH.
    $added = @()
    foreach ($dir in (Get-RaskPathEntries)) {
        if ($entries -notcontains $dir) { $entries += $dir; $added += $dir }
    }
    if ($added.Count -gt 0) {
        [Environment]::SetEnvironmentVariable('Path', ($entries -join $sep), 'User')
        foreach ($dir in $added) { Write-Detail "added $dir" }
    }
    else {
        Write-Detail 'already on PATH'
    }

    # PATH alone is not enough for a global tool when the SDK is per-user — see Test-LocalDotnet.
    if (Test-LocalDotnet) {
        [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $DotnetRoot, 'User')
        Write-Detail "set DOTNET_ROOT=$DotnetRoot"
    }
}

function Step-Verify {
    Write-Step 'Verifying'
    if ($DryRun) { Write-Detail '(dry-run) would run: rask --version && rask doctor'; return }

    $rask = Get-RaskCommandPath
    if (-not $rask) {
        Stop-Install 1 @(
            '`rask` was installed but could not be found.',
            "Expected it in $(Join-Path $DotnetRoot 'tools').")
    }

    # Deliberately not swallowed: a tool on disk that cannot find a runtime must fail here, loudly,
    # rather than be reported as installed.
    $version = (& $rask --version 2>&1)
    if ($LASTEXITCODE -ne 0) {
        Stop-Install 1 @(
            '`rask` is installed but will not run.',
            "$version",
            'If that mentions a missing .NET runtime, the SDK is outside the default location and',
            'DOTNET_ROOT is not set. Open a new terminal, or set it for this one:',
            "  `$env:DOTNET_ROOT = '$DotnetRoot'")
    }

    Write-Detail "rask $version"

    # `doctor` only exists from the release that added it, and -Version pins whatever the caller asked
    # for. An older CLI answers "Unknown command 'doctor'" and prints its whole help page, which is a
    # baffling way to end an install that worked — so ask what this one has first.
    if ((& $rask --help 2>$null) -match '\s+doctor\s+') {
        Write-Say ''
        & $rask doctor
    }
    else {
        Write-Detail '(this version has no `doctor` command — upgrade for the environment report)'
    }
}

function Step-Summary {
    Write-Say ''
    if ($script:Warnings.Count -gt 0) { Write-Say 'Installed, with warnings above.' }
    else { Write-Say 'Installed.' }
    if (-not $NoPath) {
        Write-Say ''
        Write-Say 'Open a new terminal to pick up the new PATH.'
    }
    Write-Say ''
    Write-Say 'Then:'
    Write-Say '  rask new MyApp; cd MyApp; rask dev'
}

function Invoke-Main {
    # Everything installed here is per-user and not on PATH yet in THIS session. Prepend it up front
    # so the later steps can see what Step-Dotnet just installed.
    $env:PATH = (@(Get-RaskPathEntries) + $env:PATH) -join [IO.Path]::PathSeparator
    if (-not $env:DOTNET_CLI_TELEMETRY_OPTOUT) { $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1' }
    if (-not $env:DOTNET_NOLOGO) { $env:DOTNET_NOLOGO = '1' }

    Write-Say 'Installing the rask CLI'
    Write-Say ''

    Step-Dotnet

    # Only now can we know whether the SDK is the per-user one — Step-Dotnet may have just created it.
    # Everything after this runs a global tool's apphost, which needs DOTNET_ROOT to find a runtime
    # outside the default location.
    if (Test-LocalDotnet) { $env:DOTNET_ROOT = $DotnetRoot }

    Step-Rask
    Step-Ef
    Step-WasmTools
    Step-Node
    Step-Docker
    Step-Path
    Step-Verify
    Step-Summary
}

Invoke-Main
