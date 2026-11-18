# Same two-stage pattern as Catalog.Api/Dockerfile (see the comments there).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Shared/BuildingBlocks/BuildingBlocks.csproj src/Shared/BuildingBlocks/
COPY src/Services/Identity.Api/Identity.Api.csproj src/Services/Identity.Api/
RUN dotnet restore src/Services/Identity.Api/Identity.Api.csproj

COPY src/ src/
RUN dotnet publish src/Services/Identity.Api/Identity.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Identity.Api.dll"]
