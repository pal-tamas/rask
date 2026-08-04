# Multi-stage build: compile on the .NET SDK image, run on the smaller aspnet runtime.
# The aspnet:10.0 image already runs as a non-root user and listens on port 8080
# (ASPNETCORE_HTTP_PORTS=8080) — no extra hardening needed for a basic deploy.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached layer): only the csproj invalidates it, so code edits reuse the cache.
COPY ["Rask.Example.Shop.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish "Rask.Example.Shop.csproj" -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# The replicator binary Program.cs drives when Litestream__ReplicaUrl is set (see docs/sqlite.md).
COPY --from=litestream/litestream:0.3.13 /usr/local/bin/litestream /usr/local/bin/litestream


# A writable data directory for the SQLite database, owned by the image's non-root runtime user
# ($APP_UID). `rask deploy` mounts a named volume here and points the app at /data/app.db (via
# ConnectionStrings:App), so the database survives container replacement across redeploys. A fresh
# named volume inherits this directory's ownership, so the non-root app can create app.db in it.
USER root
RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
USER $APP_UID

EXPOSE 8080
# The app calls UseHttpsRedirection(); inside the container no HTTPS port is configured,
# so it no-ops. Terminate TLS at your reverse proxy / ingress and forward plain HTTP to 8080.
ENTRYPOINT ["dotnet", "Rask.Example.Shop.dll"]
