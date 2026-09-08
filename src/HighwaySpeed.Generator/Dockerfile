# Build context is the repository root (see docker-compose.yml).

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/HighwaySpeed.Contracts/HighwaySpeed.Contracts.csproj src/HighwaySpeed.Contracts/
COPY src/HighwaySpeed.Generator/HighwaySpeed.Generator.csproj  src/HighwaySpeed.Generator/
RUN dotnet restore src/HighwaySpeed.Generator/HighwaySpeed.Generator.csproj

COPY src/HighwaySpeed.Contracts/ src/HighwaySpeed.Contracts/
COPY src/HighwaySpeed.Generator/  src/HighwaySpeed.Generator/
RUN dotnet publish src/HighwaySpeed.Generator/HighwaySpeed.Generator.csproj \
    -c Release -o /app --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENTRYPOINT ["dotnet", "HighwaySpeed.Generator.dll"]
