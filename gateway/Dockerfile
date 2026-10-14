# Etapa 1: Base de ejecución (Runtime liviano de .NET 8)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# Etapa 2: Compilación y restauración con SDK de .NET 8
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copiamos primero el .csproj para aprovechar el cache de capas de Docker en 'dotnet restore'
COPY ["src/ObraSync.Gateway/ObraSync.Gateway.csproj", "src/ObraSync.Gateway/"]
RUN dotnet restore "src/ObraSync.Gateway/ObraSync.Gateway.csproj"

# Copiamos el resto del código fuente del gateway y compilamos
COPY . .
WORKDIR "/src/src/ObraSync.Gateway"
RUN dotnet build "ObraSync.Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Etapa 3: Publicación de binarios
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ObraSync.Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Etapa 4: Imagen final
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ObraSync.Gateway.dll"]
