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
COPY ["client/package.json", "client/package-lock.json", "client/"]
RUN cd client && npm ci --no-audit --no-fund
COPY . .
RUN dotnet publish "Company.RaskServer.csproj" -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# rask:if cqrs data
# The replicator binary Program.cs drives when Rask__Litestream__ReplicaUrl is set (see docs/sqlite.md).
COPY --from=litestream/litestream:0.3.13 /usr/local/bin/litestream /usr/local/bin/litestream


# rask:end
# A writable data directory for the SQLite database, owned by the image's non-root runtime user
# ($APP_UID). `rask deploy` mounts a named volume here and points the app at /data/app.db (via
# Rask:ConnectionStrings:App), so the database survives container replacement across redeploys. A fresh
# named volume inherits this directory's ownership, so the non-root app can create app.db in it.
USER root
RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
USER $APP_UID

EXPOSE 8080
# The app calls UseHttpsRedirection(); inside the container no HTTPS port is configured,
# so it no-ops. Terminate TLS at your reverse proxy / ingress and forward plain HTTP to 8080.
ENTRYPOINT ["dotnet", "Company.RaskServer.dll"]
