# The Linux box `rask dev` sets up, as a throwaway container.
#
# Deliberately shaped like a developer's machine rather than a build agent: an ordinary user with
# passwordless sudo, so every `sudo -n` in LinuxDevHostPlatform is exercised for real rather than being
# skipped because the test happened to run as root. Running as root would also hide the one assertion
# that matters most about the sysctl — root can bind 443 whether or not it was ever set.
ARG SDK=mcr.microsoft.com/dotnet/sdk:10.0
FROM ${SDK}

# ca-certificates  — the anchor directory and update-ca-certificates
# libnss3-tools    — certutil, which is how Chrome and Firefox are reached
# curl             — the final handshake, validated against the system store
# sudo             — the elevation path the platform actually uses
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates libnss3-tools curl sudo \
    && rm -rf /var/lib/apt/lists/*

RUN useradd --create-home --shell /bin/bash dev \
    && echo 'dev ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/dev \
    && chmod 0440 /etc/sudoers.d/dev

USER dev
WORKDIR /repo

# Outputs go somewhere the mounted repo cannot see: the host's obj/ and bin/ hold macOS/arm64 artifacts
# from the same projects, and letting a Linux build write over them would corrupt both.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    RASK_DEVHOST_E2E=1

ENTRYPOINT ["/bin/bash", "-lc"]
