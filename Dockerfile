# Этап 1: Сборка с использованием полного .NET SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем .csproj файлы для эффективного кэширования слоёв
COPY ["FunAndChecks/FunAndChecks.csproj", "FunAndChecks/"]
COPY ["FunAndChecks.Domain/FunAndChecks.Domain.csproj", "FunAndChecks.Domain/"]
COPY ["FunAndChecks.Application/FunAndChecks.Application.csproj", "FunAndChecks.Application/"]
COPY ["FunAndChecks.Infrastructure/FunAndChecks.Infrastructure.csproj", "FunAndChecks.Infrastructure/"]
COPY ["Frontend/Frontend.csproj", "Frontend/"]
COPY ["Frontend.Shared/Frontend.Shared.csproj", "Frontend.Shared/"]
COPY ["Frontend.Admin/Frontend.Admin.csproj", "Frontend.Admin/"]
COPY ["Frontend.Student/Frontend.Student.csproj", "Frontend.Student/"]

# Восстанавливаем NuGet-пакеты
RUN dotnet restore "FunAndChecks/FunAndChecks.csproj"

# Копируем исходный код нужных проектов
COPY ["FunAndChecks/", "FunAndChecks/"]
COPY ["FunAndChecks.Domain/", "FunAndChecks.Domain/"]
COPY ["FunAndChecks.Application/", "FunAndChecks.Application/"]
COPY ["FunAndChecks.Infrastructure/", "FunAndChecks.Infrastructure/"]
COPY ["Frontend/", "Frontend/"]
COPY ["Frontend.Shared/", "Frontend.Shared/"]
COPY ["Frontend.Admin/", "Frontend.Admin/"]
COPY ["Frontend.Student/", "Frontend.Student/"]

# Публикуем основной API проект
RUN dotnet publish "FunAndChecks/FunAndChecks.csproj" -c Release -o /app/publish /p:UseAppHost=false


# Этап 2: Создание финального образа
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# curl — для healthcheck; postgresql-client-17 (pg_dump) — для ручки резервного копирования.
# Ставим клиент из официального репозитория PGDG, чтобы версия pg_dump совпадала с сервером PG 17.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates gnupg \
    && install -d /usr/share/postgresql-common/pgdg \
    && curl -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc --fail https://www.postgresql.org/media/keys/ACCC4CF8.asc \
    && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt $(. /etc/os-release && echo $VERSION_CODENAME)-pgdg main" > /etc/apt/sources.list.d/pgdg.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends postgresql-client-17 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/backups && chown app:app /app/backups
USER app

ENTRYPOINT ["dotnet", "FunAndChecks.dll"]
