# The environment every gate runs `dotnet` in. Sourced, never executed.
#
# DOTNET_CLI_TELEMETRY_OPTOUT is here for speed, not privacy. The CLI spools each command's telemetry to
# ~/.dotnet/TelemetryStorageService and, as it exits, walks that folder opening and locking every file
# to send it. On a machine where the upload does not get through the spool only grows — 9,769 files
# here — and a gate is hundreds of `dotnet` processes leaving at once, each walking it against the
# others. Measured, twelve small test projects started together: 28 s, all twelve exiting in the same
# second long after their tests had finished; 9 s with this set. It is exported so the `dotnet msbuild`
# children that the build tests start inherit it too.
export DOTNET_CLI_TELEMETRY_OPTOUT=1
