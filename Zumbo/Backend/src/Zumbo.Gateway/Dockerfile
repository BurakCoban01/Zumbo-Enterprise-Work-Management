FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:149d139eb6f11b9be8bc99c4d8cbd14f662d64d83359079ebb59e5f4a97bbefe AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:5dfdb3b19e6900a3dc449760547a0a3fdd1f47900062d274d57e7ab725484c16 AS build
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
