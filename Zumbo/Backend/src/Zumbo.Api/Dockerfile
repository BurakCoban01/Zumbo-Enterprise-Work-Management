FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:9822a6201c64c9f38b8a190832a7ae21cd30150d509b3ea170fd83b4f4e6166a AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:a9330090b730c2abde8f3f43b3d1e24e4b8cba028de7bab1a7fdcd50cb7a3b7e AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Zumbo.sln", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["src/Zumbo.Api/Zumbo.Api.csproj", "src/Zumbo.Api/"]
COPY ["src/Zumbo.SharedKernel/Zumbo.SharedKernel.csproj", "src/Zumbo.SharedKernel/"]
COPY ["src/Zumbo.BuildingBlocks.Application/Zumbo.BuildingBlocks.Application.csproj", "src/Zumbo.BuildingBlocks.Application/"]
COPY ["src/Zumbo.BuildingBlocks.Infrastructure/Zumbo.BuildingBlocks.Infrastructure.csproj", "src/Zumbo.BuildingBlocks.Infrastructure/"]
COPY ["src/Zumbo.Persistence.PostgreSql/Zumbo.Persistence.PostgreSql.csproj", "src/Zumbo.Persistence.PostgreSql/"]
COPY ["src/Zumbo.Modules.Identity/Zumbo.Modules.Identity.csproj", "src/Zumbo.Modules.Identity/"]
COPY ["src/Zumbo.Modules.Organizations/Zumbo.Modules.Organizations.csproj", "src/Zumbo.Modules.Organizations/"]
COPY ["src/Zumbo.Modules.Teams/Zumbo.Modules.Teams.csproj", "src/Zumbo.Modules.Teams/"]
COPY ["src/Zumbo.Modules.Projects/Zumbo.Modules.Projects.csproj", "src/Zumbo.Modules.Projects/"]
COPY ["src/Zumbo.Modules.Boards/Zumbo.Modules.Boards.csproj", "src/Zumbo.Modules.Boards/"]
COPY ["src/Zumbo.Modules.WorkItems/Zumbo.Modules.WorkItems.csproj", "src/Zumbo.Modules.WorkItems/"]
COPY ["src/Zumbo.Modules.Workflows/Zumbo.Modules.Workflows.csproj", "src/Zumbo.Modules.Workflows/"]
COPY ["src/Zumbo.Modules.Notifications/Zumbo.Modules.Notifications.csproj", "src/Zumbo.Modules.Notifications/"]
COPY ["src/Zumbo.Modules.Audit/Zumbo.Modules.Audit.csproj", "src/Zumbo.Modules.Audit/"]
RUN dotnet restore "src/Zumbo.Api/Zumbo.Api.csproj"
COPY . .
RUN dotnet publish "src/Zumbo.Api/Zumbo.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/lib/zumbo/data-protection \
    && chown -R $APP_UID:$APP_UID /var/lib/zumbo
COPY --from=build /app/publish .
USER $APP_UID
HEALTHCHECK --interval=10s --timeout=5s --start-period=120s --retries=6 \
    CMD curl --fail --silent --show-error http://localhost:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "Zumbo.Api.dll"]
