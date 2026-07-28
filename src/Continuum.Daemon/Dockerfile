# Build context is the repo root (see infra/docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Continuum.Core/Continuum.Core.csproj src/Continuum.Core/
COPY src/Continuum.Daemon/Continuum.Daemon.csproj src/Continuum.Daemon/
RUN dotnet restore src/Continuum.Daemon/Continuum.Daemon.csproj

COPY src/Continuum.Core/ src/Continuum.Core/
COPY src/Continuum.Daemon/ src/Continuum.Daemon/
RUN dotnet publish src/Continuum.Daemon/Continuum.Daemon.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Continuum.Daemon.dll"]
