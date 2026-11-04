# Build context is the repository root (see docker-compose.yml), because this service
# references shared projects under src/Shared.

# ---- Stage 1: build with the full .NET SDK ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project files first. Docker caches this layer, so `dotnet restore`
# re-runs only when a .csproj or the package versions change, not on every code edit.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Shared/BuildingBlocks/BuildingBlocks.csproj src/Shared/BuildingBlocks/
COPY src/Shared/Contracts/Contracts.csproj src/Shared/Contracts/
COPY src/Services/Catalog.Api/Catalog.Api.csproj src/Services/Catalog.Api/
RUN dotnet restore src/Services/Catalog.Api/Catalog.Api.csproj

COPY src/ src/
RUN dotnet publish src/Services/Catalog.Api/Catalog.Api.csproj -c Release -o /app/publish --no-restore

# ---- Stage 2: run on the small ASP.NET runtime image (no SDK, no source code) ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Run as the non-root user that the .NET images provide.
USER $APP_UID
# .NET 8+ images listen on port 8080 by default.
EXPOSE 8080
ENTRYPOINT ["dotnet", "Catalog.Api.dll"]
