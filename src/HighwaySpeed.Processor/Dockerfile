# Build context is the repository root (see docker-compose.yml).

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first, on just the project files, so this layer is cached until a
# dependency actually changes.
COPY Directory.Build.props ./
COPY src/HighwaySpeed.Contracts/HighwaySpeed.Contracts.csproj src/HighwaySpeed.Contracts/
COPY src/HighwaySpeed.Processor/HighwaySpeed.Processor.csproj  src/HighwaySpeed.Processor/
RUN dotnet restore src/HighwaySpeed.Processor/HighwaySpeed.Processor.csproj

# Now the sources.
COPY src/HighwaySpeed.Contracts/ src/HighwaySpeed.Contracts/
COPY src/HighwaySpeed.Processor/  src/HighwaySpeed.Processor/
RUN dotnet publish src/HighwaySpeed.Processor/HighwaySpeed.Processor.csproj \
    -c Release -o /app --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Ports are configured explicitly in Program.cs (ConfigureKestrel); clear the
# base image's defaults so they don't add a second, conflicting endpoint.
ENV ASPNETCORE_URLS= \
    ASPNETCORE_HTTP_PORTS=

EXPOSE 5080
EXPOSE 5081

ENTRYPOINT ["dotnet", "HighwaySpeed.Processor.dll"]
