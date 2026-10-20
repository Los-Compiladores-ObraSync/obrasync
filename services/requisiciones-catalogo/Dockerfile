# Etapa 1: Base de ejecución (Runtime liviano de .NET 8)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# Etapa 2: Compilación y restauración con SDK de .NET 8
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copiamos primero los archivos .csproj para aprovechar el cache de capas de Docker en 'dotnet restore'
COPY ["ObraSync.Requisiciones.Api/ObraSync.Requisiciones.Api.csproj", "ObraSync.Requisiciones.Api/"]
COPY ["ObraSync.Requisiciones.Application/ObraSync.Requisiciones.Application.csproj", "ObraSync.Requisiciones.Application/"]
COPY ["ObraSync.Requisiciones.Domain/ObraSync.Requisiciones.Domain.csproj", "ObraSync.Requisiciones.Domain/"]
COPY ["ObraSync.Requisiciones.Infrastructure/ObraSync.Requisiciones.Infrastructure.csproj", "ObraSync.Requisiciones.Infrastructure/"]

RUN dotnet restore "ObraSync.Requisiciones.Api/ObraSync.Requisiciones.Api.csproj"

# Copiamos todo el código fuente del servicio y compilamos
COPY . .
WORKDIR "/src/ObraSync.Requisiciones.Api"
RUN dotnet build "ObraSync.Requisiciones.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Etapa 3: Publicación de binarios
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ObraSync.Requisiciones.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Etapa 4: Imagen final productiva
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ObraSync.Requisiciones.Api.dll"]
