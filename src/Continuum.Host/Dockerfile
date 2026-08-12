# Build context is the repo root (see infra/docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore against just the project files first for better layer caching.
COPY src/Continuum.Client/Continuum.Client.csproj src/Continuum.Client/
COPY src/Continuum.Core/Continuum.Core.csproj src/Continuum.Core/
COPY src/Continuum.Host/Continuum.Host.csproj src/Continuum.Host/
RUN dotnet restore src/Continuum.Host/Continuum.Host.csproj

COPY src/Continuum.Client/ src/Continuum.Client/
COPY src/Continuum.Core/ src/Continuum.Core/
COPY src/Continuum.Host/ src/Continuum.Host/
RUN dotnet publish src/Continuum.Host/Continuum.Host.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "Continuum.Host.dll"]
