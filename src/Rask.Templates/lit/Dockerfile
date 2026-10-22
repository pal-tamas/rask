# Multi-stage build: compile on the .NET SDK image with node beside it, run on the smaller aspnet
# runtime, which carries neither. The aspnet:10.0 image already runs as a non-root user and listens
# on port 8080 (ASPNETCORE_HTTP_PORTS=8080) — no extra hardening needed for a basic deploy.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Node, for the client build `dotnet publish` runs (Rask.Spa.Hosting).
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
 && curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
 && apt-get install -y --no-install-recommends nodejs \
 && rm -rf /var/lib/apt/lists/*

# Restore and install first (cached layers): only the csproj and the client's manifests invalidate
# them, so code edits reuse the cache.
COPY ["Company.RaskServer.csproj", "./"]
RUN dotnet restore
# The lockfile is the one the first build wrote: the template ships none, its versions are chosen at `rask new`.
COPY ["client/package*.json", "client/"]
RUN cd client && if [ -f package-lock.json ]; then npm ci --no-audit --no-fund; else npm install --no-audit --no-fund; fi
COPY . .
RUN dotnet publish "Company.RaskServer.csproj" -c Release -o /app --no-restore -p:RaskPdfChromiumCache=/chrome
# An app that references Rask.Pdf prints with the pinned Chrome for Testing headless shell, which that publish
# fetched into /chrome and checked against its SHA-256. Gathered into one folder for the runtime image; for any
# other app the folder stays empty and costs nothing.
RUN mkdir -p /chrome-shell \
 && shell="$(find /chrome -name chrome-headless-shell -type f 2>/dev/null | head -n 1)" \
 && if [ -n "$shell" ]; then cp -a "$(dirname "$shell")/." /chrome-shell/; fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=build /chrome-shell /opt/rask/chrome

# rask:if cqrs data
# The replicator binary Program.cs drives when Rask__Litestream__ReplicaUrl is set (see docs/sqlite.md).
COPY --from=litestream/litestream:0.5.17 /usr/local/bin/litestream /usr/local/bin/litestream


# rask:end
# A writable data directory for the SQLite database, owned by the image's non-root runtime user
# ($APP_UID). `rask deploy` mounts a named volume here and points the app at /data/app.db (via
# Rask:ConnectionStrings:App), so the database survives container replacement across redeploys. A fresh
# named volume inherits this directory's ownership, so the non-root app can create app.db in it.
USER root
RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
# Rask.Pdf's browser, only when the app has one: the libraries the headless shell links against and fonts with
# every Latin, Greek and Cyrillic letter (the app's own web fonts come with its stylesheet). About 220 MB more,
# most of it the GPU stack libgbm1 drags in; an app without Rask.Pdf skips the step.
RUN if [ -f /opt/rask/chrome/chrome-headless-shell ]; then \
      apt-get update \
      && apt-get install -y --no-install-recommends libglib2.0-0t64 libnspr4 libnss3 libatk1.0-0t64 libatk-bridge2.0-0t64 libatspi2.0-0t64 \
           libdbus-1-3 libx11-6 libxcb1 libxcomposite1 libxdamage1 libxext6 libxfixes3 libxrandr2 libgbm1 libexpat1 \
           libxkbcommon0 libasound2t64 fonts-dejavu-core fonts-liberation \
      && rm -rf /var/lib/apt/lists/*; \
    fi
USER $APP_UID

EXPOSE 8080
# The app calls UseHttpsRedirection(); inside the container no HTTPS port is configured,
# so it no-ops. Terminate TLS at your reverse proxy / ingress and forward plain HTTP to 8080.
ENTRYPOINT ["dotnet", "Company.RaskServer.dll"]
