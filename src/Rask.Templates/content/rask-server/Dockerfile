# Multi-stage build: compile on the .NET SDK image, run on the smaller aspnet runtime.
# The aspnet:10.0 image already runs as a non-root user and listens on port 8080
# (ASPNETCORE_HTTP_PORTS=8080) — no extra hardening needed for a basic deploy.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached layer): only the csproj invalidates it, so code edits reuse the cache.
COPY ["Company.RaskServer.csproj", "./"]
RUN dotnet restore
COPY . .
RUN dotnet publish "Company.RaskServer.csproj" -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
# The app calls UseHttpsRedirection(); inside the container no HTTPS port is configured,
# so it no-ops. Terminate TLS at your reverse proxy / ingress and forward plain HTTP to 8080.
ENTRYPOINT ["dotnet", "Company.RaskServer.dll"]
