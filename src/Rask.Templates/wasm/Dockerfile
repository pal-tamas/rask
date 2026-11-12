# syntax=docker/dockerfile:1
# Multi-stage build: publish the standalone WASM bundle on the .NET SDK image, then serve
# the static output from a tiny nginx image. A standalone Rask SPA has no ASP.NET host of
# its own — it's plain static files, so any static-file server works.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# The browser-wasm target needs the wasm-tools workload to publish.
RUN dotnet workload install wasm-tools
WORKDIR /src

# Restore first (cached layer): only the csproj invalidates it, so code edits reuse the cache.
# NuGet's packages live in a BuildKit cache mount (Docker 23+, Podman 4+), which outlives the layers: the
# publish below finds them there, and a changed manifest downloads only what changed.
# With what restore reads beside it: nuget.config (a private feed, source mapping), Directory.*.props, global.json.
# Each is a pattern, so one the project does not have copies nothing instead of failing the build.
COPY ["Company.RaskServer.csproj", "[Nn]u[Gg]et.[Cc]onfig", "Directory.*", "global.json*", "./"]
RUN --mount=type=cache,id=rask-nuget,target=/root/.nuget/packages \
    dotnet restore
COPY . .
# The publish restores again: a no-op while the cache holds the packages, a download only if the builder pruned
# them. The Tailwind CLI the stylesheet compiles with, and tsgo for scoped TypeScript, are cached the same way, once
# per version: a deploy then never reaches GitHub or npm for them.
RUN --mount=type=cache,id=rask-nuget,target=/root/.nuget/packages \
    --mount=type=cache,id=rask-tailwind,target=/root/.rask/tailwind \
    --mount=type=cache,id=rask-typescript,target=/root/.rask/typescript \
    dotnet publish "Company.RaskServer.csproj" -c Release -o /app

FROM nginx:alpine
COPY --from=build /app/wwwroot /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
