# Two toolchains, two stages: node builds the front end, the .NET SDK builds the host. Unlike the
# TypeScript-SPA template, the FINAL image keeps node — a Next.js app has a server
# of its own, and this host supervises it as a child process for the life of the container.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
 && curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
 && apt-get install -y --no-install-recommends nodejs \
 && rm -rf /var/lib/apt/lists/*

# Restore against the manifests alone, so a source-only change does not invalidate this layer.
COPY ["Company.RaskServer/Company.RaskServer.csproj", "Company.RaskServer/"]
RUN dotnet restore "Company.RaskServer/Company.RaskServer.csproj"

COPY ["Company.RaskServer/client/package.json", "Company.RaskServer/client/package-lock.json*", "Company.RaskServer/client/"]
RUN cd Company.RaskServer/client && npm ci --no-audit --no-fund || npm install --no-audit --no-fund

COPY . .
RUN dotnet publish "Company.RaskServer/Company.RaskServer.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# The one thing this image has that the others do not. The front end's server runs here, bound to
# loopback and supervised by the host — publishing this container's port cannot expose it.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
 && curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
 && apt-get install -y --no-install-recommends nodejs \
 && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# rask:if cqrs data
# The replicator binary Program.cs drives when Litestream__ReplicaUrl is set (see docs/sqlite.md).
COPY --from=litestream/litestream:0.3.13 /usr/local/bin/litestream /usr/local/bin/litestream

# rask:end
# 8080, matching the other templates so `rask deploy` maps the same port. The framework's own
# server is NOT exposed: it listens on 127.0.0.1 and only Kestrel reaches it.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Company.RaskServer.dll"]
