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
COPY ["ObraSync.Asistente.Api/ObraSync.Asistente.Api.csproj", "ObraSync.Asistente.Api/"]
COPY ["ObraSync.Asistente.Application/ObraSync.Asistente.Application.csproj", "ObraSync.Asistente.Application/"]
COPY ["ObraSync.Asistente.Infrastructure/ObraSync.Asistente.Infrastructure.csproj", "ObraSync.Asistente.Infrastructure/"]

RUN dotnet restore "ObraSync.Asistente.Api/ObraSync.Asistente.Api.csproj"

# Copiamos todo el código fuente del servicio y compilamos
COPY . .
WORKDIR "/src/ObraSync.Asistente.Api"
RUN dotnet build "ObraSync.Asistente.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Etapa 3: Publicación de binarios
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ObraSync.Asistente.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Etapa 4: Imagen final
# La clave de IA (Ai__ApiKey) y la URL del gateway de IA (Ai__BaseUrl) llegan por variables de entorno: NUNCA se escriben acá.
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ObraSync.Asistente.Api.dll"]
