# Multi-stage build: publish the standalone WASM bundle on the .NET SDK image, then serve
# the static output from a tiny nginx image. A standalone Rask SPA has no ASP.NET host of
# its own — it's plain static files, so any static-file server works.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# The browser-wasm target needs the wasm-tools workload to publish.
RUN dotnet workload install wasm-tools
WORKDIR /src

# Restore first (cached layer): only the csproj invalidates it, so code edits reuse the cache.
COPY ["Company.RaskWasm.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish "Company.RaskWasm.csproj" -c Release -o /app --no-restore

FROM nginx:alpine
COPY --from=build /app/wwwroot /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
