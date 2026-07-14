# Multi-stage build for the two-project solution: build both the WASM client and its ASP.NET
# Host, then run the Host (which serves the baked client bundle) on the aspnet runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# The browser-wasm client the Host bakes in needs the wasm-tools workload to publish.
RUN dotnet workload install wasm-tools
WORKDIR /src

# Copy the whole solution — the Host's ProjectReference to the WASM client makes a
# csproj-only restore layer brittle, so restore happens inside publish for correctness.
COPY . .
RUN dotnet publish "Company.RaskWasmHosted.Host/Company.RaskWasmHosted.Host.csproj" -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
# The Host calls UseHttpsRedirection(); inside the container no HTTPS port is configured,
# so it no-ops. Terminate TLS at your reverse proxy / ingress and forward plain HTTP to 8080.
ENTRYPOINT ["dotnet", "Company.RaskWasmHosted.Host.dll"]
