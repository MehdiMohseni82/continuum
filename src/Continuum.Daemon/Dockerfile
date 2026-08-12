# Build context is the repo root (see infra/docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# The daemon builds against Continuum.Client only — it has no use for the database stack in Core,
# which is the whole point of that split.
COPY src/Continuum.Client/Continuum.Client.csproj src/Continuum.Client/
COPY src/Continuum.Daemon/Continuum.Daemon.csproj src/Continuum.Daemon/
RUN dotnet restore src/Continuum.Daemon/Continuum.Daemon.csproj

COPY src/Continuum.Client/ src/Continuum.Client/
COPY src/Continuum.Daemon/ src/Continuum.Daemon/
RUN dotnet publish src/Continuum.Daemon/Continuum.Daemon.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Continuum.Daemon.dll"]
