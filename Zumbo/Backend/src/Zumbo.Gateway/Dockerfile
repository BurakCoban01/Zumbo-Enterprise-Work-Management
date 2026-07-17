FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:9822a6201c64c9f38b8a190832a7ae21cd30150d509b3ea170fd83b4f4e6166a AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:a9330090b730c2abde8f3f43b3d1e24e4b8cba028de7bab1a7fdcd50cb7a3b7e AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Directory.Packages.props", "./"]
COPY ["src/Zumbo.Gateway/Zumbo.Gateway.csproj", "src/Zumbo.Gateway/"]
RUN dotnet restore "src/Zumbo.Gateway/Zumbo.Gateway.csproj"
COPY . .
RUN dotnet publish "src/Zumbo.Gateway/Zumbo.Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
USER $APP_UID
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=6 \
    CMD curl --fail --silent --show-error http://localhost:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "Zumbo.Gateway.dll"]
