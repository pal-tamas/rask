# Two toolchains, two stages: node builds the front end, the .NET SDK builds the host. The final
# image carries neither.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Node, for the client build the Rask.Spa.Hosting targets run during `dotnet publish`.
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
COPY --from=build /app/publish .

# 8080, matching the other templates so `rask deploy` maps the same port.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Company.RaskServer.dll"]
