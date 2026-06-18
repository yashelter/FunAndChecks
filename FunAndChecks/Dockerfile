FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копируем ТОЛЬКО то, что нужно для API
COPY ["FunAndChecks/FunAndChecks.csproj", "FunAndChecks/"]
RUN dotnet restore "FunAndChecks/FunAndChecks.csproj"

# Копируем ТОЛЬКО исходный код API
COPY FunAndChecks/ ./FunAndChecks/

# Публикуем
WORKDIR /src/FunAndChecks
RUN dotnet publish "FunAndChecks.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Этап 2: Финальный образ
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

USER root

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=5 CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "FunAndChecks.dll"]