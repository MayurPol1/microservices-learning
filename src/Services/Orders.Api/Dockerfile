# Same two-stage pattern as Catalog.Api/Dockerfile (see the comments there).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Shared/BuildingBlocks/BuildingBlocks.csproj src/Shared/BuildingBlocks/
COPY src/Shared/Contracts/Contracts.csproj src/Shared/Contracts/
COPY src/Services/Orders.Api/Orders.Api.csproj src/Services/Orders.Api/
RUN dotnet restore src/Services/Orders.Api/Orders.Api.csproj

COPY src/ src/
RUN dotnet publish src/Services/Orders.Api/Orders.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Orders.Api.dll"]
